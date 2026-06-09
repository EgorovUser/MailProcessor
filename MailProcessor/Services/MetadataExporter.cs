using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MailProcessor.Configuration;
using MailProcessor.Models;
using MailProcessor.Utilities;
using Newtonsoft.Json;

namespace MailProcessor.Services
{
    /// <summary>
    /// Экспорт EmailMetadata и сводных отчётов в JSON-файлы.
    /// </summary>
    public static class MetadataExporter
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            StringEscapeHandling = StringEscapeHandling.Default
        };

        /// <summary>
        /// Сохраняет метаданные одного письма в JSON-файл.
        /// </summary>
        public static string SaveSingle(EmailMetadata meta, string outputFolder, string dateFormat, bool dryRun)
        {
            string safeDate = meta.dates.processed.Replace(":", "-").Replace("T", "_");
            string fileName = meta.uid + "_" + safeDate + ".json";
            string filePath = Path.Combine(outputFolder, fileName);

            if (dryRun)
            {
                Logger.Info("[DRY RUN] JSON метаданных был бы сохранён: {0}", filePath);
                return filePath;
            }

            Directory.CreateDirectory(outputFolder);
            string json = JsonConvert.SerializeObject(meta, Formatting.Indented, JsonSettings);
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
            Logger.Debug("JSON метаданных сохранён: {0}", filePath);

            return filePath;
        }

        /// <summary>
        /// Сохраняет сводный JSON с контекстом запуска и статистикой.
        /// </summary>
        public static string SaveSummary(SummaryReport summary, string outputFolder, string dateFormat, bool dryRun)
        {
            string fileName = "_summary_" + DateTime.Now.ToString(dateFormat) + ".json";
            string filePath = Path.Combine(outputFolder, fileName);

            if (dryRun)
            {
                Logger.Info("[DRY RUN] Сводный JSON был бы сохранён: {0}", filePath);
                return filePath;
            }

            Directory.CreateDirectory(outputFolder);
            string json = JsonConvert.SerializeObject(summary, Formatting.Indented, JsonSettings);
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
            Logger.Info("Сводный JSON сохранён: {0}", filePath);

            return filePath;
        }

        /// <summary>
        /// Вычисляет путь к папке JSON.
        /// </summary>
        public static string ResolveJsonPath(string basePath, string jsonFolder)
        {
            if (string.IsNullOrWhiteSpace(jsonFolder))
                return basePath;

            return Path.Combine(basePath, jsonFolder);
        }

        /// <summary>
        /// Собирает сводный отчёт за запуск.
        /// </summary>
        public static SummaryReport BuildSummaryReport(
            AppConfig config,
            string runId,
            DateTime runStart,
            int emailsFound,
            int processedCount,
            int skippedCount,
            int filteredCount,
            int errorCount,
            int attachmentSavedCount,
            int tableParsedCount,
            int jsonExportedCount,
            Dictionary<string, GroupStatsInfo> byGroup,
            Dictionary<string, int> byResult,
            List<EmailMetadata> allMetadata)
        {
            DateTime now = DateTime.Now;

            return new SummaryReport
            {
                run = new RunInfo
                {
                    id = runId,
                    startedAt = runStart.ToString("yyyy-MM-ddTHH:mm:ss"),
                    finishedAt = now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    durationSeconds = (int)(now - runStart).TotalSeconds,
                    server = config.imap.server,
                    folder = config.imap.folder,
                    dryRun = config.processing.dryRun
                },
                stats = new StatsInfo
                {
                    emailsFound = emailsFound,
                    emailsProcessed = processedCount,
                    emailsSkipped = skippedCount,
                    emailsFiltered = filteredCount,
                    errors = errorCount,
                    attachmentsSaved = attachmentSavedCount,
                    tablesParsed = tableParsedCount,
                    jsonExported = jsonExportedCount
                },
                byGroup = byGroup,
                byResult = byResult,
                emails = allMetadata
            };
        }
    }
}