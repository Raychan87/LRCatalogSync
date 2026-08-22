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
        private System.Threading.Timer? MainCycleTimer;     // Timer für Sync-Zyklus (Backup + Katalog)

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

            // Autorun aus Registry laden und in Config speichern (für Anzeige in SettingsForm)
            config.AutoRun = Autorun.IsEnabled();

            // ========== MANAGER INITIALISIEREN ==========
            // Erstelle TrayManager für UI-Verwaltung
            trayManager = new TrayManager();
            SMBConnectionManager.Instance.SetStatusCallback(trayManager.UpdateStatus);

            // ========== KONTEXTMENÜ AUFBAUEN ==========
            SetupContextMenu();

            // ========== PRÜFUNG: Config-Datei vorhanden? ==========
            // Wenn Config-Datei fehlt, zeige weißen Status an und BEENDHE Konstruktor
            // Damit ist das Tray-Menü sofort bedienbar - der Nutzer kann eine Config anlegen
            if (!File.Exists(GlobalData.LRCatSyncConfigPath))
            {
                Log.Error("LRCatSync: Konfigurationsdatei fehlt - erstelle Standard-Konfiguration");
                trayManager.UpdateStatus("NoCfg");
                return;
            }

            // ========== CRASH-RECOVERY: Verwaiste Locks bereinigen ==========
            // Nur ausführen, wenn Config existiert (sonst keine SMB-Verbindung nötig)
            if (LockManager.CheckRecovery(config, trayManager))
                Log.Debug("LRCatSync: Crash-Recovery abgeschlossen - nächster Zyklus startet Sync neu");

            // ==================== STARTE SYNC-ZYKLUS ====================
            // Timer führt alle GlobalCycleInterval Sekunden kompletten Zyklus aus (Backup → Katalog)            
            MainCycle();
        }

        // ==================== MENÜ-SETUP ====================
        // Erstellt das Kontextmenü für das Tray-Icon
        private void SetupContextMenu()
        {
            var menu = new ContextMenuStrip();

            // ========== MENÜ-EINTRAG: Status (nur anzeigen) ==========
//            var statusItem = new ToolStripMenuItem("Status: Standby") 
//            { 
//                Enabled = false, 
//                Name = "statusItem" 
//            };
//            menu.Items.Add(statusItem);
//            menu.Items.Add(new ToolStripSeparator());

            // ========== MENÜ-EINTRAG: Einstellungen öffnen ==========
            var settingsItem = new ToolStripMenuItem("Einstellungen");
            settingsItem.Click += (s, e) =>
            {
                // Zeige Einstellungs-Dialog
                using (var form = new SettingsForm(config))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        // Config neu laden (wenn in SettingsForm gespeichert wurde)
                        config = AppConfig.LoadFromFile(GlobalData.LRCatSyncConfigPath, GlobalData.BaseDir);
                        Log.SetLogLevel(config.LogLevel);
                        MainCycle();
                        Log.Info("Config: Einstellungen aktualisiert");
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

        // ==================== TIMER-CALLBACK FÜR COORDINATOR ====================
        // Ein Zyklus des Programms: Backup → Katalog-Sync
        private void MainCycleCallback(object? state)
        {
            // Coordinator übernimmt die sequenzielle Ausführung
            Coordinator.RunCoordinator(config, trayManager);
        }
        // ==================== STARTE SYNC-ZYKLUS ====================
        private void MainCycle()
        {
            // Stoppe vorherigen Timer (falls vorhanden) und starte neuen Timer mit aktuellem Intervall
            MainCycleTimer?.Dispose();    
            Log.Debug($"LRCatSync: Starte Sync-Zyklus ({config.GlobalCycleInterval}sec Intervall)");   
            // Timer führt alle GlobalCycleInterval Sekunden kompletten Zyklus aus (Backup → Katalog)     
            MainCycleTimer = new System.Threading.Timer(MainCycleCallback, null, 0, config.GlobalCycleInterval * 1000);
        }

        // ==================== BEREINIGUNG ====================
        // Cleanup: Beende Timer und gebe Ressourcen frei
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Stoppe und dispose Timer
                if (MainCycleTimer != null)
                {
                    MainCycleTimer.Dispose();
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
