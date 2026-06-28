using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace LRCatalogSync
{
    /// <summary>
    /// Manager für Tray-Icon Verwaltung und Status-Updates
    /// </summary>
    public class TrayManager
    {
        // ==================== EIGENSCHAFTEN ====================
        private NotifyIcon trayIcon;                           // Tray-Icon in der Taskleiste
        private Icon iconGreen;                                 // Status: Standby
        private Icon iconRed;                                   // Status: Fehler
        private Icon iconYellow;                                // Status: Syncing
        private readonly SynchronizationContext? uiContext = null!;      // Für Thread-sichere UI-Updates
        // ==================== KONSTRUKTOR ====================
        /// <summary>
        /// Initialisiert TrayManager mit Icons und Tray-Icon
        /// </summary>
        public TrayManager()
        {
            // Speichere UI-Kontext für Thread-sichere Updates
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
        }

        // ==================== ÖFFENTLICHE FUNKTIONEN ====================
        /// <summary>
        /// Gibt das NotifyIcon zurück (für ContextMenuStrip Zuweisung)
        /// </summary>
        /// <returns>Das verwaltete Tray-Icon</returns>
        public NotifyIcon GetTrayIcon()
        {
            return trayIcon;
        }

        /// <summary>
        /// Aktualisiert den Status im Tray-Icon (mit Thread-Safety)
        /// </summary>
        /// <param name="state">Neuer Status (Standby, Syncing, rclone, Error)</param>
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
    }
}
