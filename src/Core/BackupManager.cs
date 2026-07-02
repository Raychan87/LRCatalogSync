using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

using LRCatalogSync.Infrastructure;    // ← für Log, AppConfig, GlobalData
using LRCatalogSync.UI;                // ← für TrayManager

namespace LRCatalogSync.Core
{
    // Manager für alle Backup-Operationen (Check, Sync, Statistiken)
    public static class BackupManager
    {
        // ==================== ÖFFENTLICHE FUNKTIONEN ====================
        // Führt Backup-Sync durch (rclone bisync)
        // config: App-Konfiguration
        // remoteFullPath: Remote-Pfad
        public static void SyncBackups(AppConfig config, string remoteFullPath)
        {
            try
            {
                // ========== LOG-EINTRAG: START ==========
                Log.Debug($"Backup: gestartet {config.BackupsLocalPath} -> {remoteFullPath}");

                // ========== LOG-DATEI VORBEREITEN ==========
                string tempLog = Path.Combine(GlobalData.BaseDir, "data", "logs", "rclone_backup_sync.log");
                string logsDir = Path.Combine(GlobalData.BaseDir, "data", "logs");
                if (!Directory.Exists(logsDir))
                    Directory.CreateDirectory(logsDir);

                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                // ========== RCLONE PROZESS STARTEN ==========
                // Starte rclone bisync (Synchronisation)
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
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

                // ========== LOG-EINTRAG: ENDE ==========
                Log.Debug("Backup: abgeschlossen");
            }
            catch (Exception ex)
            {
                Log.Error($"Backup/Fehler: {ex.Message}");
            }
        }

        // ==================== HAUPTMETHODE: BACKUP-PROZESS ====================
        /// Führt kompletten Backup-Prozess aus: Validiert Konfiguration und startet SyncBackups().
        /// Aktualisiert Tray-Status.
        /// config: App-Konfiguration mit Pfaden und Einstellungen
        /// trayManager: TrayManager für Status-Updates (Syncing/Standby/Error)
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

                // ========== BACKUP AUSFÜHREN ==========
                if (!config.EnableBackups)
                {
                    Log.Debug("Backup: deaktiviert");
                    return;
                }

                // Zusammenstellung des Remote-Pfads (z.B. "synology:/Lightroom/Backups")
                string remoteFullPath = GlobalConst.REMOTE_NAME;
                if (!string.IsNullOrEmpty(config.BackupsRemotePath))
                    remoteFullPath += ":" + config.BackupsRemotePath;

                // Setze Tray auf "Syncing" und starte Sync
                trayManager.UpdateStatus("Syncing");

                // Führe Sync durch
                SyncBackups(config, remoteFullPath);
            }
            catch (Exception ex)
            {
                Log.Error($"Backup/Fehler: {ex.Message}");
                trayManager.UpdateStatus("Error");
            }
            finally
            {
                // Nach Backup immer zu "Standby" zurücksetzen
                trayManager.UpdateStatus("Standby");
            }
        }

        // ==================== PRIVATE HILFSFUNKTIONEN ====================
        // Liest rclone Logdatei und gibt wichtige Statistiken aus (Copied, Deleted, etc.)
        // logFile: Pfad zur rclone Logdatei
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
                        Log.Debug("Backup/rclone: " + trimmed);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Backup/rclone/Fehler: {ex.Message}");
            }
        }

        /// Liest Datei mit Retry-Logik (falls Datei noch gesperrt ist)
        /// filePath: Pfad zur Datei
        /// maxRetries: Max. Anzahl Versuche
        /// delayMs: Wartezeit zwischen Versuchen in ms
        /// returns: Array von Zeilen oder leeres Array wenn Fehler
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
    }
}
