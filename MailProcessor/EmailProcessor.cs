using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

/// <summary>
/// Обработка писем из IMAP: скачивание вложений с фильтром по расширениям,
/// парсинг HTML-таблиц при отсутствии подходящих вложений.
/// </summary>
public static class EmailProcessor
{
    /// <summary>
    /// Основной метод: ищет непрочитанные письма, обрабатывает их.
    /// </summary>
    public static void ProcessEmails(ImapClient client, Configuration.AppConfig config)
    {
        var processing = config.processing;
        var attachCfg = config.attachments;
        var tableCfg = config.tableParsing;

        var inbox = client.Inbox;
        inbox.Open(FolderAccess.ReadWrite);

        // Поиск непрочитанных писем
        var searchQuery = SearchQuery.NotSeen;

        if (processing.daysToSearch > 0)
        {
            DateTime since = DateTime.Now.AddDays(-processing.daysToSearch);
            searchQuery = searchQuery.And(SearchQuery.DeliveredAfter(since));
            Logger.Info("Поиск непрочитанных писем за последние {0} дней...", processing.daysToSearch);
        }
        else
        {
            Logger.Info("Поиск всех непрочитанных писем (без ограничения по дате)...");
        }

        var uids = inbox.Search(searchQuery);
        Logger.Info("Найдено непрочитанных писем: {0}", uids.Count);

        if (uids.Count == 0)
            return;

        // Ограничение на количество писем за запуск
        if (processing.maxEmailsPerRun > 0 && uids.Count > processing.maxEmailsPerRun)
        {
            Logger.Info("Ограничение: обрабатываем только {0} из {1} писем.",
                processing.maxEmailsPerRun, uids.Count);
            uids = new UniqueIdSet(uids.Take(processing.maxEmailsPerRun));
        }

        var summaries = inbox.Fetch(uids,
            MessageSummaryItems.Flags | MessageSummaryItems.Envelope | MessageSummaryItems.BodyStructure);

        int processedCount = 0;
        int skippedCount = 0;
        int attachmentSavedCount = 0;
        int tableParsedCount = 0;

        foreach (var summary in summaries)
        {
            try
            {
                var sender = summary.Envelope.From.Mailboxes.FirstOrDefault();
                string senderAddr = sender != null ? sender.Address : "unknown";
                string subject = summary.Envelope.Subject ?? "(no subject)";
                bool hasAttachments = summary.Attachments != null && summary.Attachments.Any();

                Logger.Info("── Письмо UID={0}: от={1}, тема='{2}', вложения={3}",
                    summary.UniqueId.Id, senderAddr, subject, hasAttachments);

                // Определяем, есть ли подходящие вложения (по summary, без скачивания)
                bool hasMatchingAttachments = HasMatchingAttachments(summary, attachCfg);

                bool somethingProcessed = false;

                // 1. Скачиваем подходящие вложения
                if (attachCfg.enabled && hasMatchingAttachments)
                {
                    var message = inbox.GetMessage(summary.UniqueId);
                    int saved = SaveMatchingAttachments(message, summary.UniqueId, attachCfg, config);
                    attachmentSavedCount += saved;
                    somethingProcessed = true;
                }

                // 2. Если подходящих вложений нет — пробуем парсить HTML-таблицы
                if (tableCfg.enabled && !hasMatchingAttachments)
                {
                    var message = inbox.GetMessage(summary.UniqueId);
                    string html = message.HtmlBody;

                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        int parsed = ParseAndSaveTables(html, summary.UniqueId, tableCfg, config);
                        tableParsedCount += parsed;
                        if (parsed > 0)
                            somethingProcessed = true;
                    }
                    else
                    {
                        Logger.Debug("UID={0}: HTML-тело отсутствует, парсинг таблиц невозможен.",
                            summary.UniqueId.Id);
                    }
                }

                if (!somethingProcessed)
                {
                    Logger.Info("UID={0}: нет подходящих вложений и таблицы не найдены — пропускаем.",
                        summary.UniqueId.Id);
                    skippedCount++;
                }
                else
                {
                    processedCount++;
                }

                // Помечаем как прочитанное
                if (processing.markAsRead)
                {
                    inbox.AddFlags(summary.UniqueId, MessageFlags.Seen, true);
                    Logger.Debug("UID={0} помечено как прочитанное.", summary.UniqueId.Id);
                }
            }
            catch (Exception emailEx)
            {
                Logger.Error("Ошибка обработки письма UID={0}: {1}", summary.UniqueId.Id, emailEx.Message);
                Logger.Debug("Стек: {0}", emailEx.StackTrace);
            }
        }

