using Serilog.Core;
using Serilog.Events;
using System;
using System.Text;

namespace SkillzBot.Logging
{
    /// <summary>
    /// Adds two properties for compact, grep-friendly log lines:
    /// Component (short class name from SourceContext) and ExceptionShort
    /// (one-line " => Type: message <- InnerType: message" chain).
    /// Full stack traces stay available in the errors log.
    /// </summary>
    public sealed class CompactLogEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            string component = "App";
            if (logEvent.Properties.TryGetValue("SourceContext", out var source) &&
                source is ScalarValue scalar && scalar.Value is string name && name.Length > 0)
            {
                int dot = name.LastIndexOf('.');
                component = dot >= 0 ? name.Substring(dot + 1) : name;
            }
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("Component", component));

            if (logEvent.Exception != null)
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("ExceptionShort", Summarize(logEvent.Exception)));
        }

        public static string Summarize(Exception exception)
        {
            var sb = new StringBuilder(" => ");
            int depth = 0;
            for (var ex = exception; ex != null && depth < 4; ex = ex.InnerException, depth++)
            {
                if (depth > 0) sb.Append(" <- ");
                sb.Append(ex.GetType().Name).Append(": ").Append(FirstLine(ex.Message));
            }
            return sb.ToString();
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int nl = text.IndexOfAny(new[] { '\r', '\n' });
            var line = nl >= 0 ? text.Substring(0, nl) : text;
            return line.Length > 300 ? line.Substring(0, 300) + "…" : line;
        }
    }
}
