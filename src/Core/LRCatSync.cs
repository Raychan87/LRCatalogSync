using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

using LRCatalogSync.Infrastructure;    // ← für Log, AppConfig, GlobalData
using LRCatalogSync.UI;                // ← für TrayManager

namespace LRCatalogSync.Core
{
    // Hauptklasse: Startet und verwaltet die Anwendung
    // Delegiert Backup-Logik an BackupManager und UI an TrayManager
    // Startet automatischen Backup-Zyklus
    public class LRCatSync : ApplicationContext
    {
        // ==================== EIGENSCHAFTEN ====================
        private AppConfig config;                           // Konfigurationsdaten laden/speichern
        private TrayManager trayManager;                    // Manager für Tray-Icon und Status
        private System.Threading.Timer syncCycleTimer;      // Timer für Sync-Zyklus (Backup + Katalog)

        // ==================== KONSTRUKTOR - HAUPTEINSTIEGSPUNKT ====================
        // Initialisiert die Anwendung: Logs, Config, Tray und Menü
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

            // ========== CRASH-RECOVERY: Verwaiste Locks bereinigen ==========
            // Prüfe beim Programmstart ob verwaiste Locks existieren (>30 min alt)
            LockManager.CleanupStaleLocks(config);

            // ========== KONTEXTMENÜ AUFBAUEN ==========
            SetupContextMenu();

            // ========== STARTE AUTOMATISCHEN SYNC-ZYKLUS ==========
            // Timer führt alle CATALOG_SYNC_CHECK_INTERVAL Sekunden kompletten Zyklus aus (Backup → Katalog)
            Log.Debug($"LRCatSync: Starte Sync-Zyklus ({GlobalConst.CATALOG_SYNC_CHECK_INTERVAL}sec Intervall)");
            syncCycleTimer = new System.Threading.Timer(SyncCycleCallback, null, 0, GlobalConst.CATALOG_SYNC_CHECK_INTERVAL * 1000);
        }

        // ==================== MENÜ-SETUP ====================
        // Erstellt das Kontextmenü für das Tray-Icon
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
                        Log.Info("Config: Einstellungen aktualisiert");
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
        // Timer-Callback: Führt kompletten Sync-Zyklus aus (Backup → Katalog-Sync)
        // Wird alle CATALOG_SYNC_CHECK_INTERVAL Sekunden aufgerufen
        private void SyncCycleCallback(object? state)
        {
            // Coordinator übernimmt die sequenzielle Ausführung
            Coordinator.RunSyncCycle(config, trayManager);
        }

        // ==================== BEREINIGUNG ====================
        // Cleanup: Beende Timer und gebe Ressourcen frei
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Stoppe und dispose Timer
                if (syncCycleTimer != null)
                {
                    syncCycleTimer.Dispose();
                    Log.Debug("LRCatSync:Zyklus Timer beendet");
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
