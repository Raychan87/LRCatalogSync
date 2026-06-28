using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace LRCatalogSync
{
    /// <summary>
    /// Manager für alle Backup-Operationen (Check, Sync, Statistiken)
    /// </summary>
    public static class BackupManager
    {
        // ==================== ÖFFENTLICHE FUNKTIONEN ====================
        /// <summary>
        /// Prüft mit rclone --dry-run ob Backup-Unterschiede vorhanden sind
        /// Gibt true zurück wenn Änderungen gefunden, false wenn keine Änderungen
        /// </summary>
        /// <param name="config">App-Konfiguration mit Pfaden und rclone-Pfad</param>
        /// <param name="remoteFullPath">Remote-Pfad z.B. "synology:/Lightroom/Backups"</param>
        /// <returns>true wenn Änderungen, false wenn keine</returns>
        public static bool CheckBackup(AppConfig config, string remoteFullPath)
        {
            try
            {
                // ========== LOG-DATEI VORBEREITEN ==========
                string tempLog = Path.Combine(GlobalData.BaseDir, "data", "logs", "rclone_backup_check.log");
                string logsDir = Path.Combine(GlobalData.BaseDir, "data", "logs");

                // Erstelle Logs-Verzeichnis falls nicht vorhanden
                if (!Directory.Exists(logsDir))
                    Directory.CreateDirectory(logsDir);

                // Alte Log-Datei löschen
                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                // ========== RCLONE PROZESS STARTEN (--dry-run) ==========
                // Starte rclone mit bisync --dry-run (Probe ohne echte Änderungen)
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    // bisync: bidirektionale Sync-Prüfung
                    // --dry-run: nur prüfen, nichts ändern
                    // --log-level: Logging-Verbosität
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" bisync \"{config.BackupsLocalPath}\" {remoteFullPath} --compare modtime,size --metadata --log-file \"{tempLog}\" --log-level {config.LogLevel} --dry-run",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Warte max. WATCHDOG_TIME Sekunden auf rclone
                using (var p = Process.Start(psi))
                {
                    p?.WaitForExit(GlobalConst.WATCHDOG_TIME * 1000);
                }

                // ========== LOG-DATEI AUSLESEN ==========
                // Retry-Logik wartet automatisch falls Datei noch gesperrt ist
                var lines = ReadLogFileWithRetry(tempLog, 5, 200);
                if (lines == null || lines.Length == 0)
                    return false;

                // ========== ERGEBNIS PRÜFEN ==========
                // Suche nach Änderungen-Indikatoren in der rclone Log-Datei
                foreach (var line in lines)
                {
                    var t = line.Trim();

                    // Keine Änderungen gefunden
                    if (t.Contains("No changes found"))
                        return false;

                    // Änderungen gefunden (Copied, Deleted oder Skipped)
                    if (t.Contains("Skipped") || t.Contains("Copied") || t.Contains("Deleted"))
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"Backups-Check-Fehler: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Führt echten Backup-Sync durch (rclone bisync ohne --dry-run)
        /// </summary>
        /// <param name="config">App-Konfiguration</param>
        /// <param name="remoteFullPath">Remote-Pfad</param>
        public static void SyncBackups(AppConfig config, string remoteFullPath)
        {
            try
            {
                // ========== LOG-DATEI VORBEREITEN ==========
                string tempLog = Path.Combine(GlobalData.BaseDir, "data", "logs", "rclone_backup_sync.log");
                string logsDir = Path.Combine(GlobalData.BaseDir, "data", "logs");
                if (!Directory.Exists(logsDir))
                    Directory.CreateDirectory(logsDir);

                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                // ========== RCLONE PROZESS STARTEN (echter Sync) ==========
                // Starte rclone bisync (echte Synchronisation, OHNE --dry-run)
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    // Unterschied zu CheckBackup: OHNE --dry-run, echte Änderungen werden durchgeführt
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" bisync \"{config.BackupsLocalPath}\" {remoteFullPath} --compare modtime,size --metadata --log-file \"{tempLog}\" --log-level {config.LogLevel}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Starte rclone und warte bis Prozess beendet ist
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return;

                    p.WaitForExit(); // Warte bis Prozess beendet ist
                }

                // ========== LOG-STATISTIKEN AUSGEBEN ==========
                // Lese rclone Logdatei und gebe Statistiken aus (Copied, Deleted, etc.)
                WriteRcloneStats(tempLog);
            }
            catch (Exception ex)
            {
                Log.Error($"Backups-Fehler: {ex.Message}");
            }
        }

        // ==================== PRIVATE HILFSFUNKTIONEN ====================
        /// <summary>
        /// Liest rclone Logdatei und gibt wichtige Statistiken aus (Copied, Deleted, etc.)
        /// </summary>
        /// <param name="logFile">Pfad zur rclone Logdatei</param>
        private static void WriteRcloneStats(string logFile)
        {
            try
            {
                // Prüfe ob Logdatei existiert
                if (string.IsNullOrEmpty(logFile) || !File.Exists(logFile)) 
                    return;

                // ========== LOG-DATEI LESEN ==========
                var lines = ReadLogFileWithRetry(logFile, 5, 200);
                if (lines == null || lines.Length == 0) 
                    return;

                // ========== RELEVANTE ZEILEN FILTERN UND LOGGEN ==========
                // Suche nach Statistik-Zeilen in rclone Logdatei
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();

                    // Gebe nur wichtige Zeilen aus (Copied, Deleted, Transferred, Elapsed)
                    if (trimmed.Contains("Copied") || 
                        trimmed.Contains("Deleted") || 
                        (trimmed.Contains("Transferred:") && !trimmed.Contains("0 B / 0 B")) || 
                        trimmed.Contains("Elapsed time:"))
                    {
                        // Ausgabe ins Logfile mit rclone-Präfix
                        Log.Debug("rclone: " + trimmed);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"WriteRcloneStats Fehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Liest Datei mit Retry-Logik (falls Datei noch gesperrt ist)
        /// </summary>
        /// <param name="filePath">Pfad zur Datei</param>
        /// <param name="maxRetries">Max. Anzahl Versuche</param>
        /// <param name="delayMs">Wartezeit zwischen Versuchen in ms</param>
        /// <returns>Array von Zeilen oder leeres Array wenn Fehler</returns>
        private static string[] ReadLogFileWithRetry(string filePath, int maxRetries = 5, int delayMs = 200)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (!File.Exists(filePath))
                        return Array.Empty<string>();

                    // Versuche Datei zu lesen
                    return File.ReadAllLines(filePath);
                }
                catch (IOException)
                {
                    // Datei ist noch gesperrt, warte und versuche erneut
                    if (i < maxRetries - 1)
                        Thread.Sleep(delayMs);
                    else
                        return Array.Empty<string>(); // Nach max. Versuchen: leeres Array zurückgeben
                }
            }
            return Array.Empty<string>();
        }

        // ==================== HAUPTMETHODE: BACKUP-PROZESS ====================
        /// <summary>
        /// Führt kompletten Backup-Prozess aus: Validiert Konfiguration, prüft auf Änderungen mit CheckBackup(),
        /// und startet SyncBackups() wenn Änderungen gefunden werden. Aktualisiert Tray-Status.
        /// </summary>
        /// <param name="config">App-Konfiguration mit Pfaden und Einstellungen</param>
        /// <param name="trayManager">TrayManager für Status-Updates (Syncing/Standby/Error)</param>
        public static void RunBackupProcess(AppConfig config, TrayManager trayManager)
        {
            try
            {
                // ========== VALIDIERUNGEN ==========
                if (!File.Exists(GlobalData.RcloneConfigPath))
                {
                    Log.Error("rclone.conf fehlt. Bitte Einstellungen prüfen.");
                    trayManager.UpdateStatus("Error");
                    return;
                }

                if (!File.Exists(config.RclonePath))
                {
                    Log.Error("rclone.exe nicht gefunden. Bitte Einstellungen prüfen.");
                    trayManager.UpdateStatus("rclone");
                    return;
                }

                // ========== BACKUP PRÜFUNG ==========
                if (!config.EnableBackups)
                {
                    Log.Debug("Backup ist deaktiviert");
                    return;
                }

                // Zusammenstellung des Remote-Pfads (z.B. "synology:/Lightroom/Backups")
                string remoteFullPath = GlobalConst.REMOTE_NAME;
                if (!string.IsNullOrEmpty(config.BackupsRemotePath))
                    remoteFullPath += ":" + config.BackupsRemotePath;

                // 1. Prüfe mit CheckBackup() ob Änderungen vorhanden sind
                if (CheckBackup(config, remoteFullPath))
                {
                    // 2. Änderungen gefunden - Setze Tray auf "Syncing" und starte Sync
                    trayManager.UpdateStatus("Syncing");

                    // 3. Führe echten Sync durch mit SyncBackups()
                    SyncBackups(config, remoteFullPath);
                }
                else
                {
                    Log.Debug("Backup: Keine Änderungen gefunden");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Backup-Fehler: {ex.Message}");
                trayManager.UpdateStatus("Error");
            }
            finally
            {
                // Nach Backup immer zu "Standby" zurücksetzen
                trayManager.UpdateStatus("Standby");
            }
        }
    }
}
