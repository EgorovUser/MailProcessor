using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MailProcessor.Configuration;
using MailProcessor.Models;
using MailProcessor.Utilities;
using Newtonsoft.Json;

namespace MailProcessor.Parsing
{
    /// <summary>
    /// Экспорт распарсенных таблиц в файлы (CSV / JSON).
    /// Отвечает за сохранение результатов парсинга и построение метаданных TableParseMeta.
    /// </summary>
    public static class TableExporter
    {
        /// <summary>
        /// Парсит HTML-таблицы и сохраняет результаты в файлы.
        /// Возвращает список метаданных для каждой распарсенной таблицы.
        /// </summary>
        public static List<TableParseMeta> ParseAndSave(
            string html, string uidLabel, TableParsingConfig tableCfg, string basePath, bool dryRun)
        {
            var tableMetas = new List<TableParseMeta>();

            try
            {
                var results = MailTableParser.Parse(html, tableCfg.rules, tableCfg.parseAllTablesIfNoMatch);

                if (results.Count == 0)
                {
                    Logger.Debug("UID={0}: таблицы не найдены или ни одно правило не подошло.", uidLabel);
                    return tableMetas;
                }

                string outputDir = Path.Combine(basePath, tableCfg.subfolder);

                // Разделяем результаты по формату вывода
                var csvResults = new List<MailTableParser.ParseResult>();
                var jsonResults = new List<MailTableParser.ParseResult>();

                foreach (var r in results)
                {
                    string fmt = (r.outputFormat ?? "csv").ToLowerInvariant();
                    if (fmt == "csv" || fmt == "both") csvResults.Add(r);
                    if (fmt == "json" || fmt == "both") jsonResults.Add(r);
                }

                var csvFiles = csvResults.Count > 0
                    ? SaveToCsv(csvResults, outputDir, uidLabel, dryRun)
                    : new List<string>();

                var jsonFiles = jsonResults.Count > 0
                    ? SaveToJson(jsonResults, outputDir, uidLabel, dryRun)
                    : new List<string>();

                // Собираем метаданные для каждого результата
                foreach (var parseResult in results)
                {
                    var savedFilesList = new List<string>();
                    string baseFileName = uidLabel + "_" + parseResult.ruleName + "_" + parseResult.tableIndex;

                    foreach (var f in csvFiles)
                    {
                        if (Path.GetFileName(f).StartsWith(baseFileName))
                            savedFilesList.Add(tableCfg.subfolder + "/" + Path.GetFileName(f));
                    }
                    foreach (var f in jsonFiles)
                    {
                        if (Path.GetFileName(f).StartsWith(baseFileName))
                            savedFilesList.Add(tableCfg.subfolder + "/" + Path.GetFileName(f));
                    }

                    tableMetas.Add(new TableParseMeta
                    {
                        ruleName = parseResult.ruleName,
                        tableIndex = parseResult.tableIndex,
                        rowCount = parseResult.rows.Count,
                        savedFiles = savedFilesList
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка парсинга таблиц для UID={0}: {1}", uidLabel, ex.Message);
            }

            return tableMetas;
        }

        /// <summary>
        /// Сохраняет результаты парсинга в CSV-файлы.
        /// </summary>
        public static List<string> SaveToCsv(List<MailTableParser.ParseResult> results,
            string outputFolder, string uidLabel, bool dryRun)
        {
            var createdFiles = new List<string>();

            if (!dryRun)
                Directory.CreateDirectory(outputFolder);

            foreach (var result in results)
            {
                if (result.rows.Count == 0)
                {
                    Logger.Debug("Пустой результат для правила '{0}' — CSV не создаётся.", result.ruleName);
                    continue;
                }

                string fileName = uidLabel + "_" + result.ruleName + "_" + result.tableIndex + ".csv";
                string filePath = Path.Combine(outputFolder, fileName);

                if (dryRun)
                {
                    Logger.Info("[DRY RUN] CSV был бы сохранён: {0} ({1} строк)", filePath, result.rows.Count);
                    createdFiles.Add(filePath);
                    continue;
                }

                try
                {
                    using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
                    {
                        foreach (var row in result.rows)
                        {
                            writer.WriteLine(string.Join(result.delimiter, row.Select(EscapeCsvField)));
                        }
                    }
                    Logger.Info("CSV сохранён: {0} ({1} строк)", filePath, result.rows.Count);
                    createdFiles.Add(filePath);
                }
                catch (Exception ex)
                {
                    Logger.Error("Ошибка сохранения CSV '{0}': {1}", filePath, ex.Message);
                }
            }

            return createdFiles;
        }

        /// <summary>
        /// Сохраняет результаты парсинга в JSON-файлы.
        /// Формат: именованные поля (если есть заголовки) или массив массивов.
        /// </summary>
        public static List<string> SaveToJson(List<MailTableParser.ParseResult> results,
            string outputFolder, string uidLabel, bool dryRun)
        {
            var createdFiles = new List<string>();

            if (!dryRun)
                Directory.CreateDirectory(outputFolder);

            foreach (var result in results)
            {
                if (result.rows.Count == 0)
                    continue;

                string fileName = uidLabel + "_" + result.ruleName + "_" + result.tableIndex + ".json";
                string filePath = Path.Combine(outputFolder, fileName);

                object data;
                if (result.headers != null && result.headers.Length > 0)
                {
                    var dataRows = result.headerIncludedInRows
                        ? result.rows.Skip(1).ToList()
                        : result.rows;

                    var list = new List<Dictionary<string, string>>();
                    foreach (var row in dataRows)
                    {
                        var dict = new Dictionary<string, string>();
                        for (int i = 0; i < result.headers.Length; i++)
                        {
                            string key = result.headers[i];
                            string value = i < row.Length ? row[i] : "";
                            dict[key] = value;
                        }
                        list.Add(dict);
                    }
                    data = list;
                }
                else
                {
                    data = result.rows;
                }

                if (dryRun)
                {
                    Logger.Info("[DRY RUN] JSON таблицы был бы сохранён: {0}", filePath);
                    createdFiles.Add(filePath);
                    continue;
                }

                try
                {
                    string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    File.WriteAllText(filePath, json, new UTF8Encoding(false));
                    Logger.Info("JSON таблицы сохранён: {0}", filePath);
                    createdFiles.Add(filePath);
                }
                catch (Exception ex)
                {
                    Logger.Error("Ошибка сохранения JSON таблицы '{0}': {1}", filePath, ex.Message);
                }
            }

            return createdFiles;
        }

        // ────────────────────── Внутренние методы ──────────────────────

        private static string EscapeCsvField(string value)
        {
            if (value == null)
                return string.Empty;

            if (value.Contains(";") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }
    }
}