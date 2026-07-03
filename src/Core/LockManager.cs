using System;
using System.IO;
using System.Threading;
using System.Diagnostics;

using LRCatalogSync.Infrastructure;    // ← für Log, AppConfig

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
        
        private FileStream? _remoteLockStream;
        
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
        
        /// <summary>
        /// Akquiriert atomar lokale und remote Locks
        /// Gibt false zurück wenn Locks nicht akquiriert werden können
        /// </summary>
        public bool AcquireLocks(AppConfig config)
        {
            try
            {
                // ========== LOKALER LOCK AKQUIRIEREN ==========
                // Erstelle lokale Lock-Datei mit FileShare.None (exklusiver Zugriff)
                if (File.Exists(config.SyncLocalLockFile))
                {
                    // Prüfe ob Lock veraltet ist (älter als SYNC_LOCK_TIMEOUT_MIN Minuten)
                    FileInfo lockInfo = new FileInfo(config.SyncLocalLockFile);
                    if (lockInfo.LastWriteTime.AddMinutes(GlobalConst.SYNC_LOCK_TIMEOUT_MIN) < DateTime.Now)
                    {
                        Log.Info($"LockManager: veraltete lokale Lock File erkannt, überschreibe {config.SyncLocalLockFile}");
                        File.Delete(config.SyncLocalLockFile);
                    }
                    else
                    {
                        Log.Info($"LockManager: Lokaler Lock ist noch aktiv (jünger als {GlobalConst.SYNC_LOCK_TIMEOUT_MIN} min)");
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
                
                // ========== REMOTE LOCK AKQUIRIEREN ==========
                // Prüfe ob Remote-Pfad existiert (Samba-Verbindung)
                if (!Directory.Exists(Path.GetDirectoryName(config.SyncRemoteLockFile)))
                {
                    Log.Info($"LockManager: Remote-Pfad existiert nicht: {Path.GetDirectoryName(config.SyncRemoteLockFile)}");
                    // Kein Fehler, aber kein Remote-Lock möglich
                    _locksAcquired = true;
                    return true;
                }
                
                if (File.Exists(config.SyncRemoteLockFile))
                {
                    // Prüfe ob Lock stale ist
                    FileInfo lockInfo = new FileInfo(config.SyncRemoteLockFile);
                    if (lockInfo.LastWriteTimeUtc.AddMinutes(GlobalConst.SYNC_LOCK_TIMEOUT_MIN) < DateTime.UtcNow)
                    {
                        Log.Info($"LockManager: Stale remote Lock erkannt, überschreibe {config.SyncRemoteLockFile}");
                        File.Delete(config.SyncRemoteLockFile);
                    }
                    else
                    {
                        Log.Info($"LockManager: Remote Lock ist noch aktiv (jünger als {GlobalConst.SYNC_LOCK_TIMEOUT_MIN} min)");
                        // Release lokalen Lock
                        _localLockStream?.Close();
                        _localLockStream = null;
                        File.Delete(config.SyncLocalLockFile);
                        return false;
                    }
                }
                
                // Erstelle remote Lock-Datei
                _remoteLockStream = new FileStream(config.SyncRemoteLockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                
                // Schreibe Sync-GUID in Lock-Datei
                // WICHTIG: StreamWriter disposed nicht den underlying Stream!
                var remoteWriter = new StreamWriter(_remoteLockStream);
                remoteWriter.WriteLine($"SyncGuid={SyncGuid}");
                remoteWriter.WriteLine($"Timestamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                remoteWriter.Flush();
                remoteWriter.Dispose(); // Nur Writer disposed, NICHT den underlying Stream!
                
                _locksAcquired = true;
                Log.Info($"LockManager: Beide Locks akquiriert (SyncGuid: {SyncGuid})");
                
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
                
                if (_remoteLockStream != null && _remoteLockStream.CanWrite)
                {
                    _remoteLockStream.Seek(0, SeekOrigin.End);
                    using (var writer = new StreamWriter(_remoteLockStream))
                    {
                        writer.WriteLine($"Heartbeat={DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
                    }
                    _remoteLockStream.Flush();
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
                
                // Release remote Lock
                if (_remoteLockStream != null)
                {
                    try
                    {
                        _remoteLockStream.Close();
                        _remoteLockStream = null;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"LockManager: Fehler beim Schließen des remote Locks: {ex.Message}");
                    }
                }
                
                // Lösche remote Lock-Datei
                if (!string.IsNullOrEmpty(config.SyncRemoteLockFile) && File.Exists(config.SyncRemoteLockFile))
                {
                    try
                    {
                        File.Delete(config.SyncRemoteLockFile);
                        Log.Debug($"LockManager: Remote Lock-Datei gelöscht: {config.SyncRemoteLockFile}");
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
