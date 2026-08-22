
namespace LRCatalogSync.UI
{
    // Manager für Tray-Icon Verwaltung und Status-Updates
    public class TrayManager
    {
        // ==================== EIGENSCHAFTEN ====================
        private NotifyIcon trayIcon;                           // Tray-Icon in der Taskleiste
        private Icon iconGreen;                                 // Status: Standby
        private Icon iconRed;                                   // Status: Fehler
        private Icon iconOrange;                               // Status: Syncing
        private Icon iconYellow;                                // Status: Syncing
        private Icon iconBlue;                                  // Status: Lockfile erkannt
        private Icon iconWhite;                                 // Status: Keine Samba-Verbindung
        private Icon iconMagenta;                               // Status: Remote Lockfile aktiv
        private Icon iconLightBlue;                                  // Status: Crash-Recovery aktiv
        private readonly SynchronizationContext? uiContext = null!;      // Für Thread-sichere UI-Updates
        // ==================== KONSTRUKTOR ====================
        // Initialisiert TrayManager mit Icons und Tray-Icon
        public TrayManager()
        {
            // Speichere UI-Kontext für Thread-sichere Updates
            uiContext = SynchronizationContext.Current;

            // ========== ICONS LADEN ==========
            iconGreen = LoadIcon("tray_green.ico");         // Standby
            iconRed = LoadIcon("tray_red.ico");             // Fehler
            iconOrange = LoadIcon("tray_orange.ico");       // Syncing
            iconYellow = LoadIcon("tray_yellow.ico");       // Syncing
            iconLightBlue = LoadIcon("tray_lightblue.ico"); // Lockfile erkannt
            iconWhite = LoadIcon("tray_white.ico");         // Keine Samba-Verbindung
            iconMagenta = LoadIcon("tray_violet.ico");      // Remote Lockfile aktiv
            iconBlue = LoadIcon("tray_blue.ico");           // Crash-Recovery aktiv

            // ========== TRAY-ICON EINRICHTEN ==========
            trayIcon = new NotifyIcon()
            {
                Icon = iconGreen,
                Text = "LR Catalog Sync - Standby",
                Visible = true
            };
        }

        // ==================== ÖFFENTLICHE FUNKTIONEN ====================
        // Gibt das NotifyIcon zurück (für ContextMenuStrip Zuweisung)
        // returns: Das verwaltete Tray-Icon
        public NotifyIcon GetTrayIcon()
        {
            return trayIcon;
        }

        // Aktualisiert den Status im Tray-Icon (mit Thread-Safety)
        // state: Neuer Status (Standby, Syncing, rclone, Error)
        public void UpdateStatus(string state)
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

        // ==================== PRIVATE HILFSFUNKTIONEN ====================
        // Setzt Icon und Text des Tray-Icons basierend auf Status
        // state: Status (Standby, Syncing, rclone, Error, Lockfile, NoSamba)
        private void SetTrayText(string state)
        {
            switch (state)
            {
                case "NoCfg":
                    trayIcon.Icon = iconWhite;
                    trayIcon.Text = "LRCatSync: Konfigurationsdateien fehlen!";
                    break;
                case "Standby":
                    trayIcon.Icon = iconGreen;
                    trayIcon.Text = "LRCatSync: wartet auf Änderungen...";
                    break;
                case "BSyncing":
                    trayIcon.Icon = iconOrange;
                    trayIcon.Text = "LRCatSync: synchronisiere Lightroom Sicherungsordner.";
                    break;
                case "LSyncing":
                    trayIcon.Icon = iconYellow;
                    trayIcon.Text = "LRCatSync: synchronisiere Lightroom Katalog.";
                    break;
                case "RcloneCfg":
                    trayIcon.Icon = iconRed;
                    trayIcon.Text = "LRCatSync: rclone Konfigurationsdatei fehlt!";
                    break;
                case "RcloneExe":
                    trayIcon.Icon = iconRed;
                    trayIcon.Text = "LRCatSync: rclone.exe fehlt!";
                    break;
                case "Error":
                    trayIcon.Icon = iconRed;
                    trayIcon.Text = "LRCatSync: Interner Programm fehler, bitte Log überprüfen!";
                    break;
                case "Lockfile":
                    trayIcon.Icon = iconLightBlue;
                    trayIcon.Text = "LRCatSync: Lightroom Classic ist aktiv.";
                    break;
                case "NoSamba":
                    trayIcon.Icon = iconRed;
                    trayIcon.Text = "LRCatSync: Keine Verbindung zum Samba Server!";
                    break;
                case "RemoteLockfile":
                    trayIcon.Icon = iconMagenta;
                    trayIcon.Text = "LRCatSync: Remote Sync ist aktiv.";
                    break;
                case "LockfileErr":
                    trayIcon.Icon = iconRed;
                    trayIcon.Text = "LRCatSync: Veralteter Remote Sync Prozess erkannt, bitte Remotepfad prüfen!";
                    break;
                case "CrashRecovery":
                    trayIcon.Icon = iconBlue;
                    trayIcon.Text = "LRCatSync: Crash-Recovery läuft...";
                    break;
                
            }
        }

        private static Icon LoadIcon(string fileName)
        {
            var resourceName = $"LRCatalogSync.Resources.Icons.{fileName}";
            using var stream = typeof(TrayManager).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Icon-Ressource nicht gefunden: {resourceName}");
            using var icon = new Icon(stream);
            return new Icon(icon, icon.Width, icon.Height);
        }

        // Gibt alle verwalteten Ressourcen frei
        public void Dispose()
        {
            // Icons freigeben (GDI+ Ressourcen)
            iconGreen?.Dispose();
            iconRed?.Dispose();
            iconOrange?.Dispose();
            iconYellow?.Dispose();
            iconLightBlue?.Dispose();
            iconWhite?.Dispose();
            iconMagenta?.Dispose();
            iconBlue?.Dispose();

            // Tray-Icon entfernen und freigeben
            trayIcon?.Dispose();
        }
    }
}
