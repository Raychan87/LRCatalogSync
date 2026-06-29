using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

using LRCatalogSync.Infrastructure;
using LRCatalogSync.UI;

namespace LRCatalogSync.Core
{
    /// <summary>
    /// Koordinator für sequenzielle Ausführung von Backup und Katalog-Sync
    /// Stellt sicher dass BackupManager und CatalogManager NACHEINANDER laufen
    /// </summary>
    public static class Coordinator
    {
        // Lock gegen parallele Ausführung
        private static readonly object cycleLock = new object();
        private static bool isCycleRunning = false;

        /// <summary>
        /// Führt kompletten Sync-Zyklus aus: Backup → Katalog-Sync
        /// Wird vom Timer in LRCatSync aufgerufen
        /// </summary>
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
                Log.Debug("Coordinator: Starte Sync-Zyklus (Backup → Katalog)");

                // ========== SCHRITT 1: BackupManager ausführen ==========
                // BackupManager synchronisiert BackupsLocalPath → NAS
                Log.Info("Coordinator: Starte BackupManager");
                trayManager.UpdateStatus("Syncing");
                
                try
                {
                    BackupManager.RunBackupProcess(config, trayManager);
                    Log.Info("Coordinator: BackupManager abgeschlossen");
                }
                catch (Exception ex)
                {
                    Log.Error($"Coordinator: BackupManager fehlgeschlagen: {ex.Message}");
                    trayManager.UpdateStatus("Error");
                    // Mache weiter mit Katalog-Sync auch wenn Backup fehlerhaft war
                }

                // ========== SCHRITT 2: Katalog-Sync ausführen ==========
                // CatalogManager synchronisiert CatalogLocalPath → NAS (oder umgekehrt)
                Log.Info("Coordinator: Starte CatalogManager");
                trayManager.UpdateStatus("Syncing");
                
                try
                {
                    CatalogManager.RunCatalogSync(config, trayManager);
                    Log.Info("Coordinator: CatalogManager abgeschlossen");
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
    }
}
