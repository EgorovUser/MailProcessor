using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

/// <summary>
/// Конфигурация приложения — модели для десериализации JSON.
/// Поддерживает JSON-файлы с однострочными комментариями (// ...).
/// </summary>
public static class Configuration
{
    // ────────────────────── Корневая модель ──────────────────────

    public class AppConfig
    {
        public ImapConfig imap { get; set; } = new ImapConfig();
        public ProcessingConfig processing { get; set; } = new ProcessingConfig();
        public List<AttachmentGroupConfig> attachments { get; set; } = new List<AttachmentGroupConfig>();
        public TableParsingConfig tableParsing { get; set; } = new TableParsingConfig();
        public JsonExportConfig jsonExport { get; set; } = new JsonExportConfig();
        public LoggingConfig logging { get; set; } = new LoggingConfig();
    }

    // ────────────────────── IMAP ──────────────────────

    public class ImapConfig
    {
        /// <summary>Адрес IMAP-сервера.</summary>
        public string server { get; set; } = "";

        /// <summary>Имя цели в Windows Credential Manager.</summary>
        public string credentialTarget { get; set; } = "";

        /// <summary>Порт (0 = авто-подбор).</summary>
        public int port { get; set; } = 0;

        /// <summary>SSL-режим: "SslOnConnect" | "StartTls" | "None" | "Auto".</summary>
        public string sslOption { get; set; } = "Auto";
    }

    // ────────────────────── Обработка ──────────────────────

    public class ProcessingConfig
    {
        /// <summary>Помечать письма как прочитанные после обработки.</summary>
        public bool markAsRead { get; set; } = true;

        /// <summary>Сколько последних дней просматривать (0 = все непрочитанные).</summary>
        public int daysToSearch { get; set; } = 7;

        /// <summary>Максимум писем за запуск (0 = без лимита).</summary>
        public int maxEmailsPerRun { get; set; } = 0;

        /// <summary>Папка для сохранения результатов (вложений, CSV).</summary>
        public string outputFolder { get; set; } = "";

        /// <summary>Создавать подпапку с датой запуска.</summary>
        public bool useDateSubfolder { get; set; } = false;

        /// <summary>
        /// Путь до папки для сохранения JSON-файлов.
        /// Может быть абсолютным (C:\Output\JSON) или относительным (json) —
        /// в последнем случае считается подпапкой внутри outputFolder.
        /// Если пустое — используется outputFolder\json.
        /// </summary>
        public string jsonFolder { get; set; } = "";

        /// <summary>
        /// Сохранять JSON-метаданные для ВСЕХ писем (независимо от вложений и таблиц).
        /// Если false — JSON сохраняется только для писем, подошедших под условия
        /// saveMailToJson в группах вложений или tableParsing.saveMailToJson.
        /// </summary>
        public bool saveAllMailsToJson { get; set; } = false;
    }

    // ────────────────────── Группы вложений ──────────────────────

    /// <summary>
    /// Конфигурация одной группы вложений.
    /// attachments — это массив таких объектов, что позволяет разделять
    /// вложения по расширениям в разные папки и независимо управлять
    /// выгрузкой JSON для каждой группы.
    /// </summary>
    public class AttachmentGroupConfig
    {
        /// <summary>
        /// Человекочитаемое имя группы (используется в логах и в JSON-метаданных).
        /// Например: "PDF Documents", "Excel Files".
        /// </summary>
        public string name { get; set; } = "Attachments";

        /// <summary>Включена ли эта группа (если false — пропускается).</summary>
        public bool enabled { get; set; } = true;

        /// <summary>Расширения файлов для скачивания (например, ".pdf", ".xls").</summary>
        public List<string> extensions { get; set; } = new List<string>();

        /// <summary>
        /// Подпапка для вложений этой группы внутри outputFolder.
        /// Например: "attachments_pdf", "attachments_excel".
        /// </summary>
        public string subfolder { get; set; } = "attachments";

