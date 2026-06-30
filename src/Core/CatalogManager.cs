using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;

using LRCatalogSync.Infrastructure;    // ← für Log, AppConfig, GlobalData
using LRCatalogSync.UI;                // ← für TrayManager

namespace LRCatalogSync.Core
{
    /// <summary>
    /// CatalogManager für die Katalog-Synchronisation
    /// Führt sequenziell Backup, rclone sync und Cleanup aus
    /// </summary>
    public static class CatalogManager
    {
        // Enum für Sync-Richtung
        private enum SyncDirection
        {
            None,      // Keine Änderungen
            Upload,    // Lokal → NAS
            Download   // NAS → Lokal
        }
        
        // Speichert Pfad der erstellten Lightroom-Lock für Cleanup
        private static string? _createdLightroomLockPath = null;
        
        // ==================== ÖFFENTLICHE METHODEN ====================
        
        /// <summary>
        /// Führt die Katalog-Synchronisation aus
        /// Phase 0: Prüfe ob Lightroom läuft (.lrcat.lock vorhanden?)
        /// Phase 1: rclone check – Versionsvergleich lokal vs remote + Upload/Download Entscheidung
        /// Phase 2: Lock akquirieren (NUR bei Upload!)
        /// Phase 3: ZIP Backup erstellen (Zielort: Upload=NAS, Download=Lokal)
        /// Phase 4: rclone sync ausführen (Upload oder Download)
        /// Phase 5: Cleanup (IMMER im finally!)
        /// </summary>
        public static void RunCatalogSync(AppConfig config, TrayManager trayManager)
        {
            LockManager? lockManager = null;
            SyncDirection syncDirection = SyncDirection.None;
            
            try
            {
                // ========== PHASE 0: LIGHTROOM-LOCK-ERKENNUNG ==========
                if (IsLightroomRunning(config.CatalogLocalPath, config.CatalogName))
                {
                    Log.Debug("CatalogManager: Lightroom läuft (.lrcat.lock erkannt), warte auf Sync-Ende");
                    trayManager.UpdateStatus("Lockfile");  // 🔵 Blau
                    return; // Warte auf nächsten Zyklus
                }
                
                // ========== PHASE 1: VERSIONSVERGLEICH + RICHTUNGSBESTIMMUNG ==========
                Log.Info("CatalogManager: Starte Versionsvergleich (lokal vs remote)");
                
                // Verwende CatalogFileName Property statt Path.GetFileName
                string remoteFullPath = Path.Combine(config.CatalogRemotePath, config.CatalogFileName);
                string tempLog = Path.Combine(GlobalData.BaseDir, "data", "logs", "rclone_catalog_check.log");
                
                syncDirection = CheckSyncDirection(config, remoteFullPath, tempLog);
                
                if (syncDirection == SyncDirection.None)
                {
                    Log.Info("CatalogManager: Katalog ist bereits synchron (kein Sync nötig)");
                    trayManager.UpdateStatus("Standby");  // 🟢 Grün
                    return;
                }
                
                Log.Info($"CatalogManager: Sync-Richtung erkannt: {syncDirection}");
                
                // ========== PHASE 2: LOCK AKQUIRIEREN (NUR BEI UPLOAD!) ==========
                if (syncDirection == SyncDirection.Upload)
                {
                    Log.Info("CatalogManager: Akquiere Locks für Upload-Synchronisation");
                    lockManager = new LockManager();
                    
                    if (!lockManager.AcquireLocks(config))
                    {
                        Log.Info("CatalogManager: Konnte Locks nicht akquirieren, breche Sync ab");
                        trayManager.UpdateStatus("Standby");  // 🟢 Grün
                        return;
                    }
                    
                    // Erstelle Lightroom-Lock-Datei um Lightroom zu blockieren
                    CreateLightroomLock(config);
                }
                else if (syncDirection == SyncDirection.Download)
                {
                    Log.Info("CatalogManager: Download-Sync benötigt keine Lock-Akquise");
                }
                
                // ========== PHASE 3: ZIP BACKUP ERSTELLEN (Zielort abhängig von Richtung) ==========
                Log.Info($"CatalogManager: Erstelle ZIP-Backup für {syncDirection}");
                CreateZipBackup(config, syncDirection);
                
                // ========== PHASE 4: RCLONE SYNC AUSFÜHREN ==========
                Log.Info($"CatalogManager: Starte rclone {syncDirection.ToString().ToLower()}");
                trayManager.UpdateStatus("Syncing");  // 🟡 Gelb
                
                if (syncDirection == SyncDirection.Upload)
                {
                    RunRcloneSync(config, remoteFullPath, SyncDirection.Upload);
                }
                else if (syncDirection == SyncDirection.Download)
                {
                    RunRcloneSync(config, remoteFullPath, SyncDirection.Download);
                }
                
                // ========== PHASE 5: CLEANUP ==========
                Log.Info("CatalogManager: Cleanup - Locks freigeben");
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Fehler: {ex.Message}");
                trayManager.UpdateStatus("Error");  // 🔴 Rot
            }
            finally
            {
                // IMMER ausführen – selbst bei Exceptions!
                lockManager?.ReleaseLocks();
                
                // Lightroom-Lock-Dateien löschen (nur die von uns erstellten)
                CleanupLightroomLocks(config.CatalogLocalPath);
                
                // Tray-Status zurücksetzen
                trayManager.UpdateStatus("Standby");  // 🟢 Grün
                Log.Info("CatalogManager: Sync abgeschlossen, Status zurückgesetzt");
            }
        }
        
