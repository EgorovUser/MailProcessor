using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MailKit;
using MimeKit;
using Newtonsoft.Json;

/// <summary>
/// Метаданные обработанного письма для выгрузки в JSON.
/// </summary>
public class EmailMetadata
{
    /// <summary>UID письма в IMAP.</summary>
    public string uid { get; set; } = "";

    /// <summary>Дата/время обработки (ISO 8601).</summary>
    public string processingDate { get; set; } = "";

    /// <summary>IMAP-сервер.</summary>
    public string imapServer { get; set; } = "";

    // ─────────────── Отправитель ───────────────

    /// <summary>Email отправителя.</summary>
    public string fromAddress { get; set; } = "";

    /// <summary>Имя отправителя (если указано).</summary>
    public string fromName { get; set; } = "";

    // ─────────────── Получатели ───────────────

    /// <summary>Список получателей (email).</summary>
    public List<string> to { get; set; } = new List<string>();

    /// <summary>Список получателей копий (email).</summary>
    public List<string> cc { get; set; } = new List<string>();

    /// <summary>Список скрытых копий (email).</summary>
    public List<string> bcc { get; set; } = new List<string>();

    // ─────────────── Тема и даты ───────────────

    /// <summary>Тема письма.</summary>
    public string subject { get; set; } = "";

    /// <summary>Дата отправки письма.</summary>
    public string dateSent { get; set; } = "";

    /// <summary>Дата получения письма.</summary>
    public string dateReceived { get; set; } = "";

    // ─────────────── Тело ───────────────

    /// <summary>Текстовое тело письма (plain text), null если не включено в конфиге.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string bodyPlainText { get; set; }

    /// <summary>HTML-тело письма, null если не включено в конфиге.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string bodyHtml;

    // ─────────────── Вложения ───────────────

    /// <summary>Есть ли у письма вложения (любые).</summary>
    public bool hasAttachments { get; set; }

    /// <summary>Общее количество вложений (включая неподходящие по расширению).</summary>
    public int totalAttachmentCount { get; set; }

    /// <summary>Информация о сохранённых вложениях.</summary>
    public List<AttachmentMeta> savedAttachments { get; set; } = new List<AttachmentMeta>();

    /// <summary>Имена всех вложений письма (даже не сохранённых — для информации).</summary>
    public List<string> allAttachmentNames { get; set; } = new List<string>();

    // ─────────────── Парсинг таблиц ───────────────

    /// <summary>Была ли распарсена таблица из HTML-тела.</summary>
    public bool hasParsedTable { get; set; }

    /// <summary>Информация о распарсенных таблицах.</summary>
    public List<TableParseMeta> parsedTables { get; set; } = new List<TableParseMeta>();

    // ─────────────── Результат обработки ───────────────

    /// <summary>Результат обработки: "attachments" | "table" | "both" | "none" | "error".</summary>
    public string processingResult { get; set; } = "none";

    /// <summary>Сообщение об ошибке (если processingResult = "error").</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string errorMessage { get; set; }

    // ─────────────── Методы ───────────────

    /// <summary>
    /// Создаёт метаданные из MimeMessage и IMessageSummary.
    /// </summary>
    public static EmailMetadata FromMessage(MimeMessage message, IMessageSummary summary, string imapServer)
    {
        var meta = new EmailMetadata();

        meta.uid = summary.UniqueId.Id.ToString();
        meta.imapServer = imapServer;
        meta.processingDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        // Отправитель
        var sender = message.From.Mailboxes.FirstOrDefault();
        if (sender != null)
        {
            meta.fromAddress = sender.Address ?? "";
            meta.fromName = sender.Name ?? "";
        }

        // Получатели
        meta.to = message.To.Mailboxes.Select(m => m.Address ?? "").ToList();
        meta.cc = message.Cc.Mailboxes.Select(m => m.Address ?? "").ToList();
        meta.bcc = message.Bcc.Mailboxes.Select(m => m.Address ?? "").ToList();

        // Тема и даты
        meta.subject = message.Subject ?? "";
        meta.dateSent = message.Date.ToString("yyyy-MM-ddTHH:mm:sszzz");
        meta.dateReceived = summary.Envelope.Date != null
            ? summary.Envelope.Date.Value.ToString("yyyy-MM-ddTHH:mm:sszzz")
            : "";

        // Вложения (общая информация)
        meta.hasAttachments = message.Attachments.Any();
        meta.totalAttachmentCount = message.Attachments.Count();
        meta.allAttachmentNames = message.Attachments
            .Select(a =>
            {
                var part = a as MimePart;
                if (part == null) return "(message-part)";
                return part.ContentDisposition != null && part.ContentDisposition.Parameters["filename"] != null
                    ? part.ContentDisposition.Parameters["filename"]
                    : (part.ContentType.Name ?? "(unnamed)");
            })
            .ToList();

        return meta;
    }

