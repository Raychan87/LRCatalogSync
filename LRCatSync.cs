using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LRCatalogSync
{
    /// <summary>
    /// Hauptklasse: Startet und verwaltet die Anwendung
    /// Delegiert Backup-Logik an BackupManager und UI an TrayManager
    /// Startet automatischen Backup-Zyklus
    /// </summary>
    public class LRCatSync : ApplicationContext
    {
        // ==================== EIGENSCHAFTEN ====================
        private AppConfig config;                           // Konfigurationsdaten laden/speichern
        private TrayManager trayManager;                    // Manager für Tray-Icon und Status
        private System.Threading.Timer backupTimer;         // Timer für periodische Backup-Überprüfung
        private readonly object backupLock = new object();  // Lock für Thread-Sicherheit
        private bool isBackupRunning = false;               // Flag: Backup läuft gerade

        // ==================== KONSTRUKTOR - HAUPTEINSTIEGSPUNKT ====================
        /// <summary>
        /// Initialisiert die Anwendung: Logs, Config, Tray und Menü
        /// </summary>
        public LRCatSync()
        {
            // ========== INITIALISIERUNG ==========
            // Logs im Verzeichnis data/logs erstellen
            Log.Initialize(GlobalData.BaseDir);
            Log.Info("LR Catalog Sync gestartet");

            // Config aus Datei laden (falls vorhanden, sonst Standard-Einstellungen)
            config = AppConfig.LoadFromFile(GlobalData.LRCatSyncConfigPath, GlobalData.BaseDir);
            Log.SetLogLevel(config.LogLevel);

            // ========== MANAGER INITIALISIEREN ==========
            // Erstelle TrayManager für UI-Verwaltung
            trayManager = new TrayManager();

            // ========== KONTEXTMENÜ AUFBAUEN ==========
            SetupContextMenu();

            // ========== STARTE AUTOMATISCHEN BACKUP-ZYKLUS ==========
            // Timer prüft alle BACKUP_CHECK_INTERVAL Sekunden ob Backup-Änderungen vorhanden sind
            // Startet automatisch, wenn Änderungen gefunden werden
            Log.Info($"Starte automatischen Backup-Zyklus ({GlobalConst.BACKUP_CHECK_INTERVAL}sec Intervall)");
            backupTimer = new System.Threading.Timer(BackupTimerCallback, null, 0, GlobalConst.BACKUP_CHECK_INTERVAL * 1000);
        }

        // ==================== MENÜ-SETUP ====================
        /// <summary>
        /// Erstellt das Kontextmenü für das Tray-Icon
        /// </summary>
        private void SetupContextMenu()
        {
            var menu = new ContextMenuStrip();

            // ========== MENÜ-EINTRAG: Status (nur anzeigen) ==========
            var statusItem = new ToolStripMenuItem("Status: Standby") 
            { 
                Enabled = false, 
                Name = "statusItem" 
            };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());

            // ========== MENÜ-EINTRAG: Einstellungen öffnen ==========
            var settingsItem = new ToolStripMenuItem("Einstellungen");
            settingsItem.Click += (s, e) =>
            {
                // Zeige Einstellungs-Dialog
                using (var form = new SettingsForm(config))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        // Config neu laden (wurde in SettingsForm gespeichert)
                        config = AppConfig.LoadFromFile(GlobalData.LRCatSyncConfigPath, GlobalData.BaseDir);
                        Log.SetLogLevel(config.LogLevel);
                        Log.Info("Einstellungen aktualisiert");
                        trayManager.UpdateStatus("Standby");
                    }
                }
            };
            menu.Items.Add(settingsItem);

            // ========== MENÜ-TRENNLINIE ==========
            menu.Items.Add(new ToolStripSeparator());

            // ========== MENÜ-EINTRAG: Programm beenden ==========
            var exitItem = new ToolStripMenuItem("Beenden");
            exitItem.Click += (s, e) => 
            { 
                trayManager.GetTrayIcon().Visible = false;
                Application.Exit(); 
            };
            menu.Items.Add(exitItem);

            // Binde Menü an Tray-Icon
            trayManager.GetTrayIcon().ContextMenuStrip = menu;
        }

        // ==================== TIMER-CALLBACK FÜR AUTOMATISCHE ÜBERPRÜFUNG ====================
        /// <summary>
        /// Timer-Callback: Prüft periodisch ob Backup-Änderungen vorhanden sind
        /// Wird alle BACKUP_CHECK_INTERVAL Sekunden aufgerufen
        /// </summary>
        private void BackupTimerCallback(object? state)
        {
            // Thread-Sicherheit: Keine überlappenden Backup-Prozesse
            lock (backupLock)
            {
                // Wenn bereits ein Backup läuft, diese Iteration überspringen
                if (isBackupRunning)
                {
                    Log.Debug("Backup läuft noch, überspringe diese Überprüfung");
                    return;
                }

                isBackupRunning = true;
            }

            try
            {
                // Führe automatischen Backup-Check aus (delegiert an BackupManager)
                BackupManager.RunBackupProcess(config, trayManager);
            }
            catch (Exception ex)
            {
                Log.Error($"Fehler in BackupTimerCallback: {ex.Message}");
            }
            finally
            {
                lock (backupLock)
                {
                    isBackupRunning = false;
                }
            }
        }

        // ==================== BEREINIGUNG ====================
        /// <summary>
        /// Cleanup: Beende Timer und gebe Ressourcen frei
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Stoppe und dispose Timer
                if (backupTimer != null)
                {
                    backupTimer.Dispose();
                    Log.Info("Backup-Timer beendet");
                }

                // Verstecke Tray-Icon und gebe Ressourcen frei
                if (trayManager != null)
                {
                    trayManager.GetTrayIcon().Visible = false;
                    trayManager.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
