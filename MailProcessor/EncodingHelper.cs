using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Утилиты для декодирования имён файлов с кириллицей.
/// </summary>
public static class EncodingHelper
{
    /// <summary>
    /// Регистрация кодовых страниц (windows-1251, koi8-r и т.д.).
    /// В .NET Framework 4.8 кодовые страницы доступны нативно —
    /// регистрация провайдера не требуется, метод оставлен для совместимости.
    /// </summary>
    public static void RegisterEncodings()
    {
        // В .NET Framework 4.8 Encoding.GetEncoding(1251) работает из коробки.
        // CodePagesEncodingProvider нужен только в .NET Core / .NET 5+.
        // Ничего делать не нужно.
    }

    /// <summary>
    /// Умное декодирование имени файла.
    /// Если имя уже содержит кириллицу — возвращает как есть.
    /// Иначе пытается декодировать из windows-1251 / koi8-r / UTF-8.
    /// </summary>
    public static string SmartDecodeFileName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName))
            return rawName;

        if (HasCyrillic(rawName))
        {
            Logger.Debug("Имя уже содержит кириллицу, без декодирования: {0}", rawName);
            return rawName;
        }

        byte[] bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(rawName);

        int[] codePages = { 1251, 20866 };
        string bestDecoded = rawName;
        int maxCyrillicCount = 0;

        foreach (var codePage in codePages)
        {
            try
            {
                Encoding enc = Encoding.GetEncoding(codePage);
                string decoded = enc.GetString(bytes);
                int cyrillicCount = CountCyrillic(decoded);

                Logger.Debug("Попытка декодирования в codepage {0}: '{1}' (кириллица: {2})",
                    codePage, decoded, cyrillicCount);

                if (cyrillicCount > maxCyrillicCount)
                {
                    maxCyrillicCount = cyrillicCount;
                    bestDecoded = decoded;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("Ошибка декодирования codepage {0}: {1}", codePage, ex.Message);
            }
        }

        if (maxCyrillicCount == 0)
        {
            try
            {
                string utfDecoded = Encoding.UTF8.GetString(bytes);
                int utfCount = CountCyrillic(utfDecoded);
                if (utfCount > 0)
                {
                    Logger.Debug("Fallback UTF-8: '{0}' (кириллица: {1})", utfDecoded, utfCount);
                    return utfDecoded;
                }
            }
            catch { }
        }

        if (maxCyrillicCount > 0)
        {
            Logger.Debug("Выбрано лучшее декодирование: '{0}'", bestDecoded);
            return bestDecoded;
        }

        Logger.Debug("Декодирование не удалось, оставляем сырое: '{0}'", rawName);
        return rawName;
    }

    /// <summary>
    /// Удаление недопустимых символов из имени файла.
    /// </summary>
    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return fileName;

        string invalidChars = new string(Path.GetInvalidFileNameChars())
                            + new string(Path.GetInvalidPathChars());
        string escapedChars = Regex.Escape(invalidChars);
        string pattern = $"[{escapedChars}]";
        return Regex.Replace(fileName, pattern, "_");
    }

    public static bool HasCyrillic(string text)
    {
        return CountCyrillic(text) > 0;
    }

    public static int CountCyrillic(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c >= '\u0400' && c <= '\u04FF')
                count++;
        }
        return count;
    }
}
