using System;
using System.Collections.Generic;
using System.Linq;
using MailKit;
using MailProcessor.Utilities;
using MimeKit;
using Newtonsoft.Json;

namespace MailProcessor.Models
{
    /// <summary>
    /// Метаданные обработанного письма для выгрузки в JSON.
    /// Структура сгруппирована по логическим блокам для удобства парсинга downstream.
    /// Все пути (relativePath) — относительно basePath запуска.
    /// </summary>
    public class EmailMetadata
    {
        // ─────────────── Идентификация ───────────────

        /// <summary>UID письма в IMAP.</summary>
        public string uid { get; set; } = "";

        /// <summary>
        /// Message-ID — стандартный заголовок письма, уникальный глобально.
        /// Используется для дедупликации и сопоставления между системами.
        /// Пример: "&lt;abc123@mail.example.com&gt;"
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string messageId { get; set; }

        /// <summary>
        /// Идентификатор запуска (дата/время в формате dateFormat).
        /// Позволяет привязать письмо к конкретному запуску программы.
        /// </summary>
        public string runId { get; set; } = "";

        /// <summary>IMAP-сервер, с которого получено письмо.</summary>
        public string imapServer { get; set; } = "";

        /// <summary>IMAP-папка, из которой получено письмо.</summary>
        public string imapFolder { get; set; } = "";

        // ─────────────── Отправитель ───────────────

        /// <summary>Информация об отправителе.</summary>
        public SenderInfo sender { get; set; } = new SenderInfo();

        // ─────────────── Получатели ───────────────

        /// <summary>Информация о получателях.</summary>
        public RecipientsInfo recipients { get; set; } = new RecipientsInfo();

        // ─────────────── Тема ───────────────

        /// <summary>Тема письма.</summary>
        public string subject { get; set; } = "";

        // ─────────────── Даты ───────────────

        /// <summary>Даты письма и обработки.</summary>
        public DatesInfo dates { get; set; } = new DatesInfo();

        // ─────────────── Тело ───────────────

        /// <summary>Тело письма (опционально, управляется конфигом).</summary>
        public BodyInfo body { get; set; } = new BodyInfo();

        // ─────────────── Вложения ───────────────

        /// <summary>Информация о вложениях.</summary>
        public AttachmentsInfo attachments { get; set; } = new AttachmentsInfo();

        // ─────────────── Парсинг таблиц ───────────────

        /// <summary>Информация о распарсенных таблицах.</summary>
        public TablesInfo tables { get; set; } = new TablesInfo();

        // ─────────────── Результат обработки ───────────────

        /// <summary>Результат обработки письма.</summary>
        public ResultInfo result { get; set; } = new ResultInfo();

        // ─────────────── Фабричный метод ───────────────

        /// <summary>
        /// Создаёт метаданные из MimeMessage и IMessageSummary.
        /// </summary>
        public static EmailMetadata FromMessage(
            MimeMessage message, IMessageSummary summary,
            string imapServer, string imapFolder, string runId)
        {
            var meta = new EmailMetadata();

            meta.uid = summary.UniqueId.Id.ToString();
            meta.messageId = message.MessageId;
            meta.runId = runId;
            meta.imapServer = imapServer;
            meta.imapFolder = imapFolder;

            // Отправитель
            var senderMailbox = message.From.Mailboxes.FirstOrDefault();
            if (senderMailbox != null)
            {
                meta.sender.address = senderMailbox.Address ?? "";
                meta.sender.name = senderMailbox.Name ?? "";
            }

            // Получатели
            meta.recipients.to = message.To.Mailboxes.Select(m => m.Address ?? "").ToList();
            meta.recipients.cc = message.Cc.Mailboxes.Select(m => m.Address ?? "").ToList();
            meta.recipients.bcc = message.Bcc.Mailboxes.Select(m => m.Address ?? "").ToList();

            // Тема
            meta.subject = message.Subject ?? "";

            // Даты
            meta.dates.sent = message.Date.ToString("yyyy-MM-ddTHH:mm:sszzz");
            meta.dates.received = summary.Envelope.Date != null
                ? summary.Envelope.Date.Value.ToString("yyyy-MM-ddTHH:mm:sszzz")
                : "";
            meta.dates.processed = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

            // Вложения (общая информация)
            meta.attachments.total = message.Attachments.Count();
            meta.attachments.allNames = message.Attachments
                .Select(a =>
                {
                    var part = a as MimePart;
                    if (part == null) return "(message-part)";
                    string name = AttachmentHelper.GetFileName(part);
                    return name.Length > 0 ? name : "(unnamed)";
                })
                .ToList();

            return meta;
        }
    }

    // ────────────────────── Вложенные модели ──────────────────────

    /// <summary>
    /// Информация об отправителе письма.
    /// </summary>
    public class SenderInfo
    {
        /// <summary>Email отправителя.</summary>
        public string address { get; set; } = "";

        /// <summary>Имя отправителя (если указано).</summary>
        public string name { get; set; } = "";
    }

    /// <summary>
    /// Информация о получателях письма.
    /// </summary>
    public class RecipientsInfo
    {
        /// <summary>Список получателей (email).</summary>
        public List<string> to { get; set; } = new List<string>();

        /// <summary>Список получателей копий (email).</summary>
        public List<string> cc { get; set; } = new List<string>();

        /// <summary>Список скрытых копий (email).</summary>
        public List<string> bcc { get; set; } = new List<string>();
    }

    /// <summary>
    /// Даты письма и обработки.
    /// </summary>
    public class DatesInfo
    {
        /// <summary>Дата отправки письма.</summary>
        public string sent { get; set; } = "";

        /// <summary>Дата получения письма сервером.</summary>
        public string received { get; set; } = "";

        /// <summary>Дата/время обработки программой.</summary>
        public string processed { get; set; } = "";
    }

    /// <summary>
    /// Тело письма (опционально, управляется конфигом jsonExport).
    /// </summary>
    public class BodyInfo
    {
        /// <summary>Текстовое тело письма (plain text). null если не включено в конфиге.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string plainText { get; set; }

        /// <summary>HTML-тело письма. null если не включено в конфиге.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string html { get; set; }
    }

    /// <summary>
    /// Информация о вложениях письма.
    /// </summary>
    public class AttachmentsInfo
    {
        /// <summary>Общее количество вложений (включая не сохранённые).</summary>
        public int total { get; set; }

        /// <summary>Имена всех вложений (включая не сохранённые).</summary>
        public List<string> allNames { get; set; } = new List<string>();

        /// <summary>Информация о сохранённых вложениях.</summary>
        public List<AttachmentMeta> saved { get; set; } = new List<AttachmentMeta>();
    }

    /// <summary>
    /// Информация о распарсенных таблицах.
    /// </summary>
    public class TablesInfo
    {
        /// <summary>Список распарсенных таблиц.</summary>
        public List<TableParseMeta> parsed { get; set; } = new List<TableParseMeta>();
    }

    /// <summary>
    /// Результат обработки письма. 
    /// </summary>
    public class ResultInfo
    {
        /// <summary>
        /// Статус обработки: "attachments" | "table" | "both" | "none" | "error".
        /// </summary>
        public string status { get; set; } = "none";

        /// <summary>Сообщение об ошибке (только если status = "error").</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string errorMessage { get; set; }
    }
}
