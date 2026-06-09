namespace MailProcessor.Models
{
    /// <summary>
    /// Метаданные сохранённого вложения.
    /// Пути хранятся относительно basePath запуска.
    /// </summary>
    public class AttachmentMeta
    {
        /// <summary>Оригинальное имя файла вложения.</summary>
        public string fileName { get; set; } = "";

        /// <summary>Расширение файла.</summary>
        public string extension { get; set; } = "";

        /// <summary>MIME-тип вложения.</summary>
        public string contentType { get; set; } = "";

        /// <summary>Размер файла в байтах.</summary>
        public long sizeBytes { get; set; }

        /// <summary>
        /// Путь к файлу относительно basePath запуска.
        /// Пример: "attachments_pdf/123_1_doc.pdf"
        /// </summary>
        public string relativePath { get; set; } = "";

        /// <summary>
        /// Имя группы вложений (из конфига attachments[].name),
        /// по которой это вложение было сохранено.
        /// </summary>
        public string group { get; set; } = "";
    }
}
