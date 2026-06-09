using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MailKit;
using MailProcessor.Configuration;
using MailProcessor.Models;
using MailProcessor.Utilities;
using MimeKit;

namespace MailProcessor.Services
{
    /// <summary>
    /// Сервис сохранения вложений из письма на диск.
    /// </summary>
    public static class AttachmentSaver
    {
        /// <summary>
        /// Определяет, какие группы вложений подходят для данного письма
        /// (по summary, без загрузки полного письма).
        /// </summary>
        public static List<AttachmentGroupConfig> GetMatchingGroups(
            IMessageSummary summary, List<AttachmentGroupConfig> groups)
        {
            var matched = new List<AttachmentGroupConfig>();

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
                    string fileName = AttachmentHelper.GetFileNameFromSummary(attachment);
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
        /// Возвращает AttachmentMeta с relativePath (относительно basePath).
        /// </summary>
        public static List<AttachmentMeta> SaveForGroup(MimeMessage message, UniqueId uid,
            AttachmentGroupConfig group, string basePath, bool dryRun)
        {
            string targetDir = Path.Combine(basePath, group.subfolder);

            if (!dryRun)
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

                string rawName = AttachmentHelper.GetFileName(part);
                if (string.IsNullOrEmpty(rawName))
                    rawName = "attachment_" + fileNumber;

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

                // Относительный путь от basePath
                string relativePath = group.subfolder + "/" + fileName;

                if (dryRun)
                {
                    Logger.Info("[DRY RUN] Группа '{0}': было бы сохранено вложение: {1}",
                        group.name, Path.Combine(targetDir, fileName));

                    savedAttachments.Add(new AttachmentMeta
                    {
                        fileName = decodedName,
                        extension = ext,
                        contentType = part.ContentType.MimeType,
                        sizeBytes = 0,
                        relativePath = relativePath,
                        group = group.name
                    });

                    fileNumber++;
                    continue;
                }

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
                        relativePath = relativePath,
                        group = group.name
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

        /// <summary>
        /// Проверяет, есть ли у письма вложения (по summary).
        /// </summary>
        public static bool HasAttachments(IMessageSummary summary)
        {
            return summary.Attachments != null && summary.Attachments.Any();
        }
    }
}