        /// <summary>
        /// Löscht die von uns erstellte Lightroom-Lock-Datei
        /// </summary>
        private static void CleanupLightroomLocks(string catalogPath)
        {
            try
            {
                // Lösche gespeicherte Lock-Datei
                if (!string.IsNullOrEmpty(_createdLightroomLockPath) && File.Exists(_createdLightroomLockPath))
                {
                    File.Delete(_createdLightroomLockPath);
                    Log.Debug($"CatalogManager: Lightroom-Lock gelöscht: {_createdLightroomLockPath}");
                    _createdLightroomLockPath = null;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Fehler beim Löschen der Lightroom-Locks: {ex.Message}");
            }
        }
        
        // ==================== PRIVATE HILFSMETHODEN ====================
        
        // Geprüft!!
        // Prüft ob Lightroom läuft (sucht nach [Katalogname].lrcat.lock, [Katalogname].lrcat-shm, [Katalogname].lrcat-wal)
        private static bool IsLightroomRunning(string catalogPath, string catalogName)
        {
            try
            {
                // Erstelle Array mit möglichen Lock-Dateinamen 
                string[] lockFiles = {
                    $"{catalogName}.lrcat.lock",
                    $"{catalogName}.lrcat-shm",
                    $"{catalogName}.lrcat-wal"
                };
                
                // Suche nach Lightroom-Lock-Dateien mit vollständigem Dateinamen
                foreach (string lockFile in lockFiles)
                {
                    string fullPath = Path.Combine(catalogPath, lockFile);
                    if (File.Exists(fullPath))
                    {
                        Log.Debug($"CatalogManager: Lightroom-Lock erkannt: {fullPath}");
                        return true;
                    }
                }                
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Fehler bei Lightroom-Lock-Prüfung: {ex.Message}");
                return false;
            }
        }
        
        // geprüft!!
        // Prüft die Sync-Richtung basierend auf rclone check Ausgabe
        private static SyncDirection CheckSyncDirection(AppConfig config, string remoteFullPath, string logFile)
        {
            try
            {
                // WICHTIG: --filter filtert nach exaktem Dateinamen
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" check \"{config.CatalogLocalPath}\" \"{remoteFullPath}\" --filter \"+ {config.CatalogFileName}\" --filter \"- *\" --log-file \"{logFile}\" --log-level INFO",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                // Starte rclone check Prozess
                using (var p = Process.Start(psi))
                {   // Prüfe ob Prozess gestartet wurde
                    if (p == null)
                        return SyncDirection.None;
                    
                    p.WaitForExit();
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    
                    // rclone check ExitCodes:
                    // 0 = Keine Unterschiede
                    // 1 = Unterschiede vorhanden
                    // 2+ = Fehler
                    
                    if (p.ExitCode == 0)
                    {
                        Log.Debug("CatalogManager: Keine Unterschiede erkannt (ExitCode 0)");
                        return SyncDirection.None;
                    }
                    else if (p.ExitCode == 1)
                    {
                        // Unterschiede vorhanden - analysiere die Ausgabe
                        // Format: "X files missing on remote" oder "Y files missing on local"
                        bool diffOnRemote = output.Contains("missing on remote") || output.Contains("differ on remote");
                        bool diffOnLocal = output.Contains("missing on local") || output.Contains("differ on local");
                        
                        if (diffOnRemote && !diffOnLocal)
                        {
                            Log.Debug("CatalogManager: Lokale Datei ist neuer/unterschiedlich → UPLOAD");
                            return SyncDirection.Upload;
                        }
                        else if (diffOnLocal && !diffOnRemote)
                        {
                            Log.Debug("CatalogManager: Remote Datei ist neuer/unterschiedlich → DOWNLOAD");
                            return SyncDirection.Download;
                        }
                        else if (diffOnRemote && diffOnLocal)
                        {
                            Log.Notice("CatalogManager: Beide Seiten haben Unterschiede (Konflikt!)");
                            // Bei Konflikt: Lokale Version hat Priorität (Upload)
                            return SyncDirection.Upload;
                        }
                        else
                        {
                            // Fallback: Wenn unklar, lokale Version bevorzugen
                            Log.Debug("CatalogManager: Unklarer Zustand, bevorzuge Upload");
                            return SyncDirection.Upload;
                        }
                    }
                    else
                    {
                        Log.Error($"CatalogManager: rclone check fehlgeschlagen (ExitCode: {p.ExitCode})");
                        return SyncDirection.None;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Fehler bei Sync-Richtungsbestimmung: {ex.Message}");
                return SyncDirection.None;
            }
        }
        
        // geprüft!!
        // Erstellt Lightroom-Lock-Datei um Lightroom zu blockieren
        // Verwendet festen Namen [Katalogname].lrcat.lock
        private static void CreateLightroomLock(AppConfig config)
        {
            try
            {
                string lockPath = Path.Combine(config.CatalogLocalPath, $"{config.CatalogName}.lrcat.lock");
                
                // Speichere Pfad für Cleanup
                _createdLightroomLockPath = lockPath;
                
                // Schreibe Sync-Info in Lock-Datei (Lightroom ignoriert Inhalt, prüft nur Existenz)
                File.WriteAllText(lockPath, $"LRCatSync={DateTime.Now:yyyy-MM-dd HH:mm:ss}\nSyncGuid={Guid.NewGuid():N}");
                
                Log.Debug($"CatalogManager: Lightroom-Lock erstellt: {lockPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Fehler beim Erstellen der Lightroom-Lock: {ex.Message}");
            }
        }
        
        // geprüft!!
        // Erstellt ein gefiltertes ZIP-Backup des Katalogs vor dem Sync
        // Nur kritische Dateien gemäß Konzept-KatalogSync.md
        private static void CreateZipBackup(AppConfig config, SyncDirection direction)
        {
            try
            {
                string backupPath;
                string sourcePath;
                
                // Bestimme Quelle und Ziel basierend auf Sync-Richtung
                if (direction == SyncDirection.Upload)
                {
                    // Upload: Backup auf NAS erstellen (Quelle = Lokal)
                    backupPath = Path.Combine(config.CatalogRemotePath, GlobalConst.BACKUP_FILENAME);
                    sourcePath = config.CatalogLocalPath;
                    Log.Info($"CatalogManager: Erstelle Upload-Backup auf NAS: {backupPath}");
                }
                else if (direction == SyncDirection.Download)
                {
                    // Download: Backup Lokal erstellen (Quelle = NAS)
                    backupPath = Path.Combine(config.CatalogLocalPath, GlobalConst.BACKUP_FILENAME);
                    sourcePath = config.CatalogRemotePath;
                    Log.Info($"CatalogManager: Erstelle Download-Backup Lokal: {backupPath}");
                }
                else
                {
                    return;
                }
                
                // Lösche alte ZIP-Datei falls vorhanden (Ringspeicher mit 1 Slot)
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    Log.Debug($"CatalogManager: Altes Backup gelöscht: {backupPath}");
                }
                
                // Erstelle ZIP-Datei DIREKT am Zielort
                // ZIP wird direkt in backupPath erstellt (Zielort = Backup-Ort)
                using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
                {
                    // Katalog-Hauptdatei (.lrcat)
                    string catalogFile = Path.Combine(sourcePath, $"{config.CatalogName}.lrcat");
                    if (File.Exists(catalogFile))
                        zip.CreateEntryFromFile(catalogFile, $"{config.CatalogName}.lrcat");
                    
                    // Katalog-Datenordner (.lrcat-data)
                    string catalogDataDir = Path.Combine(sourcePath, $"{config.CatalogName}.lrcat-data");
                    if (Directory.Exists(catalogDataDir))
                        AddDirectoryToZip(zip, catalogDataDir, $"{config.CatalogName}.lrcat-data");
                    
                    // Helper.lrdata
                    string helperDir = Path.Combine(sourcePath, $"{config.CatalogName} Helper.lrdata");
                    if (Directory.Exists(helperDir))
                        AddDirectoryToZip(zip, helperDir, $"{config.CatalogName} Helper.lrdata");
                    
                    // Sync.lrdata
                    string syncDir = Path.Combine(sourcePath, $"{config.CatalogName} Sync.lrdata");
                    if (Directory.Exists(syncDir))
                        AddDirectoryToZip(zip, syncDir, $"{config.CatalogName} Sync.lrdata");
                    
                    // Smart Previews.lrdata
                    string smartPreviewsDir = Path.Combine(sourcePath, $"{config.CatalogName} Smart Previews.lrdata");
                    if (Directory.Exists(smartPreviewsDir))
                        AddDirectoryToZip(zip, smartPreviewsDir, $"{config.CatalogName} Smart Previews.lrdata");                    
                }
                
                Log.Info($"CatalogManager: Backup erfolgreich erstellt: {backupPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Backup-Fehler: {ex.Message}");
            }
        }
        
        // geprüft!!
        // Fügt Dateien aus einem Verzeichnis rekursiv in das ZIP-Archiv ein
        private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string entryPath)
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                // Erstelle relativen Pfad innerhalb des Ordners
                string relativePath = Path.GetRelativePath(sourceDir, file);
                string entryName = Path.Combine(entryPath, relativePath).Replace('\\', '/');
                zip.CreateEntryFromFile(file, entryName);
            }
        }
        
