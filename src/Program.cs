using System;
using System.Windows.Forms;

using LRCatalogSync.Core;    // ← für LRCatSync

namespace LRCatalogSync
{
    static class Program
    {
        // Der Einsprungpunkt des Programms.
        [STAThread]
        static void Main()
        {
            // Aktiviert visuelle Windows-Styles
            Application.EnableVisualStyles();

            // Setzt Standard-Text-Rendering
            Application.SetCompatibleTextRenderingDefault(false);

            // Startet die Anwendung mit unserem TrayIcon
            Application.Run(new LRCatSync());
        }
    }
}

