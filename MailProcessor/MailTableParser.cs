using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MailKit;

/// <summary>
/// Универсальный парсер HTML-таблиц из тела письма.
/// Настраивается через правила из конфига — поддерживает любые форматы таблиц.
/// </summary>
public static class MailTableParser
{
    /// <summary>
    /// Результат парсинга одной таблицы.
    /// </summary>
    public class ParseResult
    {
        /// <summary>Имя правила, по которому найдена таблица.</summary>
        public string RuleName { get; set; } = "";

        /// <summary>Индекс таблицы в HTML (0-based).</summary>
        public int TableIndex { get; set; }

        /// <summary>Строки таблицы (каждая строка — массив ячеек).</summary>
        public List<string[]> Rows { get; set; } = new List<string[]>();

        /// <summary>Разделитель для CSV (из правила).</summary>
        public string Delimiter { get; set; } = ";";
    }

    /// <summary>
    /// Парсит HTML-тело письма, применяя правила из конфига.
    /// Возвращает список результатов для каждой найденной таблицы.
    /// </summary>
    /// <param name="html">HTML-тело письма</param>
    /// <param name="rules">Правила парсинга из конфига</param>
    /// <param name="parseAllIfNoMatch">Если true и ни одно правило не подошло — парсить все таблицы</param>
    /// <returns>Список результатов парсинга</returns>
    public static List<ParseResult> Parse(string html, List<Configuration.TableParseRule> rules, bool parseAllIfNoMatch)
    {
        var results = new List<ParseResult>();

        if (string.IsNullOrWhiteSpace(html))
        {
            Logger.Debug("MailTableParser: HTML пуст — нечего парсить.");
            return results;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null || tables.Count == 0)
        {
            Logger.Debug("MailTableParser: таблицы в HTML не найдены.");
            return results;
        }

        Logger.Debug("MailTableParser: найдено таблиц в HTML: {0}", tables.Count);

        // Применяем каждое правило к каждой таблице
        var matchedTableIndices = new HashSet<int>();

        for (int t = 0; t < tables.Count; t++)
        {
            var table = tables[t];
            var rows = table.SelectNodes(".//tr");
            if (rows == null || rows.Count == 0)
                continue;

            foreach (var rule in rules)
            {
                if (!IsTableMatch(rows, rule))
                    continue;

                Logger.Info("Таблица #{0} подошла под правило '{1}' (identifier: '{2}')",
                    t, rule.name, rule.tableIdentifier);

                var parsedRows = ExtractRows(rows, rule);

                results.Add(new ParseResult
                {
                    RuleName = rule.name,
                    TableIndex = t,
                    Rows = parsedRows,
                    Delimiter = rule.delimiter ?? ";"
                });

                matchedTableIndices.Add(t);
            }
        }

        // Если ни одно правило не подошло — парсим все таблицы (если включено в конфиге)
        if (results.Count == 0 && parseAllIfNoMatch)
        {
            Logger.Info("Ни одно правило не подошло, парсим все таблицы (parseAllTablesIfNoMatch=true)");

            for (int t = 0; t < tables.Count; t++)
            {
                var rows = tables[t].SelectNodes(".//tr");
                if (rows == null || rows.Count == 0)
                    continue;

                // Для "сырого" парсинга — берём все строки без пропусков
                var parsedRows = new List<string[]>();
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes("./th|./td");
                    if (cells == null) continue;
                    parsedRows.Add(cells.Select(c => CleanText(c.InnerText)).ToArray());
                }

                results.Add(new ParseResult
                {
                    RuleName = $"Table_{t}",
                    TableIndex = t,
                    Rows = parsedRows,
                    Delimiter = ";"
                });
            }
        }

