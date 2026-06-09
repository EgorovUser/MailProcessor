using CredentialManagement;
using MailKit.Net.Imap;
using MailProcessor.Configuration;
using MailProcessor.Connectivity;
using MailProcessor.Services;
using MailProcessor.Utilities;
using System;
using System.IO;

/// <summary>
/// MailProcessor — анализ почтового ящика через IMAP.
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        string exeDir = Directory.GetCurrentDirectory();
        string configPath = Path.Combine(exeDir, "config.json");

        // ── Загрузка конфига ──
        AppConfig config;
        try
        {
            config = ConfigLoader.Load(configPath);
        }
        catch (Exception ex)
        {
            string fallbackLogDir = Path.Combine(exeDir, "logs");
            Directory.CreateDirectory(fallbackLogDir);
            string fallbackLog = Path.Combine(fallbackLogDir, DateTime.Now.ToString("dd_MM_yyyy_HH_mm") + ".txt");
            Logger.Initialize(fallbackLog, LogLevel.Info);
            Logger.Error("Ошибка загрузки конфига: {0}", ex.Message);
            return 1;
        }

        // ── Инициализация логгера ──
        LogLevel logLevel = ConfigLoader.ParseLogLevel(config.logging.level);
        string logDir = Path.Combine(exeDir, config.logging.folder);
        string logPath = Path.Combine(logDir, DateTime.Now.ToString("dd_MM_yyyy_HH_mm") + ".txt");
        Logger.Initialize(logPath, logLevel);

        Logger.Info("══════════════════════════════════════════════");
        Logger.Info("MailProcessor запущен. LogLevel: {0}", logLevel);
        Logger.Info("Конфиг: {0}", configPath);

        if (config.processing.dryRun)
            Logger.Info("═══ DRY RUN — файлы НЕ сохраняются ═══");

        Logger.Info("IMAP папка: {0}", config.imap.folder);

        // ── Получение учётных данных ──
        string username;
        string password;
        try
        {
            var creds = GetCredentials(config.imap.credentialTarget);
            username = creds.Item1;
            password = creds.Item2;
            if (username == null)
            {
                Logger.Error("Не удалось получить credentials для '{0}'. Завершение.",
                    config.imap.credentialTarget);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Ошибка получения credentials: {0}", ex.Message);
            return 1;
        }

        // ── Создание папки вывода ──
        if (!config.processing.dryRun)
        {
            try
            {
                Directory.CreateDirectory(config.processing.outputFolder);
                Logger.Info("Папка вывода: {0}", config.processing.outputFolder);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось создать папку вывода '{0}': {1}",
                    config.processing.outputFolder, ex.Message);
                return 1;
            }
        }

        // ── Подключение к IMAP и обработка ──
        try
        {
            ImapClient client = ImapConnector.Connect(
                config.imap.server,
                username,
                password,
                config.imap.port,
                config.imap.sslOption);

            using (client)
            {
                Logger.Info("Подключено к {0}", config.imap.server);

                EmailProcessor.ProcessEmails(client, config);

                client.Disconnect(true);
                Logger.Info("Отключено от {0}", config.imap.server);
            }
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("аутентификации"))
            {
                Logger.Error("Не удалось подключиться к {0}: проверьте логин/пароль и включён ли IMAP",
                    config.imap.server);
            }
            else
            {
                Logger.Error("Ошибка при работе с сервером {0}: {1}", config.imap.server, ex.Message);
            }
            Logger.Debug("Стек: {0}", ex.StackTrace);
            return 1;
        }
        catch (Exception ex)
        {
            Logger.Error("Ошибка при работе с сервером {0}: {1}", config.imap.server, ex.Message);
            Logger.Debug("Стек: {0}", ex.StackTrace);
            return 1;
        }

        Logger.Info("MailProcessor завершён успешно.");
        return 0;
    }

    private static Tuple<string, string> GetCredentials(string target)
    {
        Logger.Debug("Загрузка credentials для target='{0}'", target);

        try
        {
            var cred = new Credential();
            cred.Target = target;
            cred.Load();

            if (string.IsNullOrEmpty(cred.Username) || string.IsNullOrEmpty(cred.Password))
            {
                Logger.Error("Данные для '{0}' отсутствуют в Credential Manager", target);
                return Tuple.Create((string)null, (string)null);
            }

            Logger.Debug("Credentials загружены: пользователь={0}", cred.Username);
            return Tuple.Create(cred.Username, cred.Password);
        }
        catch (Exception ex)
        {
            Logger.Error("Ошибка загрузки credentials для '{0}': {1}", target, ex.Message);
            return Tuple.Create((string)null, (string)null);
        }
    }
}