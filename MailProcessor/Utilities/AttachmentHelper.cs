using MailKit;
using MimeKit;

namespace MailProcessor.Utilities
{
    /// <summary>
    /// Утилиты для извлечения имён файлов из MIME-частей.
    /// Устраняет дублирование кода при работе с вложениями
    /// на уровне summary (до загрузки) и MimePart (после загрузки).
    /// </summary>
    public static class AttachmentHelper
    {
        /// <summary>
        /// Извлекает имя файла из MimePart (полностью загруженное письмо).
        /// Проверяет ContentDisposition.Filename, затем ContentType.Name.
        /// </summary>
        public static string GetFileName(MimePart part)
        {
            if (part == null)
                return "";

            if (part.ContentDisposition != null && part.ContentDisposition.Parameters["filename"] != null)
                return part.ContentDisposition.Parameters["filename"];

            if (part.ContentType != null && !string.IsNullOrEmpty(part.ContentType.Name))
                return part.ContentType.Name;

            return "";
        }

        /// <summary>
        /// Извлекает имя файла из BodyPartBasic (summary, до загрузки полного письма).
        /// </summary>
        public static string GetFileNameFromSummary(BodyPartBasic attachment)
        {
            if (attachment == null)
                return "";

            string fileName = "";

            if (attachment.ContentDisposition != null)
            {
                fileName = attachment.ContentDisposition.Parameters["filename"] ?? "";
            }

            if (string.IsNullOrEmpty(fileName) && attachment.ContentType != null)
            {
                fileName = attachment.ContentType.Name ?? "";
            }

            return fileName;
        }
    }
}