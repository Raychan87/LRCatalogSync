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
        
        // Sync-Lock Timeout - wann ein Lock als "Veraltet" gilt (30 Minuten)
        public const int SYNC_LOCK_TIMEOUT_MIN = 30;
        
        // Heartbeat-Intervall für Lock-Aktualisierung (2,5 Minuten)
        public const int HEARTBEAT_INTERVAL_SEC = 150;
        
        // Rclone-Timeouts (Go-Duration-Format, z.B. "30s")
        public const string RCLONE_CONNECT_TIMEOUT = "30s";
        
        // Lock-Dateinamen für Synchronisation
        public const string LOCK_FILE = "LRCatSync.lock";
    }    
}

