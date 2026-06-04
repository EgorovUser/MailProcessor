using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

/// <summary>
/// Обработка писем из IMAP: скачивание вложений по группам расширений,
/// парсинг HTML-таблиц при отсутствии подходящих вложений,
/// выгрузка метаданных в JSON.
/// </summary>
public static class EmailProcessor
{
    /// <summary>
    /// Основной метод: ищет непрочитанные письма, обрабатывает их.
    /// </summary>
    public static void ProcessEmails(ImapClient client, Configuration.AppConfig config)
    {
        var processing = config.processing;
        var attachGroups = config.attachments ?? new List<Configuration.AttachmentGroupConfig>();
        var tableCfg = config.tableParsing;
        var jsonCfg = config.jsonExport;

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
        int jsonExportedCount = 0;

        // Список метаданных для сводного JSON
        var allMetadata = new List<EmailMetadata>();

        foreach (var summary in summaries)
        {
            EmailMetadata meta = null;

            try
            {
                var sender = summary.Envelope.From.Mailboxes.FirstOrDefault();
                string senderAddr = sender != null ? sender.Address : "unknown";
                string subject = summary.Envelope.Subject ?? "(no subject)";
                bool hasAttachments = summary.Attachments != null && summary.Attachments.Any();

                Logger.Info("── Письмо UID={0}: от={1}, тема='{2}', вложения={3}",
                    summary.UniqueId.Id, senderAddr, subject, hasAttachments);

                // ── Шаг 1: Определяем, какие группы вложений подходят (по summary, без скачивания) ──
                var matchedGroups = GetMatchingAttachmentGroups(summary, attachGroups);
                bool hasMatchedGroups = matchedGroups.Count > 0;

                // ── Шаг 2: Определяем, нужно ли сохранять JSON для этого письма ──
                bool shouldSaveJson = processing.saveAllMailsToJson;

                if (!shouldSaveJson)
                {
                    foreach (var g in matchedGroups)
                    {
                        if (g.saveMailToJson)
                        {
                            shouldSaveJson = true;
                            break;
                        }
                    }
                }

                // tableParsing.saveMailToJson проверим позже — после парсинга таблиц

                // ── Шаг 3: Определяем, нужно ли загружать полное письмо ──
                bool needMessage = hasMatchedGroups
                                || (tableCfg.enabled && !hasMatchedGroups)
                                || shouldSaveJson;

                MimeMessage message = null;
                if (needMessage)
                {
                    message = inbox.GetMessage(summary.UniqueId);
                }

                // ── Шаг 4: Создаём метаданные (если JSON нужен) ──
                if (shouldSaveJson && message != null)
                {
                    meta = EmailMetadata.FromMessage(message, summary, config.imap.server);

                    if (jsonCfg.includeBodyText)
                        meta.bodyPlainText = message.TextBody;

                    if (jsonCfg.includeBodyHtml)
                        meta.bodyHtml = message.HtmlBody;
                }

                bool somethingProcessed = false;

                // ── Шаг 5: Скачиваем вложения для каждой подошедшей группы ──
                if (hasMatchedGroups && message != null)
                {
                    foreach (var group in matchedGroups)
                    {
                        var savedAttachments = SaveAttachmentsForGroup(message, summary.UniqueId, group, config);
                        attachmentSavedCount += savedAttachments.Count;

                        if (meta != null)
                        {
                            meta.savedAttachments.AddRange(savedAttachments);
                        }

                        if (savedAttachments.Count > 0)
                            somethingProcessed = true;
                    }
                }

                // ── Шаг 6: Если подходящих вложений нет — пробуем парсить HTML-таблицы ──
                if (tableCfg.enabled && !hasMatchedGroups && message != null)
                {
                    string html = message.HtmlBody;

                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        var tableResults = ParseAndSaveTablesWithMeta(html, summary.UniqueId, tableCfg, config);
                        tableParsedCount += tableResults.Count;
                        if (tableResults.Count > 0)
                            somethingProcessed = true;

                        if (meta != null)
                        {
                            meta.hasParsedTable = tableResults.Count > 0;
                            meta.parsedTables = tableResults;
                        }

                        // Проверяем tableParsing.saveMailToJson — если таблица найдена
                        if (tableResults.Count > 0 && tableCfg.saveMailToJson && !shouldSaveJson)
                        {
                            shouldSaveJson = true;

                            // Нужно создать метаданные, если ещё не созданы
                            if (meta == null)
                            {
                                meta = EmailMetadata.FromMessage(message, summary, config.imap.server);

                                if (jsonCfg.includeBodyText)
                                    meta.bodyPlainText = message.TextBody;

                                if (jsonCfg.includeBodyHtml)
                                    meta.bodyHtml = message.HtmlBody;

                                meta.hasParsedTable = true;
                                meta.parsedTables = tableResults;
                            }
                        }
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
                    if (meta != null) meta.processingResult = "none";
                }
                else
                {
                    processedCount++;
                    if (meta != null)
                    {
                        bool hadAttachments = meta.savedAttachments.Count > 0;
                        bool hadTables = meta.hasParsedTable;
                        if (hadAttachments && hadTables) meta.processingResult = "both";
                        else if (hadAttachments) meta.processingResult = "attachments";
                        else if (hadTables) meta.processingResult = "table";
                        else meta.processingResult = "none";
                    }
                }

                // Помечаем как прочитанное
                if (processing.markAsRead)
                {
                    inbox.AddFlags(summary.UniqueId, MessageFlags.Seen, true);
                    Logger.Debug("UID={0} помечено как прочитанное.", summary.UniqueId.Id);
                }

                // ── Шаг 7: Экспорт JSON для данного письма ──
                if (shouldSaveJson && meta != null && meta.PassesContentFilters(jsonCfg))
                {
                    string jsonDir = Configuration.ResolveJsonFolder(config);
                    string jsonPath = EmailMetadataExporter.SaveSingle(meta, jsonDir);
                    jsonExportedCount++;
                    Logger.Info("JSON метаданных: {0}", jsonPath);

                    // Добавляем в список для summary
                    allMetadata.Add(meta);
                }
            }
            catch (Exception emailEx)
            {
                Logger.Error("Ошибка обработки письма UID={0}: {1}", summary.UniqueId.Id, emailEx.Message);
                Logger.Debug("Стек: {0}", emailEx.StackTrace);

                if (meta != null)
                {
                    meta.processingResult = "error";
                    meta.errorMessage = emailEx.Message;
                }
            }
        }

        // Сводный JSON
        if (jsonCfg.createSummary && allMetadata.Count > 0)
        {
            string jsonDir = Configuration.ResolveJsonFolder(config);
            EmailMetadataExporter.SaveSummary(allMetadata, jsonDir);
        }

        Logger.Info("══ ИТОГ: обработано {0}, пропущено {1}, вложений сохранено {2}, таблиц распарсено {3}, JSON выгружено {4}",
            processedCount, skippedCount, attachmentSavedCount, tableParsedCount, jsonExportedCount);
    }