        /// <summary>Добавлять ли UID письма к имени файла.</summary>
        public bool prefixWithUid { get; set; } = true;

        /// <summary>
        /// Сохранять ли JSON-метаданные для писем, у которых есть
        /// вложения из этой группы.
        /// Например: для PDF — true, для XLS/XLSX — false.
        /// </summary>
        public bool saveMailToJson { get; set; } = true;
    }

    // ────────────────────── Парсинг таблиц ──────────────────────

    public class TableParsingConfig
    {
        /// <summary>Включён ли парсинг таблиц.</summary>
        public bool enabled { get; set; } = true;

        /// <summary>Подпапка для CSV внутри outputFolder.</summary>
        public string subfolder { get; set; } = "parsed";

        /// <summary>Если ни одно правило не подошло — парсить ли все таблицы.</summary>
        public bool parseAllTablesIfNoMatch { get; set; } = false;

        /// <summary>
        /// Сохранять ли JSON-метаданные для писем, у которых была распарсена таблица.
        /// Срабатывает только если таблица действительно найдена и сохранена в CSV.
        /// </summary>
        public bool saveMailToJson { get; set; } = false;

        /// <summary>Список правил парсинга таблиц.</summary>
        public List<TableParseRule> rules { get; set; } = new List<TableParseRule>();
    }

    public class TableParseRule
    {
        /// <summary>Человекочитаемое имя правила (используется в именах файлов и логах).</summary>
        public string name { get; set; } = "Unnamed";

        /// <summary>Текст для поиска нужной таблицы.</summary>
        public string tableIdentifier { get; set; } = "";

        /// <summary>Режим поиска: "contains" | "startsWith" | "exact" | "regex".</summary>
        public string matchMode { get; set; } = "contains";

        /// <summary>Где искать: "firstRow" | "anyRow".</summary>
        public string searchScope { get; set; } = "firstRow";

        /// <summary>Сколько строк пропустить сверху.</summary>
        public int skipFirstRows { get; set; } = 0;

        /// <summary>Сколько строк пропустить снизу.</summary>
        public int skipLastRows { get; set; } = 0;

        /// <summary>Включать ли строку-заголовок (после пропуска) в CSV.</summary>
        public bool includeHeader { get; set; } = false;

        /// <summary>Разделитель для CSV-вывода.</summary>
        public string delimiter { get; set; } = ";";
    }

    // ────────────────────── JSON-выгрузка ──────────────────────

    /// <summary>
    /// Настройки содержания JSON-метаданных.
    /// Само решение «сохранять ли JSON» управляется через:
    ///   - processing.saveAllMailsToJson
    ///   - attachments[].saveMailToJson
    ///   - tableParsing.saveMailToJson
    /// </summary>
    public class JsonExportConfig
    {
        /// <summary>
        /// Фильтр по отправителю.
        /// Пустой список = без фильтра (подходят все).
        /// Непустой — отправитель (адрес или имя) должен содержать
        /// хотя бы одну из строк (contains, без учёта регистра).
        /// </summary>
        public List<string> senderFilter { get; set; } = new List<string>();

        /// <summary>
        /// Фильтр по теме письма.
        /// Пустой список = без фильтра.
        /// Непустой — тема должна содержать хотя бы одну из строк.
        /// </summary>
        public List<string> subjectFilter { get; set; } = new List<string>();

        /// <summary>Включать ли текстовое тело письма в JSON (может быть большим).</summary>
        public bool includeBodyText { get; set; } = true;

        /// <summary>Включать ли HTML-тело письма в JSON (может быть очень большим).</summary>
        public bool includeBodyHtml { get; set; } = false;

        /// <summary>Создавать ли сводный _summary.json со всеми письмами.</summary>
        public bool createSummary { get; set; } = true;
    }

    // ────────────────────── Логирование ──────────────────────

    public class LoggingConfig
    {
        /// <summary>Уровень логирования: "debug" | "info" | "error".</summary>
        public string level { get; set; } = "info";

