using System;
using System.Collections.Generic;
using System.Linq;
using HtmlAgilityPack;
using MailProcessor.Configuration;
using MailProcessor.Utilities;

namespace MailProcessor.Parsing
{
    /// <summary>
    /// Парсер HTML-таблиц из тела письма.
    /// Настраивается через правила из конфига — поддерживает любые форматы таблиц.
    /// Не зависит от MailKit — принимает строковый uidLabel для имён файлов.
    /// Сохранение результатов — в TableExporter.
    /// </summary>
    public static class MailTableParser
    {
        /// <summary>
        /// Результат парсинга одной таблицы.
        /// </summary>
        public class ParseResult
        {
            public string ruleName { get; set; } = "";
            public int tableIndex { get; set; }
            public string[] headers { get; set; }
            public List<string[]> rows { get; set; } = new List<string[]>();
            public string delimiter { get; set; } = ";";
            public string outputFormat { get; set; } = "csv";
            public bool headerIncludedInRows { get; set; } = false;
        }

        /// <summary>
        /// Парсит HTML-тело письма, применяя правила из конфига.
        /// Возвращает список результатов для каждой найденной таблицы.
        /// </summary>
        public static List<ParseResult> Parse(string html, List<TableParseRule> rules, bool parseAllIfNoMatch)
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

                    var parsedResult = ExtractRows(rows, rule);
                    parsedResult.outputFormat = rule.outputFormat ?? "csv";
                    parsedResult.headerIncludedInRows = rule.includeHeader;

                    results.Add(parsedResult);
                }
            }

            if (results.Count == 0 && parseAllIfNoMatch)
            {
                Logger.Info("Ни одно правило не подошло, парсим все таблицы (parseAllTablesIfNoMatch=true)");

                for (int t = 0; t < tables.Count; t++)
                {
                    var rows = tables[t].SelectNodes(".//tr");
                    if (rows == null || rows.Count == 0)
                        continue;

                    string[] headerRow = null;
                    var dataRows = new List<string[]>();
                    bool first = true;

                    foreach (var row in rows)
                    {
                        var cells = row.SelectNodes("./th|./td");
                        if (cells == null) continue;
                        var values = cells.Select(c => CleanText(c.InnerText)).ToArray();

                        if (first) { headerRow = values; first = false; }
                        else { dataRows.Add(values); }
                    }

                    results.Add(new ParseResult
                    {
                        ruleName = "Table_" + t,
                        tableIndex = t,
                        headers = headerRow,
                        rows = dataRows,
                        delimiter = ";",
                        outputFormat = "csv"
                    });
                }
            }

            Logger.Info("MailTableParser: распарсено таблиц: {0}", results.Count);
            return results;
        }

        // ────────────────────── Внутренние методы ──────────────────────

        private static bool IsTableMatch(HtmlNodeCollection rows, TableParseRule rule)
        {
            if (string.IsNullOrWhiteSpace(rule.tableIdentifier))
                return false;

            string scope = (rule.searchScope ?? "firstRow").ToLowerInvariant();
            string mode = (rule.matchMode ?? "contains").ToLowerInvariant();

            IEnumerable<HtmlNode> rowsToCheck;
            if (scope == "firstrow") rowsToCheck = rows.Take(1);
            else rowsToCheck = rows;

            foreach (var row in rowsToCheck)
            {
                string rowText = Normalize(row.InnerText);
                if (TextMatcher.Matches(rowText, rule.tableIdentifier, mode))
                    return true;
            }

            return false;
        }

        private static ParseResult ExtractRows(HtmlNodeCollection rows, TableParseRule rule)
        {
            var result = new ParseResult
            {
                ruleName = rule.name,
                delimiter = rule.delimiter ?? ";"
            };

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

            var headerCells = rows[startIdx].SelectNodes("./th|./td");
            if (headerCells != null)
            {
                result.headers = headerCells.Select(c => CleanText(c.InnerText)).ToArray();
            }

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
                result.rows.Add(values);
            }

            Logger.Debug("Правило '{0}': извлечено {1} строк (диапазон [{2}..{3}), includeHeader={4})",
                rule.name, result.rows.Count, dataStartIdx, endIdx, rule.includeHeader);

            return result;
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return string.Join(" ",
                System.Net.WebUtility.HtmlDecode(text)
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Trim();
        }

        private static string CleanText(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;

            s = HtmlEntity.DeEntitize(s);
            s = System.Net.WebUtility.HtmlDecode(s);
            s = s.Replace("\u00A0", " ");
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            s = string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return s.Trim();
        }
    }
}