    // ────────────────────── Группы вложений ──────────────────────

    /// <summary>
    /// Определяет, какие группы вложений подходят для данного письма
    /// (по summary, без загрузки полного письма).
    /// </summary>
    private static List<Configuration.AttachmentGroupConfig> GetMatchingAttachmentGroups(
        IMessageSummary summary, List<Configuration.AttachmentGroupConfig> groups)
    {
        var matched = new List<Configuration.AttachmentGroupConfig>();

        if (summary.Attachments == null || !summary.Attachments.Any())
            return matched;

        foreach (var group in groups)
        {
            if (!group.enabled)
                continue;

            var extensions = new HashSet<string>(group.extensions, StringComparer.OrdinalIgnoreCase);
            bool groupHasMatch = false;

            foreach (var attachment in summary.Attachments)
            {
                string fileName = attachment.ContentDisposition != null
                    ? (attachment.ContentDisposition.Parameters["filename"] ?? "")
                    : "";
                if (string.IsNullOrEmpty(fileName) && attachment.ContentType != null)
                    fileName = attachment.ContentType.Name ?? "";

                string ext = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(ext) && extensions.Contains(ext))
                {
                    Logger.Debug("Группа '{0}': подходящее вложение '{1}' (ext={2})",
                        group.name, fileName, ext);
                    groupHasMatch = true;
                    break;
                }
            }

            if (groupHasMatch)
                matched.Add(group);
        }

