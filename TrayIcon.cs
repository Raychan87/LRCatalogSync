using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace LightroomSync
{
    public class TrayIcon : ApplicationContext
    {
        // Config
        private const int WATCHDOG_TIME = 30000; // Sekunden
        private const int DIFF_SEC = 5; // Sekunden
        private const int REMOTE_TIMEOUT = 5; // Sekunden

        // Variablen
        private NotifyIcon trayIcon;
        private System.Windows.Forms.Timer timer;
        private AppConfig config;
        private string rcloneConfigPath;

        private Icon iconGreen;
        private Icon iconRed;
        private Icon iconBlue;
        private Icon iconYellow;
        private Icon iconWhite;

        private string status = "Standby";
        private bool lockfile = false;

        public TrayIcon()
        {
            // ================= INITIALISIERUNG =================
            // 1. Basis-Verzeichnis holen (wo die .exe liegt)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Logger starten
            Log.Initialize(baseDir);
            Log.Info("=== Lightroom Watcher gestartet ===");

            // rclone.conf
            string rcloneConfigPath = Path.Combine(baseDir, "rclone.conf");
            if (!File.Exists(rcloneConfigPath))
            {
                string appdataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string defaultConfig = Path.Combine(appdataPath, "rclone", "rclone.conf");
                if (File.Exists(defaultConfig))
                {
                    File.Copy(defaultConfig, rcloneConfigPath);
                }
            }
            this.rcloneConfigPath = rcloneConfigPath;

            // Config laden
            string configPath = Path.Combine(baseDir, "config.txt");
            config = AppConfig.LoadFromFile(configPath, baseDir);
            if (!File.Exists(configPath))
            {
                config.Save(configPath);
            }

            // Icons
            iconGreen = CreateColoredIcon(Color.Green);
            iconRed = CreateColoredIcon(Color.Red);
            iconBlue = CreateColoredIcon(Color.DodgerBlue);
            iconYellow = CreateColoredIcon(Color.Orange);
            iconWhite = CreateColoredIcon(Color.White);

            // Tray-Icon
            trayIcon = new NotifyIcon()
            {
                Icon = iconGreen,
                Text = "Lightroom Sync - Start...",
                Visible = true
            };

            // Context-Menü
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem statusItem = new ToolStripMenuItem("Status: Start...");
            statusItem.Name = "statusItem";
            statusItem.Enabled = false;
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Beenden");
            exitItem.Click += (s, e) => {
                Log.Info("Beendet durch Benutzer");
                trayIcon.Visible = false;
                Application.Exit();
            };
            menu.Items.Add(exitItem);
            trayIcon.ContextMenuStrip = menu;

            // Timer
            timer = new System.Windows.Forms.Timer();
            timer.Interval = config.CheckInterval * 1000;
            timer.Tick += Timer_Tick;
            timer.Start();

            Main();

        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            Main();
        }

        private void Main()
        {
            try
            {
                bool CatalogSyncStart = false;
                string SyncDirection = "";

                // ================= 1. LOCK PRÜFEN =================
                if (Directory.Exists(config.LocalPath))
                {
                    string[] lockFiles = Directory.GetFiles(config.LocalPath, "*.lrcat.lock", SearchOption.AllDirectories);
                    if (lockFiles.Length > 0)
                    {
                        if (!lockfile)
                        {
                            Log.Info("Lock erkannt");
                            lockfile = true;
                        }
                        SetStatus("Lock");
                        return;
                    }
                }

                if (lockfile)
                {
                    Log.Info("Lock entfernt - Sync wird fortgesetzt");
                    lockfile = false;
                }

                // ================= 2. Netzwerk Prüfen  =================
                string remoteFull = config.RemoteName;
                if (!string.IsNullOrEmpty(config.RemotePath))
                    remoteFull += ":" + config.RemotePath;

                if (!File.Exists(config.RclonePath) || !File.Exists(rcloneConfigPath))
                {
                    Log.Warn("Keine NAS-Verbindung");
                    SetStatus("NoConnection");
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = config.RclonePath;
                psi.Arguments = $"--config \"{rcloneConfigPath}\" lsd \"{remoteFull}\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(WATCHDOG_TIME);
                    if (p.ExitCode != 0 || output.Length < 10)
                    {
                        Log.Warn("NAS-Verbindung fehlgeschlagen");
                        SetStatus("NoConnection");
                        return;
                    }
                }

                // ================= 4. KATALOG PRÜFEN =================
                DateTime? localDate = GetLocalCatalogDate();
                DateTime? remoteDate = GetRemoteCatalogDate();

                // Änderungszeit von den Katalogen
                Log.Debug($"Catalog: Check: Lokal={Log.FormatDateTime(localDate)} | NAS={Log.FormatDateTime(remoteDate)}");                

                // Wenn lokal ein Katalog existiert, dann...
                if (localDate != null)
                {
                    // Wenn Remote kein Katalog exestiert, dann
                    if (remoteDate == null)
                    {
                        Log.Info("Catalog: Kein Remote-Katalog - Upload");
                        CatalogSyncStart = true;
                        SyncDirection = "upload";
                    }
                    // Wenn beide Kataloge exestieren, dann...
                    else
                    {
                        // Berechne die Differenz der Änderungszeiten in Sekunden
                        double diff = Math.Round(((DateTime)localDate - (DateTime)remoteDate).TotalSeconds, 0);

                        if (diff > DIFF_SEC)
                        {
                            Log.Debug("Catalog: PC New -> Upload");
                            CatalogSyncStart = true;
                            SyncDirection = "upload";
                        }
                        else if (diff < -DIFF_SEC)
                        {
                            Log.Debug("Catalog: Remote New -> Download");
                            CatalogSyncStart = true;
                            SyncDirection = "download";
                        }
                        else
                        {
                            CatalogSyncStart = false;
                            Log.Debug("Catalog: kein Unterschied");
                        }
                    }
                }
                //Wenn lokal kein Katalog existiert, dann...
                else
                {
                    // Wenn Remote ein Katalog exestiert, dann...
                    if (remoteDate != null)
                    {
                        Log.Info("Catalog: Kein Lokaler Katalog - Download");
                        CatalogSyncStart = true;
                        SyncDirection = "download";
                    }
                    else
                    {
                        Log.Error("Catalog: Keine Kataloge vorhanden");
                        SetStatus("Error");
                    }
                }

                // ================= 5. SYNC KATALOG =================
                if (CatalogSyncStart)
                {
                    Log.Debug("Catalog: Sync Start");
                    SetStatus("Syncing");

                    // Katalog sync
                    if (localDate != null && (SyncDirection == "upload" || SyncDirection == "download"))
                    {
                        SyncCatalog(SyncDirection);
                    }
                    CatalogSyncStart = false;
                    Log.Debug("Catalog: Sync End");
                    SetStatus("Standby");
                }

                // ================= 5.SYNC BACKUPS =================
                // Wird geprüft ob Backups synchronisiert werden muss.
                
                Log.Debug("Backup: Checking");
                
                if (CheckBackup())
                {
                    Log.Debug("Backup: Check End");
                    SetStatus("Syncing");
                    Log.Debug("Backup: Sync Start");
                    Thread.Sleep(1000);
                    SyncBackups();
                    Log.Debug("Backup: Sync End");
                }
                Log.Debug("Backup: Check End");
                Log.Debug("Sync loop End");
                Log.Debug("==============================================");
                SetStatus("Standby");

                // ================= 7. Wenn ein Fehler passiert,... =================
            }
            catch (Exception ex)
            {
                Log.Error($"Fehler: {ex.Message}");
                SetStatus("NoConnection");
            }
        }

        private void SyncCatalog(string direction)
        {
            try
            {
                // Erstellt den kompletten Remote-Pfad, z.B. synology:Lightroom
                string remoteFull = config.RemoteName;
                if (!string.IsNullOrEmpty(config.RemotePath))
                    remoteFull += ":" + config.RemotePath; //synology:Lightroom

                Log.Info($"Catalog: {direction} Start");

                // ========== VOR SYNC: Schreibschutz AKTIVIEREN ==========
                SetCatalogReadOnly(true);

                // Temporäre Log-Datei für rclone-Ausgabe im Programm-Ordner
                string tempLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone_temp.log");

                // Alte Log-Datei löschen
                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                // ================= KATALOG SYNC =================
                ProcessStartInfo psi = new ProcessStartInfo(); // Startet einen neuen Prozess (rclone) unsichtbar im Hintergrund
                psi.FileName = config.RclonePath;

                if (direction == "upload")
                {
                    psi.Arguments = $"--config \"{rcloneConfigPath}\" sync \"{config.LocalPath}\" {remoteFull} --update --metadata --log-file \"{tempLog}\" --log-level INFO";
                }
                else
                {
                    psi.Arguments = $"--config \"{rcloneConfigPath}\" sync {remoteFull} \"{config.LocalPath}\" --update --metadata --log-file \"{tempLog}\" --log-level INFO";
                }
                psi.UseShellExecute = false; // Direkter Prozessstart
                psi.RedirectStandardOutput = true; // Ausgabe abfangen
                psi.RedirectStandardError = true;   // Fehler abfangen
                psi.CreateNoWindow = true; // Kein Fenster öffnen

                // ================= rclone Prozess =================
                using (Process p = Process.Start(psi))
                {
                    DateTime lastConnectedTime = DateTime.Now;

                    // Wenn Prozess noch nicht beendet wurde, dann...
                    while (!p.HasExited)
                    {
                        // Remote ist erreichbar, dann...
                        if (IsRemoteReachable())
                        {
                            // Remote ist erreichbar - Zeitpunkt aktualisieren
                            lastConnectedTime = DateTime.Now;
                        }
                        else
                        {
                            // Remote nicht erreichbar - prüfen, ob 5 Sekunden vergangen sind
                            TimeSpan timeSinceLastConnection = DateTime.Now - lastConnectedTime;
                            if (timeSinceLastConnection.TotalSeconds > 5)
                            {
                                Log.Error("NAS nicht erreichbar für 5 Sekunden - Prozess wird beendet");
                                p.Kill();
                                break;
                            }
                        }
                        Thread.Sleep(1000); // Warte 1 Sekunde
                    }
                    p.WaitForExit(); // Warte noch auf Ende, falls nicht gekillt wurde
                }
                Log.Info($"Catalog: {direction} complete");

                WriteRcloneStats(tempLog);

                // ========== NACH SYNC: Schreibschutz DEAKTIVIEREN ==========
                SetCatalogReadOnly(false);

                // ========== Optional: Remote read-only Status aufheben ==========
                RemoveRemoteReadOnly();
            }
            catch (Exception ex)
            {
                Log.Error($"Sync-Fehler: {ex.Message}");
            }
            finally
            {
                // Sicherstellen, dass Schreibschutz am Ende aufgehoben wird
                SetCatalogReadOnly(false);
            }
        }

        private bool CheckBackup()
        {
            try
            {
                bool BackupChange = false;

                // Erstellt den kompletten Remote-Pfad, z.B. synology:Lightroom
                string remoteFull = config.RemoteName;
                if (!string.IsNullOrEmpty(config.RemotePath))
                    remoteFull += ":" + config.RemotePath; //synology:Lightroom

                // Dynamischer lokaler Backups-Pfad
                string includeBackups = $"--include \"{config.BackupsRelativePath}/**\" --include \"{config.BackupsRelativePath}/*/**\" --exclude \"*\"";

                //Debug Adresspfad
                Log.Debug($"Backup: Pfad: {config.LocalPath}/{config.BackupsRelativePath} <-> {remoteFull}/{config.BackupsRelativePath}");

                // Temporäre Log-Datei für rclone-Ausgabe im Programm-Ordner
                string tempLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone_temp.log");

                // Alte Log-Datei löschen
                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                // DRY-RUN: Prüfen ob Änderungen vorhanden sind
                ProcessStartInfo psi = new ProcessStartInfo(); //Startet einen neuen Prozess (rclone) unsichtbar im Hintergrund
                psi.FileName = config.RclonePath;
                psi.Arguments = $"--config \"{rcloneConfigPath}\" bisync \"{config.LocalPath}\" {remoteFull} --compare modtime,size --metadata --log-file \"{tempLog}\" --log-level INFO --dry-run {includeBackups}";
                psi.UseShellExecute = false; //Direkter Prozessstart
                psi.RedirectStandardOutput = true; //Ausgabe abfangen
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;  //Kein Fenster öffnen

                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(WATCHDOG_TIME);
                }
                                
                // Log-Datei auslesen,...
                if (File.Exists(tempLog))
                {
                    //Log-Datei einlesen...
                    string[] lines = File.ReadAllLines(tempLog);
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();

                        // DEBUG: Zeige alle ERROR-Zeilen
                        if (trimmed.Contains("ERROR"))
                        {
                            Log.Debug($"DEBUG ERROR: '{trimmed}'");                        

                            // Prüfe auf Resync-Fehler (flexibler)
                            if (trimmed.Contains("Bisync aborted") &&
                                trimmed.Contains("resync"))
                            {                            
                                Log.Debug("rclone: *.lck Files fehlen, Bisync abgebrochen, es muss --resync gestaret werden.");
                                Log.Debug("Backup: resync Start");
                                SetStatus("Syncing");

                                ProcessStartInfo psi_resync = new ProcessStartInfo(); //Startet einen neuen Prozess (rclone) unsichtbar im Hintergrund
                                psi_resync.FileName = config.RclonePath;
                                psi_resync.Arguments = $"--config \"{rcloneConfigPath}\" bisync \"{config.LocalPath}\" {remoteFull} --resync --metadata --log-file \"{tempLog}\" --log-level INFO {includeBackups}";
                                psi_resync.UseShellExecute = false; //Direkter Prozessstart
                                psi_resync.RedirectStandardOutput = true; //Ausgabe abfangen
                                psi_resync.RedirectStandardError = true;
                                psi_resync.CreateNoWindow = true;  //Kein Fenster öffnen

                                using (Process p = Process.Start(psi_resync))
                                {
                                    p.WaitForExit(WATCHDOG_TIME);
                                }
                                Thread.Sleep(1000);
                                SetStatus("Standby");
                                Log.Debug("Backup: resync End");
                                return false;
                            }
                        }
                    }

                    // Nach dem Resync-Check, jetzt auf "No changes" prüfen
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();

                        if (trimmed.Contains("No changes found"))
                        {
                            Log.Debug("Backup: Keine Änderungen gefunden");
                            BackupChange = false;
                            break;
                        }
                        else if (trimmed.Contains("Skipped"))
                        {
                            Log.Debug("Backup: Änderungen gefunden");
                            BackupChange = true;
                            break;
                        }
                    }
                }
                    return BackupChange;
            }
            catch (Exception ex)
            {
                Log.Error($"Backups-Check-Fehler: {ex.Message}");
                return false;
            }
        }

        private void SyncBackups()
        {
            try
            {
                //Erstellt den kompletten Remote-Pfad, z.B. synology:Lightroom
                string remoteFull = config.RemoteName;
                if (!string.IsNullOrEmpty(config.RemotePath))
                    remoteFull += ":" + config.RemotePath; //synology:Lightroom

                // Dynamischer lokaler Backups-Pfad
                string includeBackups = $"--include \"{config.BackupsRelativePath}/**\" --include \"{config.BackupsRelativePath}/*/**\" --exclude \"*\"";

                //Debug Adresspfad
                Log.Debug($"Backup: Pfad: {config.LocalPath}/{config.BackupsRelativePath} <-> {remoteFull}/{config.BackupsRelativePath}");

                //Temporäre Log-Datei für rclone-Ausgabe im Programm-Ordner
                string tempLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rclone_temp.log");

                // Alte Log-Datei löschen
                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                ProcessStartInfo psi = new ProcessStartInfo(); //Startet einen neuen Prozess (rclone) unsichtbar im Hintergrund
                psi.FileName = config.RclonePath;
                psi.Arguments = $"--config \"{rcloneConfigPath}\" bisync \"{config.LocalPath}\" {remoteFull} --compare modtime,size --metadata --log-file \"{tempLog}\" --log-level INFO {includeBackups}";
                psi.UseShellExecute = false; //Direkter Prozessstart
                psi.RedirectStandardOutput = true; //Ausgabe abfangen
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;  //Kein Fenster öffnen

                // ================= rclone Prozess =================
                using (Process p = Process.Start(psi))
                {
                    DateTime lastConnectedTime = DateTime.Now;

                    // Wenn Prozess noch nicht beendet wurde, dann...
                    while (!p.HasExited)
                    {
                        // Remote ist erreichbar, dann...
                        if (IsRemoteReachable())
                        {
                            // Remote ist erreichbar - Zeitpunkt aktualisieren
                            lastConnectedTime = DateTime.Now;
                        }
                        else
                        {
                            // Remote nicht erreichbar - prüfen, ob 5 Sekunden vergangen sind
                            TimeSpan timeSinceLastConnection = DateTime.Now - lastConnectedTime;
                            if (timeSinceLastConnection.TotalSeconds > 5)
                            {
                                Log.Debug("NAS nicht erreichbar für 5 Sekunden - Prozess wird beendet");
                                SetStatus("NoConnection");
                                p.Kill();
                                break;
                            }
                        }
                        Thread.Sleep(1000); // Warte 1 Sekunde
                    }
                    p.WaitForExit(); // Warte noch auf Ende, falls nicht gekillt wurde
                }
                //Die Log-Datei auswerten und relevante Zeilen ins Log schreiben
                WriteRcloneStats(tempLog);
            }
            catch (Exception ex)
            {
                Log.Error($"Backups-Fehler: {ex.Message}");
            }
        }

        private void WriteRcloneStats(string logFile)
        {
            try
            {
                if (!File.Exists(logFile)) return;

                string[] lines = File.ReadAllLines(logFile);

                foreach (string line in lines)
                {
                    // Suche nach allen relevanten Zeilen
                    string trimmed = line.Trim();

                    // Copied ODER Deleted ODER Transferred (wenn nicht 0) ODER Elapsed
                    if (trimmed.Contains("Copied") ||
                        trimmed.Contains("Deleted") ||
                        (trimmed.Contains("Transferred:") && !trimmed.Contains("0 B / 0 B")) ||
                        trimmed.Contains("Elapsed time:"))
                    {
                        // Prüfe ob es eine relevante Datei ist
                        if (trimmed.Contains(".lrcat") ||
                            trimmed.Contains("Backups/") ||
                            trimmed.Contains("Lightroom-") ||
                            trimmed.Contains("Options") ||
                            trimmed.Contains("Manifest") ||
                            trimmed.Contains("Database") ||
                            trimmed.Contains("Deleted:") ||
                            trimmed.Contains("Transferred:")) 
                          //  trimmed.Contains("Elapsed time:"))
                        {
                            Log.Debug("rclone: " + trimmed);
                        }
                    }
                }
            }
            catch
            {

            }
        }

        private DateTime? GetLocalCatalogDate()
        {
            try
            {
                // Sucht im lokalen Verzeichnis nach .lrcat Dateien und gibt die letzte Änderungszeit der neuesten zurück
                string[] files = Directory.GetFiles(config.LocalPath, "*.lrcat", SearchOption.TopDirectoryOnly);

                // Wenn keine .lrcat Datei gefunden → gib null zurück
                if (files.Length == 0) return null;

                // Finde die neueste Datei 
                FileInfo newest = null;
                foreach (string file in files)
                {
                    FileInfo fi = new FileInfo(file);
                    if (newest == null || fi.LastWriteTime > newest.LastWriteTime)
                        newest = fi;
                }
                return newest?.LastWriteTime;
            }
            catch
            {
                return null;
            }
        }

        private DateTime? GetRemoteCatalogDate()
        {
            try
            {
                string remoteFull = config.RemoteName;
                if (!string.IsNullOrEmpty(config.RemotePath))
                    remoteFull += ":" + config.RemotePath;

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = config.RclonePath;
                psi.Arguments = $"--config \"{rcloneConfigPath}\" lsl {remoteFull} --include \"*.lrcat\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);

                    if (string.IsNullOrEmpty(output)) return null;

                    string[] lines = output.Split('\n');
                    DateTime? newestDate = null;

                    foreach (string line in lines)
                    {
                        // Nur Hauptverzeichnis (kein /)
                        if (line.Contains(".lrcat") && !line.Contains("/") && line.Contains("2026"))
                        {
                            int idx = line.IndexOf("2026");
                            if (idx > 0 && idx + 19 <= line.Length)
                            {
                                string datePart = line.Substring(idx, 19);
                                try
                                {
                                    DateTime dt = DateTime.ParseExact(datePart, "yyyy-MM-dd HH:mm:ss", null);
                                    if (newestDate == null || dt > newestDate)
                                        newestDate = dt;
                                }
                                catch { }
                            }
                        }
                    }
                    return newestDate;
                }
            }
            catch
            {
                return null;
            }
        }

        public void SetStatus(string newStatus)
        {
            if (status == newStatus) return;
            status = newStatus;

            switch (status)
            {
                case "Standby":
                    trayIcon.Icon = iconGreen;
                    trayIcon.Text = "Lightroom Sync - Standby";
                    break;
                case "NoConnection":
                    trayIcon.Icon = iconWhite;
                    trayIcon.Text = "Lightroom Sync - Keine Verbindung!";
                    break;
                case "Lock":
                    trayIcon.Icon = iconBlue;
                    trayIcon.Text = "Lightroom Sync - Lightroom aktiv";
                    break;
                case "Syncing":
                    trayIcon.Icon = iconYellow;
                    trayIcon.Text = "Lightroom Sync - Synchronisiere...";
                    break;
                case "Error":
                    trayIcon.Icon = iconRed;
                    trayIcon.Text = "Lightroom Sync - Kein Katalog gefunden!";
                    break;
            }

            if (trayIcon.ContextMenuStrip != null)
                ((ToolStripMenuItem)trayIcon.ContextMenuStrip.Items["statusItem"]).Text = "Status: " + status;
        }

        private Icon CreateColoredIcon(Color color)
        {
            Bitmap bitmap = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                using (Brush brush = new SolidBrush(color))
                    g.FillEllipse(brush, 2, 2, 28, 28);
                using (Pen pen = new Pen(Color.White, 2))
                    g.DrawEllipse(pen, 2, 2, 28, 28);
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }

        private bool IsRemoteReachable()
        {
            try
            {
                // 1. Holt die NAS-IP aus der Konfiguration
                string remoteIP = config.RemoteIP; // Falls das die IP ist, sonst z.B. "192.168.1.100"
                // 2. Erstellt einen Ping-Objekt
                System.Net.NetworkInformation.Ping ping = new System.Net.NetworkInformation.Ping();
                // 3. Sendet einen Ping mit 2 Sekunden Timeout
                System.Net.NetworkInformation.PingReply reply = ping.Send(remoteIP, 2000); // 2 Sekunden Timeout
                // 4. Prüft ob der Ping erfolgreich war
                return reply?.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                // 5. Bei Fehler wird false zurückgegeben
                Log.Debug("Ping auf die IP war nicht erfolgreich.");
                return false;
            }
        }

        private void SetCatalogReadOnly(bool readOnly)
        {
            try
            {
                // Suche nach *.lrcat im LocalPath (nur oberste Ebene)
                string[] lrcatFiles = Directory.GetFiles(config.LocalPath, "*.lrcat", SearchOption.TopDirectoryOnly);

                if (lrcatFiles.Length == 0)
                {
                    Log.Debug($"Catalog: Keine *.lrcat Datei gefunden in {config.LocalPath}");
                    return;
                }

                // Bearbeite die erste (und normalerweise einzige) Datei
                FileInfo fileInfo = new FileInfo(lrcatFiles[0]);

                if (readOnly)
                {
                    // Schreibschutz AKTIVIEREN
                    fileInfo.Attributes |= FileAttributes.ReadOnly;
                    Log.Info($"Catalog: Schreibschutz aktiviert für {fileInfo.Name}");
                }
                else
                {
                    // Schreibschutz DEAKTIVIEREN
                    fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                    Log.Info($"Catalog: Schreibschutz deaktiviert für {fileInfo.Name}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Fehler beim Setzen von Schreibschutz: {ex.Message}");
            }
        }

        private void RemoveRemoteReadOnly()
        {
            try
            {
                string remoteFull = config.RemoteName;
                if (!string.IsNullOrEmpty(config.RemotePath))
                    remoteFull += ":" + config.RemotePath;

                // Finde die Remote *.lrcat Datei
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = config.RclonePath;
                psi.Arguments = $"--config \"{rcloneConfigPath}\" lsl {remoteFull} --include \"*.lrcat\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);

                    if (!string.IsNullOrEmpty(output))
                    {
                        string[] lines = output.Split('\n');
                        foreach (string line in lines)
                        {
                            if (line.Contains(".lrcat") && !line.Contains("/"))
                            {
                                // Extrahiere den Dateinamen
                                string[] parts = line.Split(new[] { " " }, System.StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 0)
                                {
                                    string fileName = parts[parts.Length - 1];
                                    Log.Debug($"Catalog: Remote *.lrcat gefunden: {fileName}");
                                }
                                break;
                            }
                        }
                    }
                }

                Log.Debug("Catalog: Remote read-only Status überprüft");
            }
            catch (Exception ex)
            {
                Log.Error($"Fehler beim Überprüfen des Remote read-only Status: {ex.Message}");
            }
        }
    }
}