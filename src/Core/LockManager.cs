using System.Text;

using LRCatalogSync.Infrastructure;    // ← für Log, AppConfig, SMBConnectionManager
using LRCatalogSync.UI;

namespace LRCatalogSync.Core
{
    // LockManager für atomare Lock-Akquise lokal + remote
    // Verwaltet LRCatSync.lock Datei für Synchronisation
    public class LockManager : IDisposable
    {
        // ==================== EIGENSCHAFTEN ====================
        // Eindeutige Sync-GUID für Tracking
        public string SyncGuid { get; private set; } = Guid.NewGuid().ToString();

        // Lokale Lock-Datei
        private FileStream? _localLockStream;

        // Heartbeat Thread
        private Thread? _heartbeatThread;

        private CancellationTokenSource? _cts;

        // AppConfig für Lock-Pfade
        private AppConfig? _config;

        // Trackt ob vorher ein Remote Lock vorhanden war (für Cleanup-Logik)
        private static bool wasRemoteLockPresent = false;

        // ==================== KONSTRUKTOR ====================
        public LockManager(AppConfig config)
        {
            _config = config;
        }

        // ==================== STATIC CLEANUP METHODEN ====================
        // geprpüft!! 2026.07.18
        // Bereinigt verwaiste Locks beim Programmstart (Crash-Recovery)
        // Prüft lokale und remote Lock-Dateien und löscht diese wenn älter als SYNC_LOCK_TIMEOUT_MIN
        public static void CleanupStaleLocks(AppConfig config)
        {
            try
            {
                // ========== LOKALE LOCK PRÜFEN ==========
                if (File.Exists(config.SyncLocalLockFile))
                {
                    FileInfo lockFile = new FileInfo(config.SyncLocalLockFile);
                    TimeSpan age = DateTime.Now - lockFile.LastWriteTime;
                    
                    if (age.TotalMinutes > GlobalConst.SYNC_LOCK_TIMEOUT_MIN)
                    {
                        // Lock ist älter als Timeout → Crash-Recovery
                        File.Delete(config.SyncLocalLockFile);
                        Log.Notice($"LockManager: Verwaiste lokale Lock gelöscht ({age.TotalMinutes:F0} min alt)");
                    }
                    else
                    {
                        // Lock ist noch aktiv → Info
                        Log.Notice($"LockManager: Lokale Lock existiert noch ({age.TotalMinutes:F0} min alt) - Anderer Client aktiv?");
                    }
                }
                
                // ========== REMOTE LOCK PRÜFEN (via SMB) ==========
                // Nur versuchen, wenn SMB-Config vollständig ist
                if (string.IsNullOrEmpty(config.RemoteIP) || string.IsNullOrEmpty(config.SambaUser))
                {
                    Log.Debug("LockManager: SMB-Config unvollständig - überspringe Remote-Lock-Prüfung");
                }
                else if (SMBConnectionManager.Instance.EnsureConnected(config))
                {                    
                    // Prüfe ob Remote-Lock existiert
                    byte[]? lockData = SMBConnectionManager.Instance.ReadFile(GlobalConst.LOCK_FILE);
                    if (lockData != null)
                    {
                        string lockContent = Encoding.UTF8.GetString(lockData);
                        
                        // Parse Timestamp aus Lock-Datei
                        if (TryParseLockTimestampStatic(lockContent, out DateTime lockTimestamp))
                        {
                            TimeSpan age = DateTime.Now - lockTimestamp;
                            
                            if (age.TotalMinutes > GlobalConst.SYNC_LOCK_TIMEOUT_MIN)
                            {
                                // Lock ist veraltet → löschen via SMB
                                if (SMBConnectionManager.Instance.DeleteFile(GlobalConst.LOCK_FILE))
                                {
                                    Log.Notice($"LockManager: Verwaiste Remote-Lock gelöscht ({age.TotalMinutes:F0} min alt)");
                                }
                            }
                            else
                            {
                                Log.Notice($"LockManager: Remote Lock existiert noch ({age.TotalMinutes:F0} min alt) - Anderer Client aktiv?");
                            }
                        }
                    }
                }
                else
                {
                    Log.Error("LockManager: Keine SMB-Verbindung möglich, Remote-Lock wurde nicht geprüft");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"LockManager: Fehler beim Bereinigen: {ex.Message}");
            }
        }
  
