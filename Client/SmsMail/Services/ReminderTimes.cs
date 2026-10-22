using System;
using System.Collections.Generic;
using System.Linq;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services
{
    public static class ReminderTimes
    {
        public static Dictionary<TimeSpan, TimeSpan> Validate(IEnumerable<AppointmentTimeRule> rules)
        {
            var result = new Dictionary<TimeSpan, TimeSpan>();
            foreach (var rule in rules)
            {
                if (rule.From < TimeSpan.Zero || rule.From >= TimeSpan.FromDays(1) || rule.To < TimeSpan.Zero || rule.To >= TimeSpan.FromDays(1)
                    || rule.From.Ticks % TimeSpan.TicksPerMinute != 0 || rule.To.Ticks % TimeSpan.TicksPerMinute != 0)
                    throw new InvalidOperationException("Les horaires doivent être compris entre 00:00 et 23:59, à la minute près.");
                if (!result.TryAdd(rule.From, rule.To)) throw new InvalidOperationException("Un même horaire d’origine ne peut avoir deux règles.");
            }
            return result;
        }
        public static DateTime Apply(DateTime original, IReadOnlyDictionary<TimeSpan, TimeSpan> rules)
        {
            var minute = new TimeSpan(original.Hour, original.Minute, 0);
            return rules.TryGetValue(minute, out var target) ? original.Date + target : original;
        }
    }
}
