using System;
using System.IO;
using System.Text;

namespace Client.Helpers
{
    public static class Logger
    {
        private static readonly object _sync = new();
        private static readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EyeChat",
            "app.log");

        public static void Log(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:O}] {message}{Environment.NewLine}";
                lock (_sync)
                {
                    var directory = Path.GetDirectoryName(_logPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(_logPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Ignore logging failures – the goal is to avoid crashing the app when we cannot log.
            }
        }

        public static void LogException(string context, Exception exception)
        {
            var builder = new StringBuilder();
            builder.AppendLine(context);
            builder.AppendLine(exception.ToString());
            Log(builder.ToString());
        }
    }
}