        /// <summary>
        /// Führt rclone sync aus (Upload oder Download) mit dynamischen Excludes
        /// </summary>
        private static void RunRcloneSync(AppConfig config, string remoteFullPath, SyncDirection direction)
        {
            try
            {
                string sourcePath, destPath;
                DateTime syncStartTime = DateTime.Now;
                int transferredFiles = 0;
                long transferredBytes = 0;
                
                // Bestimme Quelle und Ziel basierend auf Sync-Richtung
                if (direction == SyncDirection.Upload)
                {
                    sourcePath = config.CatalogLocalPath;
                    destPath = remoteFullPath;
                    Log.Info("CatalogManager: Starte rclone upload (lokal → NAS)");
                }
                else if (direction == SyncDirection.Download)
                {
                    sourcePath = remoteFullPath;
                    destPath = config.CatalogLocalPath;
                    Log.Info("CatalogManager: Starte rclone download (NAS → lokal)");
                }
                else
                {
                    return;
                }
                
                // Baue Exclude-Filter dynamisch
                var excludes = new System.Collections.Generic.List<string>
                {
                    "--exclude \"*.lrcat.lock\"",
                    "--exclude \"*.lrcat-shm\"",
                    "--exclude \"*.lrcat-wal\"",
                    "--exclude \"LRCatSync_*.zip\"",
                };
                
                // BackupsLocalPath ausschließen wenn im Katalog-Pfad
                if (config.IsBackupInsideCatalogPath())
                {
                    string relativeBackupPath = config.GetRelativeBackupExcludePattern();
                    if (!string.IsNullOrEmpty(relativeBackupPath))
                    {
                        excludes.Add($"--exclude \"{relativeBackupPath}/**\"");
                        Log.Debug($"CatalogManager: Schließe Backup-Ordner aus: {relativeBackupPath}");
                    }
                }
                
                // Previews ausschließen wenn nicht aktiviert
                if (!config.SyncPreviewData)
                {
                    excludes.Add("--exclude \"*Previews.lrdata/\"");
                }
                
                string excludeArgs = string.Join(" ", excludes);
                
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" sync \"{sourcePath}\" \"{destPath}\" --delete-befores {excludeArgs} --log-level {config.LogLevel}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return;
                    
                    // Lese Output für Statistiken
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    
                    p.WaitForExit();
                    
                    if (p.ExitCode == 0)
                    {
                        // Parse Transfer-Statistiken aus rclone Output
                        // rclone Format: "* Transferred: 1.234 GiB / 1.234 GiB, 100%, 1234 files"
                        var stats = ParseRcloneStats(output + error);
                        transferredFiles = stats.Files;
                        transferredBytes = stats.Bytes;
                        
                        TimeSpan duration = DateTime.Now - syncStartTime;
                        
                        Log.Info($"CatalogManager: rclone {direction.ToString().ToLower()} erfolgreich");
                        Log.Info($"CatalogManager: Transfer-Statistiken:");
                        Log.Info($"  - Richtung: {direction}");
                        Log.Info($"  - Dateien: {transferredFiles}");
                        Log.Info($"  - Bytes: {transferredBytes:N0}");
                        Log.Info($"  - Dauer: {duration:hh\\:mm\\:ss}");
                    }
                    else
                    {
                        Log.Error($"CatalogManager: rclone {direction.ToString().ToLower()} fehlgeschlagen (ExitCode: {p.ExitCode})");
                    }
                }
                
                // Separater Sync für Previews.lrdata (nur wenn SyncPreviewData=true)
                if (config.SyncPreviewData)
                {
                    SyncPreviewsData(config, sourcePath, destPath, direction);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: rclone sync Fehler: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Parst Transfer-Statistiken aus rclone Output
        /// </summary>
        private static (int Files, long Bytes) ParseRcloneStats(string output)
        {
            int files = 0;
            long bytes = 0;
            
            try
            {
                // Suche nach "Transferred:" Zeile
                // Format: "* Transferred: 1.234 GiB / 1.234 GiB, 100%, 1234 files"
                string[] lines = output.Split('\n');
                
                foreach (string line in lines)
                {
                    if (line.Contains("Transferred:"))
                    {
                        // Extrahiere Bytes (erste Zahl mit Einheit)
                        var bytesMatch = System.Text.RegularExpressions.Regex.Match(line, @"Transferred:\s*([\d.]+)\s*(KiB|MiB|GiB|TiB|B)");
                        if (bytesMatch.Success)
                        {
                            double value = double.Parse(bytesMatch.Groups[1].Value);
                            string unit = bytesMatch.Groups[2].Value;
                            
                            bytes = unit switch
                            {
                                "KiB" => (long)(value * 1024),
                                "MiB" => (long)(value * 1024 * 1024),
                                "GiB" => (long)(value * 1024 * 1024 * 1024),
                                "TiB" => (long)(value * 1024 * 1024 * 1024 * 1024),
                                _ => (long)value
                            };
                        }
                        
                        // Extrahiere Datei-Anzahl
                        var filesMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s*files");
                        if (filesMatch.Success)
                        {
                            files = int.Parse(filesMatch.Groups[1].Value);
                        }
                        
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"CatalogManager: Fehler beim Parsen der Statistiken: {ex.Message}");
            }
            
            return (files, bytes);
        }
        
        /// <summary>
        /// Führt separaten Sync für Previews.lrdata durch (nur wenn SyncPreviewData=true)
        /// </summary>
        private static void SyncPreviewsData(AppConfig config, string sourcePath, string destPath, SyncDirection direction)
        {
            try
            {
                Log.Info($"CatalogManager: Starte separaten Sync für Previews.lrdata ({direction})");
                
                // Baue Exclude-Filter für Previews-Sync (alle anderen kritischen Dateien ausschließen)
                var excludes = new System.Collections.Generic.List<string>
                {
                    "--exclude \"*.lrcat.lock\"",
                    "--exclude \"*.lrcat-shm\"",
                    "--exclude \"*.lrcat-wal\"",
                    "--exclude \"*.lrcat\"",
                    "--exclude \"*.lrcat-data/\"",
                    "--exclude \"*Helper.lrdata/\"",
                    "--exclude \"*Sync.lrdata/\"",
                    "--exclude \"*Smart Previews.lrdata/\"",
                    "--exclude \"LRCatSync_*.zip\"",
                };
                
                // BackupsLocalPath ausschließen wenn im Katalog-Pfad
                if (config.IsBackupInsideCatalogPath())
                {
                    string relativeBackupPath = config.GetRelativeBackupExcludePattern();
                    if (!string.IsNullOrEmpty(relativeBackupPath))
                    {
                        excludes.Add($"--exclude \"{relativeBackupPath}/**\"");
                    }
                }
                
                string excludeArgs = string.Join(" ", excludes);
                
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" sync \"{sourcePath}\" \"{destPath}\" --include \"*/\" --include \"*Previews.lrdata/**\" {excludeArgs} --log-level {config.LogLevel}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return;
                    
                    p.WaitForExit();
                    
                    if (p.ExitCode == 0)
                    {
                        Log.Info($"CatalogManager: Previews.lrdata Sync erfolgreich");
                    }
                    else
                    {
                        Log.Error($"CatalogManager: Previews.lrdata Sync fehlgeschlagen (ExitCode: {p.ExitCode})");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Previews.lrdata Sync Fehler: {ex.Message}");
            }
        }
    }
}
