using System;
using System.Collections.Generic;
using System.Linq;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MailProcessor.Utilities;

namespace MailProcessor.Connectivity
{
    /// <summary>
    /// Подключение к IMAP-серверу.
    /// Поддерживает явное указание порта/SSL из конфига или авто-подбор.
    /// Кэширует последнюю успешную комбинацию для ускорения повторных подключений.
    /// </summary>
    public static class ImapConnector
    {
        // Кэшируем последнюю успешную комбинацию
        private static int? _cachedPort;
        private static SecureSocketOptions? _cachedOption;

        // Приоритетный порядок для авто-подбора
        private static readonly Tuple<int, SecureSocketOptions>[] AutoAttempts =
        {
            Tuple.Create(993, SecureSocketOptions.SslOnConnect),
            Tuple.Create(993, SecureSocketOptions.StartTls),
            Tuple.Create(143, SecureSocketOptions.StartTls),
            Tuple.Create(143, SecureSocketOptions.None),
            Tuple.Create(143, SecureSocketOptions.SslOnConnect),
            Tuple.Create(993, SecureSocketOptions.None),
        };

        /// <summary>
        /// Ключевые слова, означающие ошибку аутентификации.
        /// Используются для распознавания ошибок логина/пароля,
        /// поскольку MailKit 4.x может выбрасывать разные типы исключений.
        /// </summary>
        private static readonly string[] AuthErrorKeywords =
        {
            "invalid credentials",
            "authentication failed",
            "login failed",
            "auth",
            "Invalid credentials or IMAP is disabled",
        };

        /// <summary>
        /// Подключается к IMAP-серверу.
        /// Если в конфиге указан конкретный порт — использует его.
        /// Если sslOption = "Auto" и порт = 0 — перебирает варианты.
        /// </summary>
        public static ImapClient Connect(string server, string username, string password,
            int configuredPort, string configuredSslOption)
        {
            SecureSocketOptions? explicitOption = ParseSslOption(configuredSslOption);

            // Если и порт, и SSL указаны явно — используем их напрямую
            if (configuredPort > 0 && explicitOption.HasValue && explicitOption.Value != SecureSocketOptions.Auto)
            {
                return TryConnect(server, username, password, configuredPort, explicitOption.Value);
            }

            // Если есть кэшированные параметры — пробуем их
            if (_cachedPort.HasValue && _cachedOption.HasValue)
            {
                try
                {
                    var client = TryConnect(server, username, password, _cachedPort.Value, _cachedOption.Value);
                    Logger.Debug("Подключение по кэшированным параметрам: порт={0}, опция={1}", _cachedPort.Value, _cachedOption.Value);
                    return client;
                }
                catch (Exception ex)
                {
                    if (IsAuthError(ex))
                        throw;

                    Logger.Debug("Кэшированные параметры не сработали: {0}", ex.Message);
                    _cachedPort = null;
                    _cachedOption = null;
                }
            }

            // Авто-подбор
            return AutoConnect(server, username, password, configuredPort, explicitOption);
        }