        // geprpüft!! 2026.07.18
        // Statische Version von TryParseLockTimestamp für CleanupStaleLocks
        private static bool TryParseLockTimestampStatic(string content, out DateTime timestamp)
        {
            timestamp = DateTime.MinValue;
            try
            {
                var lines = content.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("Timestamp="))
                    {
                        string dateStr = line.Substring("Timestamp=".Length);
                        if (DateTime.TryParse(dateStr, out timestamp))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ==================== ÖFFENTLICHE METHODEN ====================
        
        // Prüft ob ein Remote Lock von einem anderen Client aktiv ist
        // Rückgabewerte:
        // 0 = SMB-Fehler oder Fehler beim Lesen/Prüfen der Lock-Datei
        // 1 = Kein Remote Lock vorhanden (normaler Zustand)
        // 2 = Remote Lock vorhanden und aktuell (< 30 min) - andere Client arbeitet
        // 3 = Remote Lock vorhanden aber veraltet (> 30 min) oder Fehlerhaft - manuell prüfen erforderlich
        // 
        // Side-Effects:
        // - Wenn Remote Lock mit Upload: Erstellt Lightroom-Lock lokal
        // - Wenn Remote Lock mit Download: Erstellt KEIN Lightroom-Lock
        // - Wenn Remote Lock verschwindet (war vorher da): Löscht Lightroom-Lock
        public static int CheckRemoteLock(AppConfig config, TrayManager trayManager)
        {
            try
            {
                // ========== VALIDIERUNG: SMB-Config vollständig? ==========
                if (string.IsNullOrEmpty(config.RemoteIP) || string.IsNullOrEmpty(config.SambaUser))
                {
                    Log.Debug("LockManager: SMB-Config unvollständig - überspringe Remote-Lock-Prüfung");
                    trayManager.UpdateStatus("NoSamba");
                    return 0; // SMB-Fehler
                }

                // ========== SMB-VERBINDUNG HERSTELLEN ==========
                if (!SMBConnectionManager.Instance.EnsureConnected(config))
                {
                    Log.Debug("LockManager: SMB-Verbindung fehlgeschlagen, Remote-Lock kann nicht geprüft werden");
                    return 0; // SMB-Fehler
                }

                // ========== REMOTE LOCK PRÜFEN ==========
                // Lädt alle Filenamen von dem Remote Pfad
                var existingFiles = SMBConnectionManager.Instance.ListFiles(Path.GetDirectoryName(config.CatalogRemotePath) ?? "");

                // Prüfe ob das Lockfile vorhanden ist
                if (!existingFiles.Contains(GlobalConst.LOCK_FILE))
                {
                    // ========== REMOTE LOCK VERSCHWUNDEN ==========
                    // Wenn es vorher da war → Cleanup durchführen
                    if (wasRemoteLockPresent)
                    {
                        Log.Debug("LockManager: Remote Lock ist verschwunden - räume Lightroom-Lock auf");
                        CatalogManager.CleanupLightroomLocks(config);
                        wasRemoteLockPresent = false;
                    }
                    
                    Log.Debug("LockManager: Kein Remote Lock vorhanden - andere Clients können arbeiten");
                    return 1; // Kein Lock vorhanden
                }

                // ========== LOCKFILE INHALT LESEN UND PRÜFEN ==========
                byte[]? lockData = SMBConnectionManager.Instance.ReadFile(GlobalConst.LOCK_FILE);
                if (lockData == null)
                {
                    Log.Error("LockManager: Remote Lock-Datei konnte nicht gelesen werden");
                    trayManager.UpdateStatus("NoSamba");
                    return 0; // SMB-Fehler
                }

                string lockContent = Encoding.UTF8.GetString(lockData);
                DateTime lastHeartbeat = ExtractLatestTimestamp(lockContent);

                if (lastHeartbeat == DateTime.MinValue)
                {
                    Log.Error("LockManager: Remote Lock-Datei hat ungültiges Format");
                    trayManager.UpdateStatus("LockfileErr");
                    return 3; // Fehlerhaft
                }

                // ========== ALTER DES LOCKS PRÜFEN ==========
                TimeSpan lockAge = DateTime.UtcNow - lastHeartbeat;

                if (lockAge.TotalMinutes > GlobalConst.SYNC_LOCK_TIMEOUT_MIN)
                {
                    // Lock ist älter als Timeout → Warnung ausgeben
                    Log.Error($"LockManager: Remote Lock ist älter als {GlobalConst.SYNC_LOCK_TIMEOUT_MIN} min ({lockAge.TotalMinutes:F0} min alt). " +
                              $"Ein anderer Client könnte gecrasht sein. Bitte manuell prüfen!");
                    trayManager.UpdateStatus("LockfileErr");
                    wasRemoteLockPresent = false; // Lock ist nicht mehr gültig
                    return 3; // Lock veraltet
                }

                // ========== LOCK IST AKTIV - PRÜFE DIRECTION ==========
                CatalogManager.SyncDirection direction = ExtractDirection(lockContent);
                
                if (direction == CatalogManager.SyncDirection.Upload)
                {
                    // Remote Lock ist Upload → Erstelle Lightroom-Lock lokal
                    Log.Debug("LockManager: Remote Lock ist UPLOAD - erstelle Lightroom-Lock");
                    CatalogManager.CreateLightroomLock(config);
                }
                else if (direction == CatalogManager.SyncDirection.Download)
                {
                    // Remote Lock ist Download → Kein Lightroom-Lock nötig
                    Log.Debug("LockManager: Remote Lock ist DOWNLOAD - kein Lightroom-Lock nötig");
                }
                
                Log.Debug($"LockManager: Remote Lock von anderem Client aktiv ({lockAge.TotalMinutes:F1} min alt, {direction}). Warte auf Freigabe...");
                trayManager.UpdateStatus("RemoteLockfile");
                wasRemoteLockPresent = true; // Merken dass wir ein aktives Remote Lock haben
                return 2; // Lock aktiv und aktuell
            }
            catch (Exception ex)
            {
                Log.Error($"LockManager: Fehler beim Prüfen des Remote Locks: {ex.Message}");
                trayManager.UpdateStatus("Error");
                return 0; // SMB-Fehler
            }
        }

        // Extrahiert den aktuellsten Timestamp aus Lock-Datei (parst alle Heartbeat- und Timestamp-Zeilen)
        private static DateTime ExtractLatestTimestamp(string lockContent)
        {
            try
            {
                DateTime latestTime = DateTime.MinValue;
                var lines = lockContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.StartsWith("Timestamp=") || line.StartsWith("Heartbeat="))
                    {
                        string dateStr = line.Substring(line.IndexOf('=') + 1).Trim();
                        if (DateTime.TryParse(dateStr, out DateTime parsedTime))
                        {
                            if (parsedTime > latestTime)
                                latestTime = parsedTime;
                        }
                    }
                }

                return latestTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        // Extrahiert die Direction aus Lock-Datei (Upload oder Download)
        private static CatalogManager.SyncDirection ExtractDirection(string lockContent)
        {
            try
            {
                var lines = lockContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Direction="))
                    {
                        string dirStr = line.Substring("Direction=".Length).Trim();
                        if (Enum.TryParse<CatalogManager.SyncDirection>(dirStr, out CatalogManager.SyncDirection direction))
                            return direction;
                    }
                }
            }
            catch { }
            return CatalogManager.SyncDirection.None;
        }
        
        // geprpüft!! 2026.07.07
        // Akquiriert atomar lokale und remote Locks
        // Gibt false zurück wenn Locks nicht akquiriert werden können
        // syncDirection: Upload oder Download wird in Lock-Datei gespeichert
        public bool AcquireLocks(AppConfig config, TrayManager trayManager, CatalogManager.SyncDirection syncDirection)
        {
            try
            {
                // ========== VALIDIERUNG: SMB-Config vollständig? ==========
                if (string.IsNullOrEmpty(config.RemoteIP) || string.IsNullOrEmpty(config.SambaUser))
                {
                    Log.Debug("LockManager: SMB-Config unvollständig - überspringe Lock-Akquise");
                    return false;
                }

                // ========== REMOTE LOCK AKQUIRIEREN ==========
                // Stelle SMB-Verbindung her
//                if (!SMBConnectionManager.Instance.EnsureConnected(config))
//                {
//                    Log.Error($"LockManager: SMB-Verbindung fehlgeschlagen, kein Remote-Lock möglich");
//                    return false;
//                }                

                int remoteLockStatus = CheckRemoteLock(config, trayManager);
                
                // Wenn Lockfile erkannt, Fehlerhaft oder veraltet ist, dann Zyklus überspringen und roten Status anzeigen
                if (remoteLockStatus != 1)
                {
                    return false;
                }

                // Erstelle remote Lock-Datei via SMB mit Direction Information
                string lockContentNew = $"SyncGuid={SyncGuid}\nDirection={syncDirection}\nTimestamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                byte[] lockBytes = Encoding.UTF8.GetBytes(lockContentNew);
                
                if (!SMBConnectionManager.Instance.WriteFile(GlobalConst.LOCK_FILE, lockBytes))
                {
                    Log.Error($"LockManager: Schreiben der remote Lock-Datei fehlgeschlagen");
                    _localLockStream?.Close();
                    _localLockStream = null;
                    return false;
                }

                // ========== LOKALER LOCK AKQUIRIEREN ==========
                // Erstelle lokale Lock-Datei mit FileShare.None (exklusiver Zugriff)
                if (File.Exists(config.SyncLocalLockFile))
                {
                    // Prüfe ob Lock veraltet ist (älter als SYNC_LOCK_TIMEOUT_MIN Minuten)
                    FileInfo lockInfo = new FileInfo(config.SyncLocalLockFile);
                    if (lockInfo.LastWriteTime.AddMinutes(GlobalConst.SYNC_LOCK_TIMEOUT_MIN) < DateTime.Now)
                    {
                        Log.Debug($"LockManager: veraltete lokale Lock File erkannt, überschreibe {config.SyncLocalLockFile}");
                        File.Delete(config.SyncLocalLockFile);
                    }
                    else
                    {
                        Log.Debug($"LockManager: Lokaler Lock ist noch aktiv (jünger als {GlobalConst.SYNC_LOCK_TIMEOUT_MIN} min)");
                        return false;
                    }
                }
                
                // Erstelle lokale Lock-Datei mit exklusivem Zugriff
                _localLockStream = new FileStream(config.SyncLocalLockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                
                // Schreibe Sync-GUID in Lock-Datei für Tracking (Direction nur im Remote Lock!)
                // WICHTIG: StreamWriter disposed nicht den underlying Stream!
                var writer = new StreamWriter(_localLockStream);
                writer.WriteLine($"SyncGuid={SyncGuid}");
                writer.WriteLine($"Timestamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.Flush();
                writer.Dispose(); // Nur Writer disposed, NICHT den underlying Stream!
                
                Log.Debug($"LockManager: Beide Locks akquiriert (SyncGuid: {SyncGuid})");
                
                // Starte Heartbeat-Thread
                StartHeartbeat();
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"LockManager: Fehler beim Akquirieren der Locks: {ex.Message}");
                ReleaseLocks(config);
                return false;
            }
        }
        
        // Startet Heartbeat-Thread für regelmäßige Aktualisierung
        public void StartHeartbeat()
        {
            if (_cts != null)
                return; // Bereits gestartet
                
            _cts = new CancellationTokenSource();
            
            _heartbeatThread = new Thread(() =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        UpdateLockTimestamps();
                        Thread.Sleep(GlobalConst.HEARTBEAT_INTERVAL_SEC * 1000);
                    }
                    catch (ThreadInterruptedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"LockManager: Heartbeat-Fehler: {ex.Message}");
                    }
                }
            })
            {
                IsBackground = true
            };
            
