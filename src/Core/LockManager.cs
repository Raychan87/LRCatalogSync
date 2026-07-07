using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;

using LRCatalogSync.Infrastructure;    // ← für Log, AppConfig, SMBConnectionManager

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
        
        // Lock-Status
        private bool _locksAcquired = false;
        
        // AppConfig für Lock-Pfade
        private AppConfig? _config;

        // ==================== KONSTRUKTOR ====================
        public LockManager(AppConfig config)
        {
            _config = config;
        }

        // ==================== ÖFFENTLICHE METHODEN ====================        
        // geprpüft!! 2026.07.07
        // Akquiriert atomar lokale und remote Locks
        // Gibt false zurück wenn Locks nicht akquiriert werden können
        public bool AcquireLocks(AppConfig config)
        {
            try
            {
                // ========== REMOTE LOCK AKQUIRIEREN ==========
                // Stelle SMB-Verbindung her
                if (!SMBConnectionManager.Instance.EnsureConnected(config))
                {
                    Log.Error($"LockManager: SMB-Verbindung fehlgeschlagen, kein Remote-Lock möglich");
                    _locksAcquired = false;
                    return false;
                }
                
                // Lädt alle Filenamen von den Remote Pfad in die existingFiles.
                var existingFiles = SMBConnectionManager.Instance.ListFiles(Path.GetDirectoryName(config.CatalogRemotePath) ?? "");

                // Prüfe ob das Lockfile vorhanden ist
                if (existingFiles.Contains(GlobalConst.LOCK_FILE))
                {
                    // Lies Lock-Inhalt um Alter zu prüfen
                    byte[]? lockData = SMBConnectionManager.Instance.ReadFile(GlobalConst.LOCK_FILE);
                    if (lockData != null)
                    {
                        string lockContent = Encoding.UTF8.GetString(lockData);
                        
                        // Parse Timestamp aus Lock-Datei
                        DateTime lockTimestamp;
                        if (TryParseLockTimestamp(lockContent, out lockTimestamp))
                        {
                            if (lockTimestamp.AddMinutes(GlobalConst.SYNC_LOCK_TIMEOUT_MIN) < DateTime.UtcNow)
                            {
                                Log.Debug($"LockManager: Alte remote Lock erkannt, überschreibe {config.SyncRemoteLockFile}");
                                SMBConnectionManager.Instance.DeleteFile(GlobalConst.LOCK_FILE);
                            }
                            else
                            {
                                Log.Debug($"LockManager: Remote Lock ist noch aktiv (jünger als {GlobalConst.SYNC_LOCK_TIMEOUT_MIN} min)");
                                // Release lokalen Lock
                                _localLockStream?.Close();
                                _localLockStream = null;
                                return false;
                            }
                        }
                    }
                }
                
                // Erstelle remote Lock-Datei via SMB
                string lockContentNew = $"SyncGuid={SyncGuid}\nTimestamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}";
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
                
                // Schreibe Sync-GUID in Lock-Datei für Tracking
                // WICHTIG: StreamWriter disposed nicht den underlying Stream!
                var writer = new StreamWriter(_localLockStream);
                writer.WriteLine($"SyncGuid={SyncGuid}");
                writer.WriteLine($"Timestamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.Flush();
                writer.Dispose(); // Nur Writer disposed, NICHT den underlying Stream!
                
                _locksAcquired = true;
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
        
        /// <summary>
        /// Startet Heartbeat-Thread für regelmäßige Aktualisierung
        /// </summary>
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
        
        /// <summary>
        /// Aktualisiert Timestamps in Lock-Dateien (Heartbeat)
        /// </summary>
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
                        string lockRelativePath = GetRelativePath(_config.CatalogRemotePath, _config.SyncRemoteLockFile);
                        byte[]? existingData = SMBConnectionManager.Instance.ReadFile(lockRelativePath);
                        
                        if (existingData != null)
                        {
                            string content = Encoding.UTF8.GetString(existingData);
                            content += $"\nHeartbeat={DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                            byte[] updatedData = Encoding.UTF8.GetBytes(content);
                            SMBConnectionManager.Instance.WriteFile(lockRelativePath, updatedData);
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
        
        /// <summary>
        /// Gibt alle Locks wieder frei
        /// MUSS IMMER im finally-Block aufgerufen werden!
        /// </summary>
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
                            string lockRelativePath = GetRelativePath(config.CatalogRemotePath, config.SyncRemoteLockFile);
                            if (SMBConnectionManager.Instance.DeleteFile(lockRelativePath))
                            {
                                Log.Debug($"LockManager: Remote Lock-Datei gelöscht via SMB");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"LockManager: Fehler beim Löschen der remote Lock-Datei: {ex.Message}");
                    }
                }
                
                _locksAcquired = false;
                Log.Info($"LockManager: Alle Locks freigegeben (SyncGuid: {SyncGuid})");
            }
            catch (Exception ex)
            {
                Log.Error($"LockManager: Fehler beim Freigeben der Locks: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Stoppt Heartbeat-Thread
        /// </summary>
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
        
        /// <summary>
        /// Prüft ob Locks akquiriert wurden
        /// </summary>
        public bool AreLocksAcquired => _locksAcquired;
        
        // ==================== HILFSMETHODEN ====================
        
        /// <summary>
        /// Berechnet den relativen Pfad innerhalb der SMB-Freigabe
        /// </summary>
        private string GetRelativePath(string basePath, string fullPath)
        {
            // Entferne führenden UNC-Prefix \\Server\Share von basePath
            string baseNormalized = basePath.TrimEnd('\\', '/');
            if (baseNormalized.StartsWith(@"\\"))
            {
                // Finde Position nach \\Server\Share
                int startIndex = baseNormalized.IndexOf('\\', 2);
                if (startIndex > 0)
                {
                    baseNormalized = baseNormalized.Substring(startIndex + 1);
                }
            }
            
            // Vergleiche und extrahiere relativen Teil
            string fullNormalized = fullPath.Replace('/', '\\');
            if (fullNormalized.StartsWith(baseNormalized, StringComparison.OrdinalIgnoreCase))
            {
                string relative = fullNormalized.Substring(baseNormalized.Length);
                return relative.TrimStart('\\');
            }
            
            // Fallback: Annahme dass es bereits relativ ist
            return fullPath;
        }
        
        /// <summary>
        /// Parst den Timestamp aus einer Lock-Datei
        /// </summary>
        private bool TryParseLockTimestamp(string content, out DateTime timestamp)
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
