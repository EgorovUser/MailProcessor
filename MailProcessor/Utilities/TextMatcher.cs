using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MailProcessor.Utilities
{
    /// <summary>
    /// Утилита для проверки совпадения текста с шаблоном.
    /// Поддерживает единые режимы: contains, startsWith, exact, regex.
    /// Используется в MailTableParser (правила таблиц) и EmailFilterService (фильтры писем).
    /// </summary>
    public static class TextMatcher
    {
        /// <summary>
        /// Проверяет, соответствует ли текст шаблону в указанном режиме.
        /// </summary>
        /// <param name="text">Проверяемый текст (предварительно нормализованный)</param>
        /// <param name="pattern">Шаблон для поиска</param>
        /// <param name="mode">Режим: "contains" | "startsWith" | "exact" | "regex"</param>
        /// <returns>true, если текст соответствует шаблону</returns>
        public static bool Matches(string text, string pattern, string mode)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
                return false;

            switch (mode)
            {
                case "contains":
                    return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

                case "startswith":
                    return text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);

                case "exact":
                    return string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);

                case "regex":
                    try
                    {
                        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
                    }
                    catch (ArgumentException ex)
                    {
                        Logger.Error("Некорректное regex-выражение '{0}': {1}", pattern, ex.Message);
                        return false;
                    }

                default:
                    Logger.Error("Неизвестный matchMode '{0}', используем contains.", mode);
                    return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>
        /// Проверяет, соответствует ли текст хотя бы одному шаблону из списка.
        /// Пустой список шаблонов = нет фильтра (всё проходит).
        /// </summary>
        /// <param name="text">Проверяемый текст</param>
        /// <param name="patterns">Список шаблонов</param>
        /// <param name="mode">Режим поиска (null/пустая строка = "contains")</param>
        /// <returns>true, если patterns пустой или текст соответствует хотя бы одному шаблону</returns>
        public static bool MatchesAny(string text, List<string> patterns, string mode)
        {
            if (patterns == null || patterns.Count == 0)
                return true;

            string normalizedMode = string.IsNullOrEmpty(mode) ? "contains" : mode.ToLowerInvariant();

            foreach (var pattern in patterns)
            {
                if (Matches(text, pattern, normalizedMode))
                    return true;
            }

            return false;
        }
    }
}