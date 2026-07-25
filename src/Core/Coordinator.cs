using LRCatalogSync.Infrastructure;
using LRCatalogSync.UI;

namespace LRCatalogSync.Core
{
    // Koordinator für sequenzielle Ausführung von Backup und Katalog-Sync
    // Stellt sicher dass BackupManager und CatalogManager NACHEINANDER laufen
    public static class Coordinator
    {
        // Lock gegen parallele Ausführung
        private static readonly object cycleLock = new object();
        private static bool isCycleRunning = false;

        // Führt kompletten Sync-Zyklus aus: Backup → Katalog-Sync
        // Wird vom Timer in LRCatSync aufgerufen
        public static void RunSyncCycle(AppConfig config, TrayManager trayManager)
        {
            // Verhindere parallele Ausführung
            lock (cycleLock)
            {
                if (isCycleRunning)
                {
                    Log.Debug("Coordinator: Zyklus läuft bereits - überspringe");
                    return;
                }
                
                isCycleRunning = true;
            }

            try
            {
                // ========== VALIDIERUNGEN ==========
                // Prüfe zuerst ob Config-Datei existiert (wichtig für ersten Start)
                if (!File.Exists(GlobalData.LRCatSyncConfigPath))
                {
                    Log.Debug("Coordinator: Konfigurationsdatei fehlt - überspringe Sync-Zyklus");
                    trayManager.UpdateStatus("NoCfg");
                    return;
                }

                if (!File.Exists(GlobalData.RcloneConfigPath))
                {
                    Log.Error("Coordinator: rclone.conf fehlt. Bitte Einstellungen prüfen.");
                    trayManager.UpdateStatus("Error");
                    return;
                }

                if (!File.Exists(config.RclonePath))
                {
                    Log.Error("Coordinator: rclone.exe nicht gefunden. Bitte Einstellungen prüfen.");
                    trayManager.UpdateStatus("rclone");
                    return;
                }

                // ========== PRÜFUNG: LIGHTROOM LÄUFT? ==========
                // Prüfe ob Lightroom geöffnet ist (Lock-Dateien erkennen)
                // Diese Prüfung gilt für BOTH BackupManager UND CatalogManager
                if (IsLightroomRunning(config))
                {
                    Log.Debug("Coordinator: Lightroom läuft - Backup und Katalog-Sync übersprungen");
                    trayManager.UpdateStatus("Lockfile");
                    return;
                }

                if (!config.EnableBackups)
                {
                    Log.Debug("Coordinator: Backup deaktiviert - überspringe");
                }
                else
                {
                    // ========== SCHRITT 1: BackupManager ausführen ==========
                    // BackupManager synchronisiert BackupsLocalPath → NAS
                    Log.Debug("Coordinator: Starte BackupManager");                
                    try
                    {
                        BackupManager.RunBackupProcess(config, trayManager);
                        Log.Debug("Coordinator: BackupManager abgeschlossen");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Coordinator: BackupManager fehlgeschlagen: {ex.Message}");
                        trayManager.UpdateStatus("Error");
                        // Mache weiter mit Katalog-Sync auch wenn Backup fehlerhaft war
                    }
                }

                // ========== SCHRITT 2: KATALOG-SYNC ==========
                // CatalogManager synchronisiert CatalogLocalPath → NAS (oder umgekehrt)
                Log.Debug("Coordinator: Starte Katalogsync");
                
                try
                {
                    CatalogManager.RunCatalogSync(config, trayManager);
                    Log.Debug("Coordinator: CatalogManager abgeschlossen");
                }
                catch (Exception ex)
                {
                    Log.Error($"Coordinator: CatalogManager fehlgeschlagen: {ex.Message}");
                    trayManager.UpdateStatus("Error");
                }

                // ========== ZYKLUS ABGESCHLOSSEN ==========
                Log.Debug("Coordinator: Sync-Zyklus komplett abgeschlossen");
                trayManager.UpdateStatus("Standby");
            }
            catch (Exception ex)
            {
                Log.Error($"Coordinator: Zyklus fehlgeschlagen: {ex.Message}");
                trayManager.UpdateStatus("Error");
            }
            finally
            {
                // Lock freigeben für nächsten Zyklus
                lock (cycleLock)
                {
                    isCycleRunning = false;
                }
            }
        }

        // Prüft ob Lightroom läuft (sucht nach Lock-Dateien)
        private static bool IsLightroomRunning(AppConfig config)
        {
            try
            {
                string[] lockFiles = {
                    $"{config.CatalogName}.lrcat.lock",
                    $"{config.CatalogName}.lrcat-shm",
                    $"{config.CatalogName}.lrcat-wal"
                };
                
                foreach (string lockFile in lockFiles)
                {
                    string fullPath = Path.Combine(config.CatalogLocalPath, lockFile);
                    if (File.Exists(fullPath))
                    {
                        Log.Notice($"Coordinator: Lightroom-Lock erkannt: {fullPath}");
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"Coordinator: Fehler bei Lightroom-Lock-Prüfung: {ex.Message}");
                return false;
            }
        }
    }
}
