using System;
using System.IO;

namespace LightroomSync
{
    /// <summary>
    /// Logging-Funktion für die Anwendung
    /// Schreibt in Log-Datei im Programm-Verzeichnis
    /// </summary>
    public static class Log
    {
        private static string logFilePath;
        private static object lockObj = new object();

        /// <summary>
        /// Initialisiert den Logger mit dem Basis-Verzeichnis
        /// </summary>
        public static void Initialize(string baseDir)
        {
            // Erstelle Logs-Ordner wenn nicht vorhanden
            string logsDir = Path.Combine(baseDir, "logs");
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            // Log-Datei mit Datum im Namen
            string logFileName = $"LightroomSync_{DateTime.Now:yyyy-MM-dd}.log";
            logFilePath = Path.Combine(logsDir, logFileName);
        }

        /// <summary>
        /// Schreibt eine Nachricht in die Log-Datei
        /// </summary>
        /// <param name="message">Die Nachricht</param>
        /// <param name="level">INFO, WARN, ERROR</param>
        public static void Write(string message, string level = "INFO")
        {
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

        /// <summary>
        /// Convenience-Methoden für verschiedene Stufen
        /// </summary>
        public static void Info(string message) => Write(message, "INFO");
        public static void Warn(string message) => Write(message, "WARN");
        public static void Error(string message) => Write(message, "ERROR");

        /// <summary>
        /// Formatiert eine Zeit als String
        /// </summary>
        public static string FormatDateTime(DateTime? dt)
        {
            if (dt == null) return "";
            return ((DateTime)dt).ToString("MM/dd/yyyy HH:mm:ss");
        }
    }
}