        Logger.Info("MailTableParser: распарсено таблиц: {0}", results.Count);
        return results;
    }

    /// <summary>
    /// Сохраняет результаты парсинга в CSV-файлы.
    /// </summary>
    /// <param name="results">Результаты парсинга</param>
    /// <param name="outputFolder">Папка для сохранения</param>
    /// <param name="uid">UID письма (для имени файла)</param>
    /// <returns>Список путей к созданным CSV-файлам</returns>
    public static List<string> SaveToCsv(List<ParseResult> results, string outputFolder, UniqueId uid)
    {
        var createdFiles = new List<string>();
        Directory.CreateDirectory(outputFolder);

        foreach (var result in results)
        {
            if (result.Rows.Count == 0)
            {
                Logger.Debug("Пустой результат для правила '{0}' — CSV не создаётся.", result.RuleName);
                continue;
            }

            string fileName = $"{uid.Id}_{result.RuleName}_{result.TableIndex}.csv";
            string filePath = Path.Combine(outputFolder, fileName);

            try
            {
                using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
                {
                    foreach (var row in result.Rows)
                    {
                        writer.WriteLine(string.Join(result.Delimiter, row.Select(EscapeCsvField)));
                    }
                }

                Logger.Info("CSV сохранён: {0} ({1} строк)", filePath, result.Rows.Count);
                createdFiles.Add(filePath);
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка сохранения CSV '{0}': {1}", filePath, ex.Message);
            }
        }

        return createdFiles;
    }

    // ────────────────────── Внутренние методы ──────────────────────

    /// <summary>
    /// Проверяет, соответствует ли таблица правилу.
    /// </summary>
    private static bool IsTableMatch(HtmlNodeCollection rows, Configuration.TableParseRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.tableIdentifier))
            return false;

        string scope = (rule.searchScope ?? "firstRow").ToLowerInvariant();
        string mode = (rule.matchMode ?? "contains").ToLowerInvariant();

        // Определяем, какие строки проверять
        IEnumerable<HtmlNode> rowsToCheck;
        if (scope == "firstrow")
        {
            rowsToCheck = rows.Take(1);
        }
        else // anyRow
        {
            rowsToCheck = rows;
        }

        foreach (var row in rowsToCheck)
        {
            string rowText = Normalize(row.InnerText);
            if (MatchesPattern(rowText, rule.tableIdentifier, mode))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Проверяет, соответствует ли текст шаблону в указанном режиме.
    /// </summary>
    private static bool MatchesPattern(string text, string pattern, string mode)
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
                Logger.Error("Неизвестный matchMode: '{0}', используем contains.", mode);
                return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Извлекает строки из таблицы по правилу (с учётом skipFirst/skipLast/includeHeader).
    /// </summary>
    private static List<string[]> ExtractRows(HtmlNodeCollection rows, Configuration.TableParseRule rule)
    {
        var result = new List<string[]>();

        // Вычисляем диапазон строк
        int skipFirst = Math.Max(0, rule.skipFirstRows);
        int skipLast = Math.Max(0, rule.skipLastRows);
        int totalRows = rows.Count;
        int startIdx = skipFirst;
        int endIdx = totalRows - skipLast;

        if (startIdx >= endIdx)
        {
            Logger.Debug("Правило '{0}': после пропуска строк не осталось данных (skipFirst={1}, skipLast={2}, total={3})",
                rule.name, skipFirst, skipLast, totalRows);
            return result;
        }

        // Если includeHeader = false, пропускаем первую строку после skipFirst
        int dataStartIdx = rule.includeHeader ? startIdx : startIdx + 1;

        if (dataStartIdx >= endIdx)
        {
            Logger.Debug("Правило '{0}': после пропуска заголовка не осталось данных.", rule.name);
            return result;
        }

        for (int i = dataStartIdx; i < endIdx; i++)
        {
            var cells = rows[i].SelectNodes("./th|./td");
            if (cells == null) continue;

            var values = cells.Select(c => CleanText(c.InnerText)).ToArray();
            result.Add(values);
        }

        Logger.Debug("Правило '{0}': извлечено {1} строк (диапазон [{2}..{3}), includeHeader={4})",
            rule.name, result.Count, dataStartIdx, endIdx, rule.includeHeader);

        return result;
    }

    /// <summary>
    /// Нормализация текста: удаление лишних пробелов, декодирование HTML-сущностей.
    /// </summary>
    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(" ",
            System.Net.WebUtility.HtmlDecode(text)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }

    /// <summary>
    /// Очистка текста ячейки: декодирование HTML, удаление спецсимволов.
    /// </summary>
    private static string CleanText(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        s = HtmlEntity.DeEntitize(s);
        s = System.Net.WebUtility.HtmlDecode(s);

        s = s.Replace("\u00A0", " ");  // Неразрывный пробел
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        s = string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        return s.Trim();
    }

    /// <summary>
    /// Экранирование значения для CSV: если содержит разделитель, кавычки или перенос — обернуть в кавычки.
    /// </summary>
    private static string EscapeCsvField(string value)
    {
        if (value == null)
            return string.Empty;

        if (value.Contains(";") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }
}
