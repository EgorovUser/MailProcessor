using MailProcessor.Configuration;
using MailProcessor.Utilities;

namespace MailProcessor.Services
{
    /// <summary>
    /// Сервис фильтрации писем по правилам.
    /// Правила объединяются по OR (достаточно одного совпадения).
    /// Внутри правила поля объединяются по AND (все непустые фильтры должны совпасть).
    /// Фильтр является ДОПОЛНИТЕЛЬНОЙ причиной для сохранения JSON.
    /// </summary>
    public static class EmailFilterService
    {
        /// <summary>
        /// Проверяет, проходит ли письмо хотя бы под одно правило emailFilter.
        /// Работает с данными из конверта — не требует загрузки полного письма.
        /// </summary>
        public static bool PassesAnyRule(string senderAddress, string senderName, string subject,
            EmailFilterConfig filterCfg)
        {
            if (filterCfg == null || !filterCfg.enabled)
                return false;

            if (filterCfg.rules == null || filterCfg.rules.Count == 0)
                return false;

            string senderValue = ((senderAddress ?? "") + " " + (senderName ?? "")).Trim();

            foreach (var rule in filterCfg.rules)
            {
                if (PassesRule(rule, senderValue, subject ?? ""))
                {
                    string ruleLabel = !string.IsNullOrEmpty(rule.name) ? rule.name : "(unnamed)";
                    Logger.Debug("Письмо прошло правило emailFilter '{0}'", ruleLabel);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Проверяет одно правило: все непустые фильтры должны совпасть (AND).
        /// </summary>
        private static bool PassesRule(EmailFilterRule rule, string senderValue, string subjectValue)
        {
            // Проверка отправителя (пустой patterns = любой отправитель)
            if (!PassesFieldFilter(rule.sender, senderValue))
                return false;

            // Проверка темы (пустой patterns = любая тема)
            if (!PassesFieldFilter(rule.subject, subjectValue))
                return false;

            return true;
        }

        /// <summary>
        /// Проверяет, соответствует ли значение поля хотя бы одному шаблону.
        /// Пустой patterns = без фильтра (всегда проходит).
        /// </summary>
        private static bool PassesFieldFilter(FieldFilter filter, string value)
        {
            if (filter == null || filter.patterns == null || filter.patterns.Count == 0)
                return true;

            return TextMatcher.MatchesAny(value, filter.patterns, filter.matchMode);
        }
    }
}