        Logger.Info("══ ИТОГ: обработано {0}, пропущено {1}, вложений сохранено {2}, таблиц распарсено {3}",
            processedCount, skippedCount, attachmentSavedCount, tableParsedCount);
    }

    // ────────────────────── Вложения ──────────────────────

    /// <summary>
    /// Проверяет по summary, есть ли вложения с нужными расширениями.
    /// Работает без скачивания самого письма — быстро.
    /// </summary>
    private static bool HasMatchingAttachments(IMessageSummary summary, Configuration.AttachmentsConfig cfg)
    {
        if (!cfg.enabled || summary.Attachments == null)
            return false;

        var extensions = new HashSet<string>(cfg.extensions, StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in summary.Attachments)
        {
            string fileName = attachment.ContentDisposition?.Parameters["filename"]
                          ?? attachment.ContentType.Name
                          ?? "";

            string ext = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext) && extensions.Contains(ext))
            {
                Logger.Debug("Подходящее вложение (summary): '{0}' (ext={1})", fileName, ext);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Сохраняет подходящие вложения на диск.
    /// </summary>
    private static int SaveMatchingAttachments(MimeMessage message, UniqueId uid,
        Configuration.AttachmentsConfig cfg, Configuration.AppConfig config)
    {
        string targetDir = GetOutputPath(config, cfg.subfolder);
        Directory.CreateDirectory(targetDir);

        int savedCount = 0;
        int fileNumber = 1;

        var extensions = new HashSet<string>(cfg.extensions, StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in message.Attachments)
        {
            var part = attachment as MimePart;
            if (part == null)
            {
                fileNumber++;
                continue;
            }

            string rawName = part.ContentDisposition != null && part.ContentDisposition.Parameters["filename"] != null
                         ? part.ContentDisposition.Parameters["filename"]
                         : (part.ContentType.Name ?? $"attachment_{fileNumber}");

            string ext = Path.GetExtension(rawName);
            if (string.IsNullOrEmpty(ext) || !extensions.Contains(ext))
            {
                Logger.Debug("Пропускаем вложение '{0}' (не подходит по расширению).", rawName);
                fileNumber++;
                continue;
            }

            string decodedName = EncodingHelper.SmartDecodeFileName(rawName);
            decodedName = EncodingHelper.SanitizeFileName(decodedName);

            string fileName = cfg.prefixWithUid
                ? $"{uid.Id}_{fileNumber}_{decodedName}"
                : decodedName;

            string filePath = Path.Combine(targetDir, fileName);

            try
            {
                using (var stream = File.Create(filePath))
                {
                    part.Content.DecodeTo(stream);
                }
                Logger.Info("Сохранено вложение: {0} ({1} байт)", fileName, new FileInfo(filePath).Length);
                savedCount++;
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка сохранения вложения '{0}': {1}", fileName, ex.Message);
            }

            fileNumber++;
        }

        return savedCount;
    }

    // ────────────────────── Парсинг таблиц ──────────────────────

    /// <summary>
    /// Парсит HTML-таблицы и сохраняет результаты в CSV.
    /// </summary>
    private static int ParseAndSaveTables(string html, UniqueId uid,
        Configuration.TableParsingConfig tableCfg, Configuration.AppConfig config)
    {
        try
        {
            var results = MailTableParser.Parse(html, tableCfg.rules, tableCfg.parseAllTablesIfNoMatch);

            if (results.Count == 0)
            {
                Logger.Debug("UID={0}: таблицы не найдены или ни одно правило не подошло.", uid.Id);
                return 0;
            }

            string outputDir = GetOutputPath(config, tableCfg.subfolder);
            var createdFiles = MailTableParser.SaveToCsv(results, outputDir, uid);

            return createdFiles.Count;
        }
        catch (Exception ex)
        {
            Logger.Error("Ошибка парсинга таблиц для UID={0}: {1}", uid.Id, ex.Message);
            return 0;
        }
    }

    // ────────────────────── Утилиты ──────────────────────

    /// <summary>
    /// Вычисляет путь для сохранения файлов с учётом настройки useDateSubfolder.
    /// </summary>
    private static string GetOutputPath(Configuration.AppConfig config, string subfolder)
    {
        string basePath = config.processing.outputFolder;

        if (config.processing.useDateSubfolder)
        {
            basePath = Path.Combine(basePath, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        }

        if (!string.IsNullOrWhiteSpace(subfolder))
        {
            basePath = Path.Combine(basePath, subfolder);
        }

        return basePath;
    }
}