            _heartbeatThread.Start();
            Log.Debug($"LockManager: Heartbeat gestartet (Intervall: {GlobalConst.HEARTBEAT_INTERVAL_SEC} sec)");
        }
        
        // Aktualisiert Timestamps in Lock-Dateien (Heartbeat)
        private void UpdateLockTimestamps()
        {
            try
            {
                if (_localLockStream != null && _localLockStream.CanWrite)
                {
                    // Schreibe neuen Timestamp an das Ende der Datei
                    _localLockStream.Seek(0, SeekOrigin.End);
                    using (var writer = new StreamWriter(_localLockStream))
                    {
                        writer.WriteLine($"Heartbeat={DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
                    }
                    _localLockStream.Flush();
                }
                
                // Remote Heartbeat via SMB
                if (_config != null && SMBConnectionManager.Instance.IsConnected)
                {
                    try
                    {
                        byte[]? existingData = SMBConnectionManager.Instance.ReadFile(GlobalConst.LOCK_FILE);
                        
                        if (existingData != null)
                        {
                            string content = Encoding.UTF8.GetString(existingData);
                            content += $"\nHeartbeat={DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                            byte[] updatedData = Encoding.UTF8.GetBytes(content);
                            SMBConnectionManager.Instance.WriteFile(GlobalConst.LOCK_FILE, updatedData);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"LockManager: Remote Heartbeat fehlgeschlagen: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"LockManager: Heartbeat-Update fehlgeschlagen: {ex.Message}");
            }
        }

        // Gibt alle Locks wieder frei
        // MUSS IMMER im finally-Block aufgerufen werden!
        public void ReleaseLocks(AppConfig config)
        {
            try
            {
                // Stoppe Heartbeat
                StopHeartbeat();
                
                // Release lokaler Lock
                if (_localLockStream != null)
                {
                    try
                    {
                        _localLockStream.Close();
                        _localLockStream = null;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"LockManager: Fehler beim Schließen des lokalen Locks: {ex.Message}");
                    }
                }
                
                // Lösche lokale Lock-Datei
                if (!string.IsNullOrEmpty(config.SyncLocalLockFile) && File.Exists(config.SyncLocalLockFile))
                {
                    try
                    {
                        File.Delete(config.SyncLocalLockFile);
                        Log.Debug($"LockManager: Lokale Lock-Datei gelöscht: {config.SyncLocalLockFile}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"LockManager: Fehler beim Löschen der lokalen Lock-Datei: {ex.Message}");
                    }
                }
                
                // Lösche remote Lock-Datei via SMB
                if (!string.IsNullOrEmpty(config.SyncRemoteLockFile))
                {
                    try
                    {
                        // Stelle SMB-Verbindung her falls nicht vorhanden
                        if (SMBConnectionManager.Instance.EnsureConnected(config))
                        {
                            if (SMBConnectionManager.Instance.DeleteFile(GlobalConst.LOCK_FILE))
                            {
                                Log.Debug($"LockManager: Remote Lock-Datei gelöscht via SMB");
                            }
                        }
                        else
                        {
                            Log.Error($"LockManager: Keine SMB-Verbindung, Remote Lock wurde nicht gelöscht");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"LockManager: Fehler beim Löschen der remote Lock-Datei: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"LockManager: Fehler beim Freigeben der Locks: {ex.Message}");
            }
        }
        
        // Stoppt Heartbeat-Thread
        private void StopHeartbeat()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            
            if (_heartbeatThread != null && _heartbeatThread.IsAlive)
            {
                _heartbeatThread.Interrupt();
                _heartbeatThread.Join(TimeSpan.FromSeconds(5));
                _heartbeatThread = null;
            }
            
            Log.Debug("LockManager: Heartbeat gestoppt");
        }
        
        // ==================== DISPOSE ====================
        public void Dispose()
        {
            if (_config != null)
            {
                ReleaseLocks(_config);
                _config = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
