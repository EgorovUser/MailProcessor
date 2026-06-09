using System.Collections.Generic;

namespace MailProcessor.Models
{
    /// <summary>
    /// Сводный отчёт за запуск.
    /// </summary>
    public class SummaryReport
    {
        public RunInfo run { get; set; } = new RunInfo();
        public StatsInfo stats { get; set; } = new StatsInfo();
        public Dictionary<string, GroupStatsInfo> byGroup { get; set; } = new Dictionary<string, GroupStatsInfo>();
        public Dictionary<string, int> byResult { get; set; } = new Dictionary<string, int>();
        public List<EmailMetadata> emails { get; set; } = new List<EmailMetadata>();
    }

    /// <summary>
    /// Контекст запуска программы.
    /// </summary>
    public class RunInfo
    {
        public string id { get; set; } = "";
        public string startedAt { get; set; } = "";
        public string finishedAt { get; set; } = "";
        public int durationSeconds { get; set; }
        public string server { get; set; } = "";
        public string folder { get; set; } = "";
        public bool dryRun { get; set; }
    }

    /// <summary>
    /// Статистика обработки за запуск.
    /// </summary>
    public class StatsInfo
    {
        public int emailsFound { get; set; }
        public int emailsProcessed { get; set; }
        public int emailsSkipped { get; set; }
        public int emailsFiltered { get; set; }
        public int errors { get; set; }
        public int attachmentsSaved { get; set; }
        public int tablesParsed { get; set; }
        public int jsonExported { get; set; }
    }

    /// <summary>
    /// Статистика по группе вложений.
    /// </summary>
    public class GroupStatsInfo
    {
        public int emailsMatched { get; set; }
        public int filesSaved { get; set; }
    }
}