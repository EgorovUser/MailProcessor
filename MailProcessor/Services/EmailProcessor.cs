using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MailKit;
using MailKit.Net.Imap;
using MailProcessor.Configuration;
using MailProcessor.Connectivity;
using MailProcessor.Models;
using MailProcessor.Parsing;
using MailProcessor.Utilities;
using MimeKit;

namespace MailProcessor.Services
{
    /// <summary>
    /// Основной оркестратор обработки писем.
    /// </summary>
    public static class EmailProcessor
    {
        /// <summary>
        /// Основной метод: ищет непрочитанные письма, обрабатывает их.
        /// </summary>
        public static void ProcessEmails(ImapClient client, AppConfig config)
        {
            var processing = config.processing;
            var attachGroups = config.attachments ?? new List<AttachmentGroupConfig>();
            var jsonCfg = config.jsonExport;
            bool dryRun = processing.dryRun;

            DateTime runStart = DateTime.Now;
            string dateStamp = runStart.ToString(processing.dateFormat);

            if (dryRun)
                Logger.Info("═══ DRY RUN РЕЖИМ — файлы НЕ сохраняются, письма НЕ помечаются ═══");

            // ── Вычисляем basePath запуска ──
            string basePath = processing.outputFolder;
            if (processing.useDateSubfolder)
                basePath = Path.Combine(processing.outputFolder, dateStamp);

            Logger.Info("Папка вывода: {0}", basePath);

            if (!dryRun)
            {
                try { Directory.CreateDirectory(basePath); }
                catch (Exception ex)
                {
                    Logger.Error("Не удалось создать папку вывода '{0}': {1}", basePath, ex.Message);
                    return;
                }
            }

            // ── Открываем папку IMAP ──
            IMailFolder folder;
            try
            {
                folder = ImapConnector.OpenFolder(client, config.imap.folder);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось открыть папку IMAP '{0}': {1}", config.imap.folder, ex.Message);
                return;
            }

            folder.Open(FolderAccess.ReadWrite);

            // ── Поиск писем ──
            var uids = ImapConnector.SearchUnread(folder, processing.daysToSearch, processing.maxEmailsPerRun);
            int emailsFound = uids.Count;
            if (emailsFound == 0)
                return;

            var summaries = folder.Fetch(uids,
                MessageSummaryItems.Flags | MessageSummaryItems.Envelope | MessageSummaryItems.BodyStructure);

            // ── Статистика ──
            int processedCount = 0;
            int skippedCount = 0;
            int filteredCount = 0;
            int attachmentSavedCount = 0;
            int tableParsedCount = 0;
            int jsonExportedCount = 0;
            int errorCount = 0;

            var allMetadata = new List<EmailMetadata>();
            var byGroup = new Dictionary<string, GroupStatsInfo>();
            var byResult = new Dictionary<string, int>();

            foreach (var g in attachGroups.Where(g => g.enabled))
                byGroup[g.name] = new GroupStatsInfo();

            // ── Обработка каждого письма ──
            foreach (var summary in summaries)
            {
                try
                {
                    var r = ProcessSingleEmail(folder, summary, config, attachGroups, basePath, dateStamp, dryRun);

                    if (r.Filtered) filteredCount++;
                    else if (r.Skipped) skippedCount++;
                    else processedCount++;

                    attachmentSavedCount += r.AttachmentsSaved;
                    tableParsedCount += r.TablesParsed;
                    if (r.JsonExported) jsonExportedCount++;

                    UpdateGroupStats(byGroup, r);

                    if (r.Meta != null)
                    {
                        allMetadata.Add(r.Meta);
                        string status = r.Meta.result.status;
                        if (!byResult.ContainsKey(status)) byResult[status] = 0;
                        byResult[status]++;
                    }
                }
                catch (Exception emailEx)
                {
                    errorCount++;
                    Logger.Error("Ошибка обработки письма UID={0}: {1}", summary.UniqueId.Id, emailEx.Message);
                    Logger.Debug("Стек: {0}", emailEx.StackTrace);
                }
            }

            // ── Сводный JSON ──
            if (jsonCfg.createSummary && (allMetadata.Count > 0 || dryRun))
            {
                var summaryReport = MetadataExporter.BuildSummaryReport(
                    config, dateStamp, runStart,
                    emailsFound, processedCount, skippedCount, filteredCount,
                    errorCount, attachmentSavedCount, tableParsedCount, jsonExportedCount,
                    byGroup, byResult, allMetadata);

                string jsonDir = MetadataExporter.ResolveJsonPath(basePath, processing.jsonFolder);
                MetadataExporter.SaveSummary(summaryReport, jsonDir, processing.dateFormat, dryRun);
            }

            Logger.Info("══ ИТОГ: найдено {0}, обработано {1}, пропущено {2}, отфильтровано {3}, ошибок {4}, вложений сохранено {5}, таблиц распарсено {6}, JSON выгружено {7}",
                emailsFound, processedCount, skippedCount, filteredCount, errorCount,
                attachmentSavedCount, tableParsedCount, jsonExportedCount);
        }

