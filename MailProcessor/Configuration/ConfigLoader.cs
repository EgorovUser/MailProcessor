using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MailProcessor.Utilities;
using Newtonsoft.Json;

namespace MailProcessor.Configuration
{
    /// <summary>
    /// Загрузка, парсинг и валидация конфигурации.
    /// </summary>
    public static class ConfigLoader
    {
        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Конфиг не найден: " + path);

            string rawJson = File.ReadAllText(path, Encoding.UTF8);
            string cleanJson = StripComments(rawJson);

            var config = JsonConvert.DeserializeObject<AppConfig>(cleanJson);
            if (config == null)
                throw new InvalidOperationException("Не удалось распарсить конфиг.");

            Validate(config, path);
            return config;
        }

        public static LogLevel ParseLogLevel(string level)
        {
            if (string.IsNullOrEmpty(level)) return LogLevel.Info;
            string lower = level.Trim().ToLowerInvariant();
            if (lower == "debug") return LogLevel.Debug;
            if (lower == "error") return LogLevel.Error;
            return LogLevel.Info;
        }

        // ────────────────────── Внутренние методы ──────────────────────

        private static string StripComments(string json)
        {
            bool inString = false;
            bool escaped = false;
            var result = new StringBuilder(json.Length);
            int i = 0;

            while (i < json.Length)
            {
                char c = json[i];

                if (escaped)
                {
                    result.Append(c);
                    escaped = false;
                    i++;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    result.Append(c);
                    escaped = true;
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    result.Append(c);
                    i++;
                    continue;
                }

                if (!inString && c == '/' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    while (i < json.Length && json[i] != '\n')
                        i++;
                    continue;
                }

                result.Append(c);
                i++;
            }

            return result.ToString();
        }

        private static void Validate(AppConfig config, string path)
        {
            var errors = new List<string>();

            // IMAP
            if (string.IsNullOrWhiteSpace(config.imap.server))
                errors.Add("imap.server — обязательное поле.");

            if (string.IsNullOrWhiteSpace(config.imap.credentialTarget))
                errors.Add("imap.credentialTarget — обязательное поле.");

            if (string.IsNullOrWhiteSpace(config.imap.folder))
                errors.Add("imap.folder — обязательное поле (укажите INBOX или имя подпапки).");

            // Processing
            if (string.IsNullOrWhiteSpace(config.processing.outputFolder))
                errors.Add("processing.outputFolder — обязательное поле.");

            if (string.IsNullOrWhiteSpace(config.processing.dateFormat))
                errors.Add("processing.dateFormat — обязательное поле.");

            // Группы вложений
            if (config.attachments != null)
            {
                for (int i = 0; i < config.attachments.Count; i++)
                {
                    var group = config.attachments[i];
                    string groupLabel = !string.IsNullOrWhiteSpace(group.name) ? group.name : "#" + i;

                    if (group.enabled && (group.extensions == null || group.extensions.Count == 0))
                        errors.Add("attachments[" + groupLabel + "].extensions — список расширений пуст, но группа включена.");

                    if (string.IsNullOrWhiteSpace(group.subfolder))
                        errors.Add("attachments[" + groupLabel + "].subfolder — не задана подпапка.");
                }
            }

            // Фильтры писем
            if (config.emailFilter != null && config.emailFilter.enabled)
            {
                if (config.emailFilter.rules != null)
                {
                    for (int i = 0; i < config.emailFilter.rules.Count; i++)
                    {
                        var rule = config.emailFilter.rules[i];
                        string ruleLabel = !string.IsNullOrWhiteSpace(rule.name) ? rule.name : "#" + i;

                        ValidateFieldFilter(rule.sender,
                            "emailFilter.rules[" + ruleLabel + "].sender", errors);
                        ValidateFieldFilter(rule.subject,
                            "emailFilter.rules[" + ruleLabel + "].subject", errors);
                    }
                }
            }

            // Правила парсинга таблиц
            if (config.tableParsing != null && config.tableParsing.enabled)
            {
                foreach (var rule in config.tableParsing.rules)
                {
                    if (string.IsNullOrWhiteSpace(rule.tableIdentifier))
                        errors.Add("tableParsing.rules[" + rule.name + "]: tableIdentifier не задан.");

                    string mode = rule.matchMode != null ? rule.matchMode.ToLowerInvariant() : "";
                    if (mode != "contains" && mode != "startswith" && mode != "exact" && mode != "regex")
                        errors.Add("tableParsing.rules[" + rule.name + "]: matchMode должен быть contains/startsWith/exact/regex, получено: " + rule.matchMode);

                    string scope = rule.searchScope != null ? rule.searchScope.ToLowerInvariant() : "";
                    if (scope != "firstrow" && scope != "anyrow")
                        errors.Add("tableParsing.rules[" + rule.name + "]: searchScope должен быть firstRow/anyRow, получено: " + rule.searchScope);

                    string fmt = rule.outputFormat != null ? rule.outputFormat.ToLowerInvariant() : "csv";
                    if (fmt != "csv" && fmt != "json" && fmt != "both")
                        errors.Add("tableParsing.rules[" + rule.name + "]: outputFormat должен быть csv/json/both, получено: " + rule.outputFormat);
                }
            }

            if (errors.Count > 0)
                throw new InvalidOperationException("Ошибки в конфиге " + path + ":\n  — " + string.Join("\n  — ", errors));
        }

        private static void ValidateFieldFilter(FieldFilter filter, string path, List<string> errors)
        {
            if (filter == null)
                return;

            if (filter.patterns != null && filter.patterns.Count > 0)
            {
                string mode = filter.matchMode != null ? filter.matchMode.ToLowerInvariant() : "";
                if (mode != "contains" && mode != "startswith" && mode != "exact" && mode != "regex")
                    errors.Add(path + ".matchMode должен быть contains/startsWith/exact/regex, получено: " + filter.matchMode);
            }
        }
    }
}