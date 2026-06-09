using System.Collections.Generic;

namespace MailProcessor.Configuration
{
    // ────────────────────── Корневая модель ──────────────────────

    /// <summary>
    /// Корневая модель конфигурации приложения.
    /// Сериализуется из JSON-файла с поддержкой комментариев (// ...).
    /// </summary>
    public class AppConfig
    {
        public ImapConfig imap { get; set; } = new ImapConfig();
        public ProcessingConfig processing { get; set; } = new ProcessingConfig();
        public List<AttachmentGroupConfig> attachments { get; set; } = new List<AttachmentGroupConfig>();
        public TableParsingConfig tableParsing { get; set; } = new TableParsingConfig();
        public EmailFilterConfig emailFilter { get; set; } = new EmailFilterConfig();
        public JsonExportConfig jsonExport { get; set; } = new JsonExportConfig();
        public LoggingConfig logging { get; set; } = new LoggingConfig();
    }

    // ────────────────────── IMAP ──────────────────────

    /// <summary>
    /// Настройки подключения к IMAP-серверу.
    /// </summary>
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

        /// <summary>
        /// Имя папки IMAP для обработки.
        /// По умолчанию "INBOX". Можно указать подпапку, например "INBOX/Reports".
        /// Символ разделителя зависит от сервера (обычно / или .).
        /// </summary>
        public string folder { get; set; } = "INBOX";
    }

    // ────────────────────── Обработка ──────────────────────

    /// <summary>
    /// Настройки обработки писем: путь вывода, даты, лимиты.
    /// </summary>
    public class ProcessingConfig
    {
        /// <summary>Помечать письма как прочитанные после обработки.</summary>
        public bool markAsRead { get; set; } = true;

        /// <summary>Сколько последних дней просматривать (0 = все непрочитанные).</summary>
        public int daysToSearch { get; set; } = 7;

        /// <summary>Максимум писем за запуск (0 = без лимита).</summary>
        public int maxEmailsPerRun { get; set; } = 0;

        /// <summary>Базовая папка для сохранения результатов (вложений, CSV).</summary>
        public string outputFolder { get; set; } = "";

        /// <summary>
        /// Создавать подпапку с датой запуска внутри outputFolder.
        /// </summary>
        public bool useDateSubfolder { get; set; } = false;

        /// <summary>
        /// Формат даты/времени для имени подпапки и файлов (стандарт .NET format string).
        /// </summary>
        public string dateFormat { get; set; } = "yyyy-MM-dd_HH-mm-ss";

        /// <summary>
        /// Имя подпапки для JSON-файлов внутри basePath запуска.
        /// Пустая строка — JSON-файлы сохраняются прямо в basePath.
        /// </summary>
        public string jsonFolder { get; set; } = "json";

        /// <summary>
        /// Сохранять JSON-метаданные для ВСЕХ писем.
        /// Если false — JSON сохраняется только для писем, подошедших под условия
        /// saveMailToJson в группах вложений или tableParsing.saveMailToJson.
        /// </summary>
        public bool saveAllMailsToJson { get; set; } = false;

        /// <summary>
        /// Dry-run режим: показать что БЫЛО БЫ сделано, но не сохранять файлы
        /// и не помечать письма как прочитанные. Полезно для проверки конфига.
        /// </summary>
        public bool dryRun { get; set; } = false;
    }

    // ────────────────────── Группы вложений ──────────────────────

    /// <summary>
    /// Конфигурация одной группы вложений.
    /// </summary>
    public class AttachmentGroupConfig
    {
        /// <summary>Человекочитаемое имя группы (используется в логах и JSON-метаданных).</summary>
        public string name { get; set; } = "Attachments";

        /// <summary>Включена ли эта группа (если false — пропускается).</summary>
        public bool enabled { get; set; } = true;

        /// <summary>Расширения файлов для скачивания (например, ".pdf", ".xls").</summary>
        public List<string> extensions { get; set; } = new List<string>();

        /// <summary>Подпапка для вложений этой группы внутри basePath запуска.</summary>
        public string subfolder { get; set; } = "attachments";

        /// <summary>Добавлять ли UID письма к имени файла.</summary>
        public bool prefixWithUid { get; set; } = true;

        /// <summary>
        /// Сохранять ли JSON-метаданные для писем, у которых есть
        /// вложения из этой группы.
        /// </summary>
        public bool saveMailToJson { get; set; } = true;
    }

    // ────────────────────── Парсинг таблиц ──────────────────────

    /// <summary>
    /// Настройки парсинга HTML-таблиц из тела письма.
    /// </summary>
    public class TableParsingConfig
    {
        /// <summary>Включён ли парсинг таблиц.</summary>
        public bool enabled { get; set; } = true;

        /// <summary>Подпапка для CSV внутри basePath запуска.</summary>
        public string subfolder { get; set; } = "parsed";

        /// <summary>Если ни одно правило не подошло — парсить ли все таблицы.</summary>
        public bool parseAllTablesIfNoMatch { get; set; } = false;

        /// <summary>
        /// Сохранять ли JSON-метаданные для писем, у которых была распарсена таблица.
        /// </summary>
        public bool saveMailToJson { get; set; } = false;

        /// <summary>Список правил парсинга таблиц.</summary>
        public List<TableParseRule> rules { get; set; } = new List<TableParseRule>();
    }

    /// <summary>
    /// Правило поиска и извлечения данных из HTML-таблицы.
    /// </summary>
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

        /// <summary>
        /// Формат вывода таблицы:
        ///   "csv" — только CSV-файл (по умолчанию)
        ///   "json" — только JSON-файл (именованные поля, если есть заголовок)
        ///   "both" — CSV и JSON одновременно
        /// </summary>
        public string outputFormat { get; set; } = "csv";
    }

    // ────────────────────── Фильтрация писем ──────────────────────

    /// <summary>
    /// Фильтрация писем по правилам (отправитель + тема).
    /// Правила объединяются по ИЛИ (OR): достаточно совпадения одного правила.
    /// Внутри правила поля объединяются по И (AND): должны совпасть все непустые фильтры.
    /// Фильтр является ДОПОЛНИТЕЛЬНОЙ причиной для сохранения JSON (не блокирует другие причины).
    /// </summary>
    public class EmailFilterConfig
    {
        /// <summary>Включена ли фильтрация писем.</summary>
        public bool enabled { get; set; } = false;

        /// <summary>Список правил фильтрации. Правила объединяются по OR.</summary>
        public List<EmailFilterRule> rules { get; set; } = new List<EmailFilterRule>();
    }

    /// <summary>
    /// Одно правило фильтрации писем.
    /// Все непустые фильтры внутри правила должны совпасть (AND).
    /// Если у фильтра patterns пуст — этот фильтр не учитывается.
    /// </summary>
    public class EmailFilterRule
    {
        /// <summary>Человекочитаемое имя правила (для логов).</summary>
        public string name { get; set; } = "";

        /// <summary>Фильтр по отправителю (email + имя). Пустой patterns = любой отправитель.</summary>
        public FieldFilter sender { get; set; } = new FieldFilter();

        /// <summary>Фильтр по теме письма. Пустой patterns = любая тема.</summary>
        public FieldFilter subject { get; set; } = new FieldFilter();
    }

    /// <summary>
    /// Фильтр по одному полю письма.
    /// </summary>
    public class FieldFilter
    {
        /// <summary>Список шаблонов. Пустой = без фильтра (всегда проходит).</summary>
        public List<string> patterns { get; set; } = new List<string>();

        /// <summary>Режим поиска: "contains" | "startsWith" | "exact" | "regex".</summary>
        public string matchMode { get; set; } = "contains";
    }

    // ────────────────────── JSON-выгрузка ──────────────────────

    /// <summary>
    /// Настройки содержания JSON-метаданных.
    /// </summary>
    public class JsonExportConfig
    {
        /// <summary>Включать ли текстовое тело письма в JSON.</summary>
        public bool includeBodyText { get; set; } = true;

        /// <summary>Включать ли HTML-тело письма в JSON.</summary>
        public bool includeBodyHtml { get; set; } = false;

        /// <summary>Создавать ли сводный _summary.json со всеми письмами.</summary>
        public bool createSummary { get; set; } = true;
    }

    // ────────────────────── Логирование ──────────────────────

    /// <summary>
    /// Настройки логирования.
    /// </summary>
    public class LoggingConfig
    {
        /// <summary>Уровень логирования: "debug" | "info" | "error".</summary>
        public string level { get; set; } = "info";

        /// <summary>Подпапка для логов (относительно exe).</summary>
        public string folder { get; set; } = "logs";
    }
}