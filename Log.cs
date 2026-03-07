using System;
using System.IO;

namespace LRCatalogSync
{
    // Logging-Funktion für die Anwendung
    // Schreibt in Log-Datei im Programm-Verzeichnis mit automatischer Rotation
    public static class Log
    {
        private const long MAX_LOG_FILE_SIZE = 20 * 1024 * 1024; // 20 MB
        private const int MAX_BACKUP_FILES = 3; // Behalte max X alte Log-Dateien
        
        private static string logFilePath;
        private static string logsDir;
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

            logsDir = Path.Combine(baseDir, "data", "logs");
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            // Log-Datei mit Datum im Namen
            string logFileName = $"LRCatalogSync_{DateTime.Now:yyyy-MM-dd}.log";
            logFilePath = Path.Combine(logsDir, logFileName);
            
            // Prüfe am Start ob Rotation nötig ist
            CheckAndRotateLog();
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

        // Prüfe ob Log-Rotation nötig ist und führe sie durch
        private static void CheckAndRotateLog()
        {
            try
            {
                if (!File.Exists(logFilePath))
                    return;

                FileInfo fileInfo = new FileInfo(logFilePath);
                
                // Wenn Datei größer als 20MB ist - führe Rotation durch
                if (fileInfo.Length >= MAX_LOG_FILE_SIZE)
                {
                    RotateLogFiles();
                }
            }
            catch
            {
                // Stille Fehlerbehandlung
            }
        }

        // Rotiere die Log-Dateien (Ringspeicher)
        private static void RotateLogFiles()
        {
            try
            {
                lock (lockObj)
                {
                    string currentBaseLogName = Path.Combine(logsDir, $"LRCatalogSync_{DateTime.Now:yyyy-MM-dd}");
                    
                    // Verschiebe alte Backups (Ringspeicher: .log.1 -> .log.2, aber max 3 alte Dateien)
                    for (int i = MAX_BACKUP_FILES - 1; i >= 1; i--)
                    {
                        string oldBackupPath = $"{currentBaseLogName}.{i}.log";
                        string newBackupPath = $"{currentBaseLogName}.{i + 1}.log";
                        
                        if (File.Exists(oldBackupPath))
                        {
                            // Lösche die älteste Datei wenn wir das Limit erreicht haben
                            if (i == MAX_BACKUP_FILES - 1)
                            {
                                File.Delete(oldBackupPath);
                            }
                            else
                            {
                                // Verschiebe zu einer höheren Nummer (erst löschen, dann verschieben)
                                if (File.Exists(newBackupPath))
                                    File.Delete(newBackupPath);
                                File.Move(oldBackupPath, newBackupPath);
                            }
                        }
                    }
                    
                    // Benenne die aktuelle Log-Datei um zu .1.log
                    string firstBackupPath = $"{currentBaseLogName}.1.log";
                    if (File.Exists(firstBackupPath))
                        File.Delete(firstBackupPath);
                    File.Move(logFilePath, firstBackupPath);
                    
                    // Aktualisiere logFilePath für die neue Datei
                    logFilePath = Path.Combine(logsDir, $"LRCatalogSync_{DateTime.Now:yyyy-MM-dd}.log");
                }
            }
            catch
            {
                // Falls Rotation fehlschlägt - ignorieren und weitermachen
            }
        }

        public static void Write(string message, string level = "INFO")
        {
            if (!ShouldLog(level))
                return;

            try
            {
                lock (lockObj)
                {
                    // Prüfe vor jedem Write ob Rotation nötig ist
                    CheckAndRotateLog();
                    
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
