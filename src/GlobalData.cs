using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LRCatalogSync
{
    public static class GlobalData
    {
        public static string BaseDir { get; private set; } = AppDomain.CurrentDomain.BaseDirectory;
        public static string LRCatSyncConfigPath { get; private set; } = Path.Combine(GlobalData.BaseDir, "data", "config", "LRCatSync.conf");
        public static string RcloneConfigPath { get; private set; } = Path.Combine(GlobalData.BaseDir, "data", "config", "rclone.conf");
    }

    public static class GlobalConst
    {
        public const string REMOTE_NAME = "synology";
        public const int WATCHDOG_TIME = 30;                // sec
        public const int BACKUP_CHECK_INTERVAL = 10;        // sec - Zyklus für automatische Backup-Überprüfung
    }
    
}