        /// <summary>
        /// Прямое подключение с указанными параметрами.
        /// </summary>
        private static ImapClient TryConnect(string server, string username, string password,
            int port, SecureSocketOptions option)
        {
            var client = new ImapClient();
            try
            {
                Logger.Debug("Подключение: сервер={0}, порт={1}, опция={2}", server, port, option);
                client.Connect(server, port, option);
                client.Authenticate(username, password);

                _cachedPort = port;
                _cachedOption = option;
                Logger.Info("Подключение успешно: порт={0}, опция={1}", port, option);
                return client;
            }
            catch (Exception ex)
            {
                if (IsAuthError(ex))
                {
                    client.Dispose();
                    Logger.Error("Ошибка аутентификации: {0}", ex.Message);
                    throw new InvalidOperationException(
                        string.Format("Ошибка аутентификации для {0} на {1}: {2}. Проверьте логин/пароль или включён ли IMAP.", username, server, ex.Message),
                        ex);
                }
                client.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Автоподбор порта и SSL.
        /// Фильтрует варианты по заданному порту или SSL-опции из конфига.
        /// </summary>
        private static ImapClient AutoConnect(string server, string username, string password,
            int configuredPort, SecureSocketOptions? configuredOption)
        {
            Exception lastError = null;

            // Фильтруем попытки по заданному порту или SSL-опции
            IEnumerable<Tuple<int, SecureSocketOptions>> attempts = AutoAttempts;

            if (configuredPort > 0)
                attempts = AutoAttempts.Where(a => a.Item1 == configuredPort);
            else if (configuredOption.HasValue && configuredOption.Value != SecureSocketOptions.Auto)
                attempts = AutoAttempts.Where(a => a.Item2 == configuredOption.Value);

            var attemptList = attempts.ToList();

            foreach (var attempt in attemptList)
            {
                int port = attempt.Item1;
                SecureSocketOptions option = attempt.Item2;

                ImapClient client = null;
                try
                {
                    client = new ImapClient();
                    Logger.Debug("Попытка: сервер={0}, порт={1}, опция={2}", server, port, option);
                    client.Connect(server, port, option);
                    client.Authenticate(username, password);

                    _cachedPort = port;
                    _cachedOption = option;
                    Logger.Info("Подключение успешно: порт={0}, опция={1}", port, option);
                    return client;
                }
                catch (Exception ex)
                {
                    if (IsAuthError(ex))
                    {
                        if (client != null) client.Dispose();
                        Logger.Error("Ошибка аутентификации: {0}", ex.Message);
                        throw new InvalidOperationException(
                            string.Format("Ошибка аутентификации для {0} на {1}: {2}", username, server, ex.Message),
                            ex);
                    }

                    Logger.Debug("Не удалось: порт={0}, опция={1} — {2}", port, option, ex.Message);
                    if (client != null) client.Dispose();
                    lastError = ex;
                }
            }

            throw new InvalidOperationException(
                string.Format("Не удалось подключиться к {0}. Последняя ошибка: {1}", server, lastError != null ? lastError.Message : "нет"),
                lastError);
        }

        /// <summary>
        /// Парсинг строки SSL-опции из конфига.
        /// </summary>
        private static SecureSocketOptions? ParseSslOption(string option)
        {
            if (string.IsNullOrWhiteSpace(option)) return null;

            string lower = option.Trim().ToLowerInvariant();
            if (lower == "sslonconnect") return SecureSocketOptions.SslOnConnect;
            if (lower == "starttls") return SecureSocketOptions.StartTls;
            if (lower == "none") return SecureSocketOptions.None;
            if (lower == "auto") return SecureSocketOptions.Auto;
            return null;
        }

        /// <summary>
        /// Проверяет, является ли исключение ошибкой аутентификации.
        /// Проверка по тексту сообщения, а не по типу исключения,
        /// поскольку MailKit 4.x может выбрасывать SaslException и другие типы.
        /// </summary>
        private static bool IsAuthError(Exception ex)
        {
            if (string.IsNullOrEmpty(ex.Message)) return false;
            string lower = ex.Message.ToLowerInvariant();
            foreach (var keyword in AuthErrorKeywords)
            {
                if (lower.Contains(keyword.ToLowerInvariant()))
                    return true;
            }
            return false;
        }

        // ────────────────────── Работа с папками и поиск ──────────────────────

        /// <summary>
        /// Открывает указанную папку IMAP. "INBOX" → client.Inbox.
        /// </summary>
        public static IMailFolder OpenFolder(ImapClient client, string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) ||
                folderPath.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            {
                return client.Inbox;
            }

            return client.GetFolder(folderPath);
        }

        /// <summary>
        /// Ищет непрочитанные письма в папке с ограничением по дате и количеству.
        /// </summary>
        public static IList<UniqueId> SearchUnread(IMailFolder folder, int daysToSearch, int maxEmails)
        {
            var searchQuery = SearchQuery.NotSeen;

            if (daysToSearch > 0)
            {
                DateTime since = DateTime.Now.AddDays(-daysToSearch);
                searchQuery = searchQuery.And(SearchQuery.DeliveredAfter(since));
                Logger.Info("Поиск непрочитанных писем за последние {0} дней...", daysToSearch);
            }
            else
            {
                Logger.Info("Поиск всех непрочитанных писем (без ограничения по дате)...");
            }

            var uids = folder.Search(searchQuery);
            Logger.Info("Найдено непрочитанных писем: {0}", uids.Count);

            if (uids.Count == 0)
                return uids;

            if (maxEmails > 0 && uids.Count > maxEmails)
            {
                Logger.Info("Ограничение: обрабатываем только {0} из {1} писем.", maxEmails, uids.Count);
                return new UniqueIdSet(uids.Take(maxEmails));
            }

            return uids;
        }
    }
}
