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
        
        // Sync-Lock Timeout - wann ein Lock als "stale" gilt (30 Minuten)
        public const int SYNC_LOCK_TIMEOUT_MIN = 30;
        
        // Heartbeat-Intervall für Lock-Aktualisierung (2,5 Minuten)
        public const int HEARTBEAT_INTERVAL_SEC = 150;
        
        // Katalog-Sync Intervall (Sekunden) - Häufigkeit der Prüfzyklen
        public const int CATALOG_SYNC_CHECK_INTERVAL = 30;
        
        // Lock-Dateinamen für Synchronisation
        public const string LOCK_FILE = "LRCatSync.lock";
    }
    
}

