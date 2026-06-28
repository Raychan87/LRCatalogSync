using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace LRCatalogSync
{
    // Manager für Tray-Icon Verwaltung und Status-Updates
    public class TrayManager
    {
        // ==================== EIGENSCHAFTEN ====================
        private NotifyIcon trayIcon;                           // Tray-Icon in der Taskleiste
        private Icon iconGreen;                                 // Status: Standby
        private Icon iconRed;                                   // Status: Fehler
        private Icon iconYellow;                                // Status: Syncing
        private Icon iconBlue;                                  // Status: Lockfile erkannt
        private Icon iconWhite;                                 // Status: Keine Samba-Verbindung
        private readonly SynchronizationContext? uiContext = null!;      // Für Thread-sichere UI-Updates
        // ==================== KONSTRUKTOR ====================
        // Initialisiert TrayManager mit Icons und Tray-Icon
        public TrayManager()
        {
            // Speichere UI-Kontext für Thread-sichere Updates
            uiContext = SynchronizationContext.Current;

            // ========== ICONS ERSTELLEN ==========
            // Erstelle farbige Kreisicons für verschiedene Status
            iconGreen = CreateColoredIcon(Color.Green);   // Standby
            iconRed = CreateColoredIcon(Color.Red);       // Fehler
            iconYellow = CreateColoredIcon(Color.Orange); // Syncing
            iconBlue = CreateColoredIcon(Color.Blue);     // Lockfile erkannt
            iconWhite = CreateColoredIcon(Color.White);   // Keine Samba-Verbindung

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
                case "Lockfile":
                    trayIcon.Icon = iconBlue;
                    trayIcon.Text = "LR Catalog Sync - Lightroom Lockfile erkannt";
                    break;
                case "NoSamba":
                    trayIcon.Icon = iconWhite;
                    trayIcon.Text = "LR Catalog Sync - Keine Verbindung zum Samba Server";
                    break;
            }
        }

        /// Erstellt ein farbiges Kreis-Icon (32x32 Pixel) für Tray
        /// color: Farbe des Kreises
        /// returns: Icon für Tray-Anzeige
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

        // ==================== DISPOSE-MUSTER ====================
        /// Gibt alle verwalteten Ressourcen frei
        public void Dispose()
        {
            // Icons freigeben (GDI+ Ressourcen)
            iconGreen?.Dispose();
            iconRed?.Dispose();
            iconYellow?.Dispose();
            iconBlue?.Dispose();
            iconWhite?.Dispose();
            
            // Tray-Icon entfernen und freigeben
            trayIcon?.Dispose();
        }
    }
}
