using System.Collections.Generic;

namespace MailProcessor.Models
{
    /// <summary>
    /// Метаданные распарсенной HTML-таблицы.
    /// Пути хранятся относительно basePath запуска.
    /// </summary>
    public class TableParseMeta
    {
        /// <summary>Имя правила, по которому найдена таблица.</summary>
        public string ruleName { get; set; } = "";

        /// <summary>Индекс таблицы в HTML (0-based).</summary>
        public int tableIndex { get; set; }

        /// <summary>Количество строк данных.</summary>
        public int rowCount { get; set; }

        /// <summary>
        /// Список сохранённых файлов (относительно basePath).
        /// Может содержать CSV, JSON или оба — в зависимости от outputFormat правила.
        /// Примеры: ["parsed/123_TransferDetails_0.csv"], ["parsed/123_TransferDetails_0.json"]
        /// </summary>
        public List<string> savedFiles { get; set; } = new List<string>();
    }
}