        // ────────────────────── Обработка одного письма ──────────────────────

        private class EmailProcessResult
        {
            public EmailMetadata Meta;
            public bool JsonExported;
            public bool Skipped;
            public bool Filtered;
            public int AttachmentsSaved;
            public int TablesParsed;
            public List<AttachmentGroupConfig> MatchedGroups = new List<AttachmentGroupConfig>();
        }

        private static EmailProcessResult ProcessSingleEmail(
            IMailFolder folder,
            IMessageSummary summary,
            AppConfig config,
            List<AttachmentGroupConfig> attachGroups,
            string basePath,
            string runId,
            bool dryRun)
        {
            var result = new EmailProcessResult();
            var processing = config.processing;
            var tableCfg = config.tableParsing;
            var jsonCfg = config.jsonExport;
            var filterCfg = config.emailFilter;

            var senderMailbox = summary.Envelope.From.Mailboxes.FirstOrDefault();
            string senderAddr = senderMailbox != null ? senderMailbox.Address : "unknown";
            string senderName = senderMailbox != null ? (senderMailbox.Name ?? "") : "";
            string subject = summary.Envelope.Subject ?? "(no subject)";

            Logger.Info("── Письмо UID={0}: от={1}, тема='{2}'",
                summary.UniqueId.Id, senderAddr, subject);

            // ── Подходящие группы вложений ──
            var matchedGroups = AttachmentSaver.GetMatchingGroups(summary, attachGroups);
            bool hasMatchedGroups = matchedGroups.Count > 0;
            result.MatchedGroups = matchedGroups;

            // ── Проверяем фильтр (работает с данными конверта, не требует полного письма) ──
            bool passesFilter = EmailFilterService.PassesAnyRule(
                senderAddr, senderName, subject, filterCfg);

            // ── Определяем, нужно ли сохранять JSON (все причины — OR) ──
            // 1) saveAllMailsToJson — сохранять все письма
            // 2) Группа вложений с saveMailToJson=true
            // 3) Фильтр совпал — дополнительная причина для сохранения
            bool shouldSaveJson = processing.saveAllMailsToJson
                || matchedGroups.Any(g => g.saveMailToJson)
                || passesFilter;

            bool needMessage = hasMatchedGroups
                || (tableCfg != null && tableCfg.enabled && !hasMatchedGroups)
                || shouldSaveJson;

            if (!needMessage)
            {
                Logger.Info("UID={0}: нет подходящих вложений, таблиц и фильтров — пропускаем.",
                    summary.UniqueId.Id);
                result.Skipped = true;
                MarkAsRead(folder, summary, processing, dryRun);
                return result;
            }

            // ── Загружаем полное письмо и создаём метаданные ──
            MimeMessage message = folder.GetMessage(summary.UniqueId);

            var meta = EmailMetadata.FromMessage(message, summary, config.imap.server, config.imap.folder, runId);
            if (jsonCfg.includeBodyText) meta.body.plainText = message.TextBody;
            if (jsonCfg.includeBodyHtml) meta.body.html = message.HtmlBody;

            // ── Скачиваем вложения ──
            bool somethingProcessed = false;

            if (hasMatchedGroups)
            {
                foreach (var group in matchedGroups)
                {
                    var savedAttachments = AttachmentSaver.SaveForGroup(
                        message, summary.UniqueId, group, basePath, dryRun);
                    result.AttachmentsSaved += savedAttachments.Count;
                    meta.attachments.saved.AddRange(savedAttachments);
                    if (savedAttachments.Count > 0) somethingProcessed = true;
                }
            }

            // ── Парсинг HTML-таблиц ──
            if (tableCfg != null && tableCfg.enabled && !hasMatchedGroups)
            {
                string html = message.HtmlBody;
                if (!string.IsNullOrWhiteSpace(html))
                {
                    var tableResults = TableExporter.ParseAndSave(
                        html, summary.UniqueId.Id.ToString(), tableCfg, basePath, dryRun);
                    result.TablesParsed += tableResults.Count;
                    if (tableResults.Count > 0) somethingProcessed = true;
                    meta.tables.parsed = tableResults;

                    // 4) Таблица распарсена + saveMailToJson — ещё одна причина
                    if (tableResults.Count > 0 && tableCfg.saveMailToJson)
                        shouldSaveJson = true;
                }
                else
                {
                    Logger.Debug("UID={0}: HTML-тело отсутствует, парсинг таблиц невозможен.",
                        summary.UniqueId.Id);
                }
            }

            // ── Результат обработки ──
            if (!somethingProcessed)
            {
                Logger.Info("UID={0}: нет подходящих вложений и таблицы не найдены — пропускаем.",
                    summary.UniqueId.Id);
                result.Skipped = true;
                meta.result.status = "none";
            }
            else
            {
                bool hadAttachments = meta.attachments.saved.Count > 0;
                bool hadTables = meta.tables.parsed.Count > 0;
                if (hadAttachments && hadTables) meta.result.status = "both";
                else if (hadAttachments) meta.result.status = "attachments";
                else if (hadTables) meta.result.status = "table";
                else meta.result.status = "none";
            }

            // ── Помечаем как прочитанное ──
            MarkAsRead(folder, summary, processing, dryRun);

            // ── Экспорт JSON (все причины уже учтены в shouldSaveJson — OR) ──
            if (shouldSaveJson)
            {
                string jsonDir = MetadataExporter.ResolveJsonPath(basePath, processing.jsonFolder);
                MetadataExporter.SaveSingle(meta, jsonDir, processing.dateFormat, dryRun);
                result.JsonExported = true;
                Logger.Info("JSON метаданных: UID={0}", meta.uid);
            }

            result.Meta = meta;
            return result;
        }

        // ────────────────────── Вспомогательные методы ──────────────────────

        private static void MarkAsRead(IMailFolder folder, IMessageSummary summary,
            ProcessingConfig processing, bool dryRun)
        {
            if (!processing.markAsRead) return;

            if (dryRun)
            {
                Logger.Debug("[DRY RUN] UID={0} НЕ помечено как прочитанное.", summary.UniqueId.Id);
                return;
            }

            folder.AddFlags(summary.UniqueId, MessageFlags.Seen, true);
            Logger.Debug("UID={0} помечено как прочитанное.", summary.UniqueId.Id);
        }

        private static void UpdateGroupStats(Dictionary<string, GroupStatsInfo> byGroup, EmailProcessResult r)
        {
            foreach (var group in r.MatchedGroups)
            {
                if (byGroup.ContainsKey(group.name))
                    byGroup[group.name].emailsMatched++;
            }

            if (r.Meta != null)
            {
                foreach (var att in r.Meta.attachments.saved)
                {
                    if (byGroup.ContainsKey(att.group))
                        byGroup[att.group].filesSaved++;
                }
            }
        }
    }
}