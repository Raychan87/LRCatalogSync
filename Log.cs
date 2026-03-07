using System;
using System.IO;

namespace LRCatalogSync
{
    // Logging-Funktion für die Anwendung
    // Schreibt in Log-Datei im Programm-Verzeichnis
    public static class Log
    {
        private static string logFilePath;
        private static object lockObj = new object();
        private static string currentLogLevel = "Info";

        private enum LogLevelValue
        {
            Aus = -1,
            Debug = 0,
            Info = 1,
            Warn = 2,
            Error = 3
        }

        public static void Initialize(string baseDir, string logLevel = "Info")
        {
            currentLogLevel = logLevel;

            string logsDir = Path.Combine(baseDir, "data", "logs");
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            // Log-Datei mit Datum im Namen
            string logFileName = $"LRCatalogSync_{DateTime.Now:yyyy-MM-dd}.log";
            logFilePath = Path.Combine(logsDir, logFileName);
        }

        public static void SetLogLevel(string level)
        {
            currentLogLevel = level;
        }
        // Prüft ob eine Nachricht geschrieben werden soll
        private static bool ShouldLog(string level)
        {
            if (currentLogLevel == "Aus")
                return false;

            if (!Enum.TryParse(currentLogLevel, out LogLevelValue configLevel))
                configLevel = LogLevelValue.Info;

            if (!Enum.TryParse(level, out LogLevelValue messageLevel))
                return false;

            return messageLevel >= configLevel;
        }

        public static void Write(string message, string level = "INFO")
        {
            if (!ShouldLog(level))
                return;

            try
            {
                lock (lockObj)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string logEntry = $"{timestamp} [{level}] {message}";

                    // In Datei schreiben
                    File.AppendAllText(logFilePath, logEntry + Environment.NewLine);

                    // Auch in Debug-Ausgabe (für Entwicklung)
                    System.Diagnostics.Debug.WriteLine(logEntry);
                }
            }
            catch
            {
                // Falls Logging fehlschlägt - nichts tun
            }
        }

        // Convenience-Methoden für verschiedene Stufen
        public static void Debug(string message) => Write(message, "Debug");
        public static void Info(string message) => Write(message, "Info");
        public static void Warn(string message) => Write(message, "Warn");
        public static void Error(string message) => Write(message, "Error");

        /// Formatiert eine Zeit als String
        public static string FormatDateTime(DateTime? dt)
        {
            if (dt == null) return "";
            return ((DateTime)dt).ToString("MM/dd/yyyy HH:mm:ss");
        }
    }
}
