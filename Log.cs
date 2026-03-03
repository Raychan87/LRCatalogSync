using System;
using System.IO;

namespace LightroomSync
{
    // Logging-Funktion für die Anwendung
    // Schreibt in Log-Datei im Programm-Verzeichnis
    public static class Log
    {
        private static string logFilePath;
        private static object lockObj = new object();

        // Initialisiert den Logger mit dem Basis-Verzeichnis
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

        // Schreibt eine Nachricht in die Log-Datei
        // <param name="message">Die Nachricht</param>
        // <param name="level">INFO, WARN, ERROR</param>
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

        // Convenience-Methoden für verschiedene Stufen
        public static void Debug(string message) => Write(message, "DEBUG");
        public static void Info(string message) => Write(message, "INFO");
        public static void Warn(string message) => Write(message, "WARN");
        public static void Error(string message) => Write(message, "ERROR");

        /// Formatiert eine Zeit als String
        public static string FormatDateTime(DateTime? dt)
        {
            if (dt == null) return "";
            return ((DateTime)dt).ToString("MM/dd/yyyy HH:mm:ss");
        }
       
        }

}


        