        return matched;
    }

    /// <summary>
    /// Сохраняет вложения, подходящие под конкретную группу, на диск.
    /// Возвращает список метаданных сохранённых вложений.
    /// </summary>
    private static List<AttachmentMeta> SaveAttachmentsForGroup(MimeMessage message, UniqueId uid,
        Configuration.AttachmentGroupConfig group, Configuration.AppConfig config)
    {
        string targetDir = GetOutputPath(config, group.subfolder);
        Directory.CreateDirectory(targetDir);

        var savedAttachments = new List<AttachmentMeta>();
        int fileNumber = 1;

        var extensions = new HashSet<string>(group.extensions, StringComparer.OrdinalIgnoreCase);

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
                         : (part.ContentType.Name ?? "attachment_" + fileNumber);

            string ext = Path.GetExtension(rawName);
            if (string.IsNullOrEmpty(ext) || !extensions.Contains(ext))
            {
                Logger.Debug("Группа '{0}': пропускаем вложение '{1}' (не подходит по расширению).",
                    group.name, rawName);
                fileNumber++;
                continue;
            }

            string decodedName = EncodingHelper.SmartDecodeFileName(rawName);
            decodedName = EncodingHelper.SanitizeFileName(decodedName);

            string fileName = group.prefixWithUid
                ? uid.Id + "_" + fileNumber + "_" + decodedName
                : decodedName;

            string filePath = Path.Combine(targetDir, fileName);

            try
            {
                using (var stream = File.Create(filePath))
                {
                    part.Content.DecodeTo(stream);
                }

                long fileSize = new FileInfo(filePath).Length;
                Logger.Info("Группа '{0}': сохранено вложение: {1} ({2} байт)", group.name, fileName, fileSize);

                savedAttachments.Add(new AttachmentMeta
                {
                    fileName = decodedName,
                    extension = ext,
                    contentType = part.ContentType.MimeType,
                    sizeBytes = fileSize,
                    savedPath = filePath,
                    attachmentGroup = group.name
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка сохранения вложения '{0}': {1}", fileName, ex.Message);
            }

            fileNumber++;
        }

        return savedAttachments;
    }

    // ────────────────────── Парсинг таблиц ──────────────────────

    /// <summary>
    /// Парсит HTML-таблицы и сохраняет результаты в CSV.
    /// Возвращает список метаданных распарсенных таблиц.
    /// </summary>
    private static List<TableParseMeta> ParseAndSaveTablesWithMeta(string html, UniqueId uid,
        Configuration.TableParsingConfig tableCfg, Configuration.AppConfig config)
    {
        var tableMetas = new List<TableParseMeta>();

        try
        {
            var results = MailTableParser.Parse(html, tableCfg.rules, tableCfg.parseAllTablesIfNoMatch);

            if (results.Count == 0)
            {
                Logger.Debug("UID={0}: таблицы не найдены или ни одно правило не подошло.", uid.Id);
                return tableMetas;
            }

            string outputDir = GetOutputPath(config, tableCfg.subfolder);
            var createdFiles = MailTableParser.SaveToCsv(results, outputDir, uid);

            // Собираем метаданные для каждой распарсенной таблицы
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                string savedPath = i < createdFiles.Count ? createdFiles[i] : "";

                tableMetas.Add(new TableParseMeta
                {
                    ruleName = result.RuleName,
                    tableIndex = result.TableIndex,
                    rowCount = result.Rows.Count,
                    savedPath = savedPath
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Ошибка парсинга таблиц для UID={0}: {1}", uid.Id, ex.Message);
        }

        return tableMetas;
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