        /// <summary>Подпапка для логов (относительно exe).</summary>
        public string folder { get; set; } = "logs";
    }

    // ────────────────────── Загрузка и валидация ──────────────────────

    /// <summary>
    /// Загружает конфигурацию из JSON-файла.
    /// Поддерживает однострочные комментарии (// ...).
    /// </summary>
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Конфиг не найден: " + path);

        string rawJson = File.ReadAllText(path, System.Text.Encoding.UTF8);

        // Убираем однострочные комментарии // (не внутри строк)
        string cleanJson = StripComments(rawJson);

        var config = JsonConvert.DeserializeObject<AppConfig>(cleanJson);
        if (config == null)
            throw new InvalidOperationException("Не удалось распарсить конфиг.");

        Validate(config, path);
        return config;
    }

    /// <summary>
    /// Убирает из JSON однострочные комментарии вида // ...
    /// Не трогает // внутри строковых значений.
    /// </summary>
    private static string StripComments(string json)
    {
        bool inString = false;
        bool escaped = false;
        var result = new System.Text.StringBuilder(json.Length);
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

            // Нашли // — не внутри строки → пропускаем до конца строки
            if (!inString && c == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                // Пропускаем всё до \n или до конца
                while (i < json.Length && json[i] != '\n')
                    i++;
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Валидация обязательных полей конфигурации.
    /// </summary>
    private static void Validate(AppConfig config, string path)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.imap.server))
            errors.Add("imap.server — обязательное поле.");

        if (string.IsNullOrWhiteSpace(config.imap.credentialTarget))
            errors.Add("imap.credentialTarget — обязательное поле (имя цели в Credential Manager).");

        if (string.IsNullOrWhiteSpace(config.processing.outputFolder))
            errors.Add("processing.outputFolder — обязательное поле (папка для результатов).");

        // Проверка групп вложений
        if (config.attachments != null)
        {
            for (int i = 0; i < config.attachments.Count; i++)
            {
                var group = config.attachments[i];
                string groupLabel = !string.IsNullOrWhiteSpace(group.name)
                    ? group.name
                    : "#" + i;

                if (group.enabled && (group.extensions == null || group.extensions.Count == 0))
                    errors.Add("attachments[" + groupLabel + "].extensions — список расширений пуст, но группа включена.");

                if (string.IsNullOrWhiteSpace(group.subfolder))
                    errors.Add("attachments[" + groupLabel + "].subfolder — не задана подпапка для вложений.");
            }
        }

        // Проверка правил парсинга таблиц
        if (config.tableParsing.enabled)
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
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Ошибки в конфиге " + path + ":\n  — " + string.Join("\n  — ", errors));
    }

    /// <summary>
    /// Парсинг строки уровня лога в enum.
    /// </summary>
    public static LogLevel ParseLogLevel(string level)
    {
        if (string.IsNullOrEmpty(level)) return LogLevel.Info;
        string lower = level.Trim().ToLowerInvariant();
        if (lower == "debug") return LogLevel.Debug;
        if (lower == "error") return LogLevel.Error;
        return LogLevel.Info;
    }

    /// <summary>
    /// Вычисляет полный путь к папке JSON.
    /// Если jsonFolder — абсолютный путь, использует его.
    /// Если относительный — добавляет к outputFolder.
    /// Если пустой — использует outputFolder\json.
    /// </summary>
    public static string ResolveJsonFolder(Configuration.AppConfig config)
    {
        string jsonFolder = config.processing.jsonFolder;

        if (string.IsNullOrWhiteSpace(jsonFolder))
            jsonFolder = "json";

        // Проверяем, абсолютный ли путь
        if (Path.IsPathRooted(jsonFolder))
            return jsonFolder;

        // Относительный путь — внутри outputFolder (с учётом useDateSubfolder)
        string basePath = config.processing.outputFolder;
        if (config.processing.useDateSubfolder)
            basePath = Path.Combine(basePath, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));

        return Path.Combine(basePath, jsonFolder);
    }
}
