using System;
using System.Windows.Forms;

namespace LRCatalogSync
{
    static class Program
    {
        /// <summary>
        /// Der Einsprungpunkt des Programms.
        /// </summary>
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

