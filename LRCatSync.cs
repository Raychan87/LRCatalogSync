using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LRCatalogSync
{
    /// <summary>
    /// Vereinfachte Variante der Anwendung: Nur Tray-Icon, Einstellungen und Backup-Sync
    /// </summary>
    public class LRCatSync : ApplicationContext
    {
        // ==================== EIGENSCHAFTEN ====================
        private NotifyIcon trayIcon;           // Tray-Icon für die Anwendung in der Taskleiste
        private AppConfig config;               // Konfigurationsdaten laden/speichern
        private Icon iconGreen;                 // Statusicon: Grün (Standby)
        private Icon iconRed;                   // Statusicon: Rot (Fehler)
        private Icon iconYellow;                // Statusicon: Gelb (Syncing)

        private readonly SynchronizationContext uiContext; // Sichert UI-Thread-Zugriff

        // ==================== KONSTRUKTOR ====================
        /// <summary>
        /// Initialisiert die Anwendung: Logs, Config, Tray-Icon und Kontextmenü
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

            // UI-Kontext speichern für Thread-sichere Updates
            uiContext = SynchronizationContext.Current;

            // ========== ICONS ERSTELLEN ==========
            // Erstelle farbige Kreisicons für verschiedene Status
            iconGreen = CreateColoredIcon(Color.Green);   // Standby
            iconRed = CreateColoredIcon(Color.Red);       // Fehler
            iconYellow = CreateColoredIcon(Color.Orange); // Syncing

            // ========== TRAY-ICON EINRICHTEN ==========
            trayIcon = new NotifyIcon()
            {
                Icon = iconGreen,
                Text = "LR Catalog Sync - Standby",
                Visible = true
            };

            // ========== KONTEXTMENÜ ERSTELLEN ==========
            var menu = new ContextMenuStrip();

            // Status-Anzeige (nur anzeigen, nicht anklickbar)
            var statusItem = new ToolStripMenuItem("Status: Standby") { Enabled = false, Name = "statusItem" };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());

            // Menü-Eintrag: Einstellungen öffnen
            var settingsItem = new ToolStripMenuItem("Einstellungen");
            settingsItem.Click += (s, e) =>
            {
                using (var form = new SettingsForm(config))
                {
                    // Zeige Einstellungs-Dialog
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        // Config neu laden (wurde in SettingsForm gespeichert)
                        config = AppConfig.LoadFromFile(GlobalData.LRCatSyncConfigPath, GlobalData.BaseDir);
                        Log.SetLogLevel(config.LogLevel);
                        Log.Info("Einstellungen aktualisiert");
                        UpdateStatus("Standby");
                    }
                }
            };
            menu.Items.Add(settingsItem);

            // Menü-Eintrag: Backup jetzt manuell starten
            var backupNow = new ToolStripMenuItem("Backup jetzt");
            backupNow.Click += (s, e) =>
            {
                UpdateStatus("Syncing"); // Tray-Icon auf "Syncing" setzen

                // Backup in Background-Thread ausführen (UI nicht blockieren)
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        // ========== VALIDIERUNGEN ==========
                        // Prüfe ob rclone.conf existiert
                        if (!File.Exists(GlobalData.RcloneConfigPath))
                        {
                            Log.Error("rclone.conf fehlt. Bitte Einstellungen prüfen.");
                            UpdateStatus("Error");
                            return;
                        }

                        // Prüfe ob rclone.exe existiert
                        if (!File.Exists(config.RclonePath))
                        {
                            Log.Error("rclone.exe nicht gefunden. Bitte Einstellungen prüfen.");
                            UpdateStatus("rclone");
                            return;
                        }

                        // ========== BACKUP STARTEN ==========
                        if (config.EnableBackups)
                        {
                            try
                            {
                                // 1. CheckBackup(): Prüfe mit --dry-run ob Änderungen vorhanden sind
                                if (CheckBackup())
                                {
                                    // 2. Wenn Änderungen gefunden: SyncBackups() ausführen
                                    SyncBackups();
                                }
                                else
                                {
                                    Log.Info("Backup: Keine Änderungen gefunden");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Backup-Fehler: {ex.Message}");
                            }
                        }
                        else
                        {
                            Log.Info("Backup ist deaktiviert in den Einstellungen");
                        }
                    }
                    finally
                    {
                        // Nach Backup immer zu "Standby" zurücksetzen
                        UpdateStatus("Standby");
                    }
                });
            };
            menu.Items.Add(backupNow);

            // Menü-Trennlinie
            menu.Items.Add(new ToolStripSeparator());

            // Menü-Eintrag: Programm beenden
            var exitItem = new ToolStripMenuItem("Beenden");
            exitItem.Click += (s, e) => { trayIcon.Visible = false; Application.Exit(); };
            menu.Items.Add(exitItem);

            // Menü an Tray-Icon binden
            trayIcon.ContextMenuStrip = menu;
        }

        // ==================== STATUS-VERWALTUNG ====================
        /// <summary>
        /// Aktualisiert den Status im Tray-Icon (mit Thread-Safety)
        /// </summary>
        /// <param name="state">Neuer Status (Standby, Syncing, rclone, Error)</param>
        private void UpdateStatus(string state)
        {
            // Wenn kein UI-Kontext vorhanden, direkt setzen
            if (uiContext == null)
            {
                SetTrayText(state);
                return;
            }

            // Prüfe ob bereits im UI-Thread
            if (SynchronizationContext.Current == uiContext)
            {
                // Ja: direkt setzen
                SetTrayText(state);
            }
            else
            {
                // Nein: Post in UI-Thread zum Setzen
                uiContext.Post(_ => SetTrayText(state), null);
            }
        }

        /// <summary>
        /// Setzt Icon und Text des Tray-Icons basierend auf Status
        /// </summary>
        /// <param name="state">Status (Standby, Syncing, rclone, Error)</param>
        private void SetTrayText(string state)
        {
            switch (state)
            {
                case "Standby":
                    trayIcon.Icon = iconGreen;
                    trayIcon.Text = "LR Catalog Sync - Standby";
                    break;
                case "Syncing":
                    trayIcon.Icon = iconYellow;
                    trayIcon.Text = "LR Catalog Sync - Synchronisiere...";
                    break;
                case "rclone":
                    trayIcon.Icon = iconRed;
                    trayIcon.Text = "LR Catalog Sync - rclone Fehler";
                    break;
                case "Error":
                    trayIcon.Icon = iconRed;
                    trayIcon.Text = "LR Catalog Sync - Fehler";
                    break;
            }
        }

        // ==================== BACKUP-FUNKTIONEN ====================
        /// <summary>
        /// Prüft mit rclone --dry-run ob Backup-Unterschiede vorhanden sind
        /// Gibt true zurück wenn Änderungen gefunden, false wenn keine Änderungen
        /// </summary>
        private bool CheckBackup()
        {
            try
            {
                // ========== REMOTE-PFAD ZUSAMMENSTELLEN ==========
                // z.B. "synology:" + "/Lightroom/Backups" = "synology:/Lightroom/Backups"
                string remoteFull = GlobalConst.REMOTE_NAME;
                if (!string.IsNullOrEmpty(config.BackupsRemotePath))
                    remoteFull += ":" + config.BackupsRemotePath;

                // ========== LOG-DATEI VORBEREITEN ==========
                // Speicherpfad für rclone Logdatei
                string tempLog = Path.Combine(GlobalData.BaseDir, "data", "logs", "rclone_backup_check.log");
                string logsDir = Path.Combine(GlobalData.BaseDir, "data", "logs");

                // Erstelle Logs-Verzeichnis falls nicht vorhanden
                if (!Directory.Exists(logsDir))
                    Directory.CreateDirectory(logsDir);

                // Alte Log-Datei löschen
                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                // ========== RCLONE PROZESS STARTEN ==========
                // Starte rclone mit bisync --dry-run (Probe ohne echte Änderungen)
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    // bisync: bidirektionale Sync-Prüfung
                    // --dry-run: nur prüfen, nichts ändern
                    // --log-level: Logging-Verbosität
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" bisync \"{config.BackupsLocalPath}\" {remoteFull} --compare modtime,size --metadata --log-file \"{tempLog}\" --log-level {config.LogLevel} --dry-run",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Warte max. WATCHDOG_TIME Sekunden auf rclone
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(GlobalConst.WATCHDOG_TIME * 1000);
                }

                // Kurze Pause damit Log-Datei vollständig geschrieben wird
                Thread.Sleep(300);

                // ========== LOG-DATEI AUSLESEN ==========
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
        /// Überwacht Verbindung: Bricht ab wenn Remote 5 Sekunden nicht erreichbar
        /// </summary>
        private void SyncBackups()
        {
            try
            {
                // ========== REMOTE-PFAD ZUSAMMENSTELLEN ==========
                string remoteFull = GlobalConst.REMOTE_NAME;
                if (!string.IsNullOrEmpty(config.BackupsRemotePath))
                    remoteFull += ":" + config.BackupsRemotePath;

                // ========== LOG-DATEI VORBEREITEN ==========
                string tempLog = Path.Combine(GlobalData.BaseDir, "data", "logs", "rclone_backup_sync.log");
                string logsDir = Path.Combine(GlobalData.BaseDir, "data", "logs");
                if (!Directory.Exists(logsDir))
                    Directory.CreateDirectory(logsDir);

                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                // ========== RCLONE PROZESS STARTEN ==========
                // Starte rclone bisync (echte Synchronisation, OHNE --dry-run)
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    // Unterschied zu CheckBackup: OHNE --dry-run, echte Änderungen werden durchgeführt
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" bisync \"{config.BackupsLocalPath}\" {remoteFull} --compare modtime,size --metadata --log-file \"{tempLog}\" --log-level {config.LogLevel}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Starte rclone und überwache Verbindung
                using (var p = Process.Start(psi))
                {
                    DateTime lastConnected = DateTime.Now;

                    // Solange rclone noch läuft...
                    while (!p.HasExited)
                    {
                        // Prüfe ob Remote (Samba-Server) noch erreichbar ist (Ping)
                        if (IsRemoteReachable())
                        {
                            // Ja: Aktualisiere letzten Verbindungszeitpunkt
                            lastConnected = DateTime.Now;
                        }
                        else if ((DateTime.Now - lastConnected).TotalSeconds > 5)
                        {
                            // Nein: Und länger als 5 Sekunden offline
                            Log.Debug("Samba nicht erreichbar - breche ab");
                            p.Kill(); // Stoppe rclone-Prozess
                            break;
                        }

                        Thread.Sleep(1000); // Prüfe jede Sekunde
                    }

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

        /// <summary>
        /// Liest rclone Logdatei und gibt wichtige Statistiken aus (Copied, Deleted, etc.)
        /// </summary>
        /// <param name="logFile">Pfad zur rclone Logdatei</param>
        private void WriteRcloneStats(string logFile)
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

        // ==================== HILFSFUNKTIONEN ====================
        /// <summary>
        /// Liest Datei mit Retry-Logik (falls Datei noch gesperrt ist)
        /// </summary>
        /// <param name="filePath">Pfad zur Datei</param>
        /// <param name="maxRetries">Max. Anzahl Versuche</param>
        /// <param name="delayMs">Wartezeit zwischen Versuchen in ms</param>
        /// <returns>Array von Zeilen oder leeres Array wenn Fehler</returns>
        private string[] ReadLogFileWithRetry(string filePath, int maxRetries = 3, int delayMs = 200)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (!File.Exists(filePath))
                        return new string[0];

                    // Versuche Datei zu lesen
                    return File.ReadAllLines(filePath);
                }
                catch (IOException)
                {
                    // Datei ist noch gesperrt, warte und versuche erneut
                    if (i < maxRetries - 1) 
                        Thread.Sleep(delayMs); 
                    else 
                        throw; // Nach max. Versuchen: Fehler werfen
                }
            }
            return new string[0];
        }

        /// <summary>
        /// Erstellt ein farbiges Kreis-Icon (32x32 Pixel) für Tray
        /// </summary>
        /// <param name="color">Farbe des Kreises</param>
        /// <returns>Icon für Tray-Anzeige</returns>
        private Icon CreateColoredIcon(Color color)
        {
            // ========== BITMAP ERSTELLEN ==========
            var bitmap = new Bitmap(32, 32);

            using (var g = Graphics.FromImage(bitmap))
            {
                // Transparent machen (Alpha-Kanal)
                g.Clear(Color.Transparent);

                // ========== KREIS ZEICHNEN ==========
                // Gefüllter Kreis (Ellipse) mit Farbe
                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, 2, 2, 28, 28);

                // Weißer Rand um den Kreis
                using (var pen = new Pen(Color.White, 2))
                    g.DrawEllipse(pen, 2, 2, 28, 28);
            }

            // Konvertiere Bitmap zu Icon und gebe zurück
            return Icon.FromHandle(bitmap.GetHicon());
        }

        /// <summary>
        /// Prüft ob Remote (Samba-Server) mit Ping erreichbar ist
        /// </summary>
        /// <returns>true wenn Server antwortet, false wenn nicht erreichbar</returns>
        private bool IsRemoteReachable()
        {
            try
            {
                // ========== PING SENDEN ==========
                var ping = new System.Net.NetworkInformation.Ping();
                // Sende Ping mit 2 Sekunden Timeout
                var reply = ping.Send(config.RemoteIP, 2000);

                // Prüfe ob Reply erfolgreich war
                return reply?.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                Log.Debug("Ping fehlgeschlagen");
                return false;
            }
        }
    }
}