    /// <summary>
    /// Проверяет, проходит ли письмо под фильтры по отправителю и теме.
    /// Это содержательные фильтры — решение о том, нужно ли вообще
    /// сохранять JSON, принимается на уровне конфига (saveAllMailsToJson,
    /// saveMailToJson в группах вложений и tableParsing).
    /// </summary>
    public bool PassesContentFilters(Configuration.JsonExportConfig cfg)
    {
        // Проверка по отправителю
        if (cfg.senderFilter != null && cfg.senderFilter.Count > 0)
        {
            bool senderMatch = false;
            string fromLower = (this.fromAddress + " " + this.fromName).ToLowerInvariant();
            foreach (var keyword in cfg.senderFilter)
            {
                if (fromLower.Contains(keyword.ToLowerInvariant()))
                {
                    senderMatch = true;
                    break;
                }
            }
            if (!senderMatch) return false;
        }

        // Проверка по теме письма
        if (cfg.subjectFilter != null && cfg.subjectFilter.Count > 0)
        {
            bool subjectMatch = false;
            string subjectLower = this.subject.ToLowerInvariant();
            foreach (var keyword in cfg.subjectFilter)
            {
                if (subjectLower.Contains(keyword.ToLowerInvariant()))
                {
                    subjectMatch = true;
                    break;
                }
            }
            if (!subjectMatch) return false;
        }

        return true;
    }
}

/// <summary>
/// Метаданные сохранённого вложения.
/// </summary>
public class AttachmentMeta
{
    /// <summary>Оригинальное имя файла вложения.</summary>
    public string fileName { get; set; } = "";

    /// <summary>Расширение файла.</summary>
    public string extension { get; set; } = "";

    /// <summary>MIME-тип вложения.</summary>
    public string contentType { get; set; } = "";

    /// <summary>Размер файла в байтах (0 если не удалось определить).</summary>
    public long sizeBytes { get; set; }

    /// <summary>Полный путь к сохранённому файлу.</summary>
    public string savedPath { get; set; } = "";

    /// <summary>
    /// Имя группы вложений (из конфига attachments[].name),
    /// по которой это вложение было сохранено.
    /// </summary>
    public string attachmentGroup { get; set; } = "";
}

/// <summary>
/// Метаданные распарсенной таблицы.
/// </summary>
public class TableParseMeta
{
    /// <summary>Имя правила, по которому найдена таблица.</summary>
    public string ruleName { get; set; } = "";

    /// <summary>Индекс таблицы в HTML (0-based).</summary>
    public int tableIndex { get; set; }

    /// <summary>Количество строк данных в CSV.</summary>
    public int rowCount { get; set; }

    /// <summary>Полный путь к сохранённому CSV-файлу.</summary>
    public string savedPath { get; set; } = "";
}

/// <summary>
/// Утилиты для сохранения EmailMetadata в JSON.
/// </summary>
public static class EmailMetadataExporter
{
    /// <summary>
    /// Сохраняет метаданные одного письма в JSON-файл.
    /// </summary>
    public static string SaveSingle(EmailMetadata meta, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);

        string fileName = meta.uid + "_" + meta.processingDate.Replace(":", "-").Replace("T", "_") + ".json";
        string filePath = Path.Combine(outputFolder, fileName);

        string json = JsonConvert.SerializeObject(meta, Formatting.Indented, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            StringEscapeHandling = StringEscapeHandling.Default
        });

        File.WriteAllText(filePath, json, new UTF8Encoding(false));
        Logger.Debug("JSON метаданных сохранён: {0}", filePath);

        return filePath;
    }

    /// <summary>
    /// Сохраняет сводный JSON со всеми обработанными письмами.
    /// </summary>
    public static string SaveSummary(List<EmailMetadata> allMetadata, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);

        string fileName = "_summary_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".json";
        string filePath = Path.Combine(outputFolder, fileName);

        var summary = new SummaryReport
        {
            exportDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            totalEmails = allMetadata.Count,
            withAttachments = allMetadata.Count(m => m.savedAttachments.Count > 0),
            withParsedTables = allMetadata.Count(m => m.hasParsedTable),
            processed = allMetadata.Count(m => m.processingResult != "none"),
            skipped = allMetadata.Count(m => m.processingResult == "none"),
            errors = allMetadata.Count(m => m.processingResult == "error"),
            emails = allMetadata
        };

        string json = JsonConvert.SerializeObject(summary, Formatting.Indented, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            StringEscapeHandling = StringEscapeHandling.Default
        });

        File.WriteAllText(filePath, json, new UTF8Encoding(false));
        Logger.Info("Сводный JSON сохранён: {0}", filePath);

        return filePath;
    }

    /// <summary>
    /// Модель сводного отчёта.
    /// </summary>
    public class SummaryReport
    {
        public string exportDate { get; set; } = "";
        public int totalEmails { get; set; }
        public int withAttachments { get; set; }
        public int withParsedTables { get; set; }
        public int processed { get; set; }
        public int skipped { get; set; }
        public int errors { get; set; }
        public List<EmailMetadata> emails { get; set; } = new List<EmailMetadata>();
    }
}
