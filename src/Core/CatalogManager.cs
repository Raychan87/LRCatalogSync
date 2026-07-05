using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;

using LRCatalogSync.Infrastructure;    // ← für Log, AppConfig, GlobalData
using LRCatalogSync.UI;                // ← für TrayManager

namespace LRCatalogSync.Core
{
    // CatalogManager für die Katalog-Synchronisation
    // Führt sequenziell Backup, rclone sync und Cleanup aus
    public static class CatalogManager
    {
        // Enum für Sync-Richtung
        private enum SyncDirection
        {
            None,      // Keine Änderungen
            Upload,    // Lokal → NAS
            Download   // NAS → Lokal
        }
        
        // geprüft!!
        // Führt die Katalog-Synchronisation aus
        // Phase 0: Prüfe ob Lightroom läuft (.lrcat.lock vorhanden?) – VOR try!
        // Phase 1: rclone check – Versionsvergleich lokal vs remote + Upload/Download Entscheidung
        // Phase 2: Lock akquirieren (NUR bei Upload!)
        // Phase 3: ZIP Backup erstellen (Zielort: Upload=NAS, Download=Lokal)
        // Phase 4: rclone sync ausführen (Upload oder Download)
        // Phase 5: Cleanup (IMMER im finally!)
        public static void RunCatalogSync(AppConfig config, TrayManager trayManager)
        {
            // ========== PHASE 0: LIGHTROOM-LOCK-ERKENNUNG (vor try!) ==========
            if (IsLightroomRunning(config))
            {
                Log.Debug("CatalogManager: Lightroom läuft (.lrcat.lock erkannt), warte auf Sync-Ende");
                trayManager.UpdateStatus("Lockfile");  // 🔵 Blau
                return; // Kein try/finally nötig – wir haben nichts erstellt
            }

            LockManager? lockManager = null;
            SyncDirection syncDirection = SyncDirection.None;

            try
            {
                // ========== PHASE 1: VERSIONSVERGLEICH + RICHTUNGSBESTIMMUNG ==========
                Log.Debug("CatalogManager: Starte Versionsvergleich (lokal vs remote)");
                
                string tempLog = Path.Combine(GlobalData.BaseDir, "data", "logs", "rclone_catalog_check.log");
                
                syncDirection = CheckSyncDirection(config);
                
                if (syncDirection == SyncDirection.None)
                {
                    Log.Debug("CatalogManager: Katalog ist bereits synchron (kein Sync nötig)");
                    trayManager.UpdateStatus("Standby");  // 🟢 Grün
                    return;
                }
                
                Log.Debug($"CatalogManager: Sync-Richtung erkannt: {syncDirection}");
                
                // ========== PHASE 2: LOCK AKQUIRIEREN (NUR BEI UPLOAD!) ==========
                if (syncDirection == SyncDirection.Upload)
                {
                    Log.Debug("CatalogManager: Akquiere Locks für Upload-Synchronisation");
                    lockManager = new LockManager(config);
                    
                    if (!lockManager.AcquireLocks(config))
                    {
                        Log.Debug("CatalogManager: Konnte Locks nicht akquirieren, breche Sync ab");
                        trayManager.UpdateStatus("Standby");  // 🟢 Grün
                        return;
                    }
                }
                else if (syncDirection == SyncDirection.Download)
                {
                    Log.Debug("CatalogManager: Download-Sync benötigt keine Lock-Akquise");
                }

                // Erstelle Lightroom-Lock-Datei um Lightroom zu blockieren
                CreateLightroomLock(config);
                
                // ========== PHASE 3: ZIP BACKUP ERSTELLEN (Zielort abhängig von Richtung) ==========
                Log.Debug($"CatalogManager: Erstelle ZIP-Backup für {syncDirection}");
                CreateZipBackup(config, syncDirection);
                
                // ========== PHASE 4: RCLONE SYNC AUSFÜHREN ==========
                Log.Info($"CatalogManager: Starte rclone {syncDirection.ToString().ToLower()}");
                trayManager.UpdateStatus("Syncing");  // 🟡 Gelb
                
                if (syncDirection == SyncDirection.Upload)
                {
                    RunRcloneSync(config, SyncDirection.Upload);
                }
                else if (syncDirection == SyncDirection.Download)
                {
                    RunRcloneSync(config, SyncDirection.Download);
                }
                
                // ========== PHASE 5: CLEANUP ==========
                Log.Debug("CatalogManager: Cleanup - Locks freigeben");
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Fehler: {ex.Message}");
                trayManager.UpdateStatus("Error");  // 🔴 Rot
            }
            finally
            {
                // IMMER ausführen – selbst bei Exceptions!
                lockManager?.Dispose();
                
                // Lightroom-Lock-Dateien löschen (nur die von uns erstellten)
                CleanupLightroomLocks(config);
                
                // Tray-Status zurücksetzen
                trayManager.UpdateStatus("Standby");  // 🟢 Grün
                Log.Debug("CatalogManager: Sync abgeschlossen");
            }
        }
        
        // geprüft!!
        // Löscht die von uns erstellte Lightroom-Lock-Datei
        // Erkennung NUR am Inhalt: Unsere enthält "LRCatSync=", Lightrooms enthält Prozesspfad
        private static void CleanupLightroomLocks(AppConfig config)
        {
            try
            {
                if (File.Exists(config.CatalogLockFile))
                {
                    string content = File.ReadAllText(config.CatalogLockFile);
                    if (content.StartsWith("LRCatSync="))
                    {
                        File.Delete(config.CatalogLockFile);
                        Log.Debug($"CatalogManager: LRCatSync Lock-Datei gelöscht: {config.CatalogLockFile}");
                    }
                    else
                    {
                        Log.Debug($"CatalogManager: Lightrooms Lock-Datei, NICHT löschen: {config.CatalogLockFile}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Fehler beim Löschen der Lightroom-Locks: {ex.Message}");
            }
        }
        
        // Geprüft!!
        // Prüft ob Lightroom läuft (sucht nach [Katalogname].lrcat.lock, [Katalogname].lrcat-shm, [Katalogname].lrcat-wal)
        private static bool IsLightroomRunning(AppConfig config)
        {
            try
            {
                // Erstelle Array mit möglichen Lock-Dateinamen 
                string[] lockFiles = {
                    $"{config.CatalogName}.lrcat.lock",
                    $"{config.CatalogName}.lrcat-shm",
                    $"{config.CatalogName}.lrcat-wal"
                };
                
                // Suche nach Lightroom-Lock-Dateien mit vollständigem Dateinamen
                foreach (string lockFile in lockFiles)
                {
                    string fullPath = Path.Combine(config.CatalogLocalPath, lockFile);
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
        
        // geprüft!! 2026.07.05
        // Prüft die Sync-Richtung basierend auf Änderungsdatum (via rclone lsl)
        // Vergleicht die letzte Änderungszeit von lokalem und remote Katalog
        private static SyncDirection CheckSyncDirection(AppConfig config)
        {
            try
            {
                // Hole Änderungsdatum der lokalen Datei
                DateTime? localModTime = GetFileModificationTime(config.CatalogLocalFile);
                if (localModTime == null)
                {
                    Log.Error($"CatalogManager: Lokale Katalog-Datei nicht gefunden: {config.CatalogLocalFile}");
                    return SyncDirection.None;
                }
                
                // Hole Änderungsdatum der remote Datei via rclone lsl
                DateTime? remoteModTime = GetRemoteFileModificationTime(config);
                if (remoteModTime == null)
                {
                    // Remote-Datei existiert nicht -> Upload
                    Log.Debug($"CatalogManager: Remote-Katalog nicht vorhanden → UPLOAD");
                    return SyncDirection.Upload;
                }
                
                // Vergleiche Änderungsdatumen
                TimeSpan difference = localModTime.Value - remoteModTime.Value;
                
                if (Math.Abs(difference.TotalSeconds) < 2)
                {
                    // Weniger als 2 Sekunden Differenz -> als gleich betrachten
                    Log.Debug("CatalogManager: Kataloge sind zeitlich synchron (Delta < 2s)");
                    return SyncDirection.None;
                }
                else if (difference.TotalSeconds > 0)
                {
                    Log.Debug($"CatalogManager: Lokaler Katalog ist neuer ({difference.TotalMinutes:F1} Min) → UPLOAD");
                    return SyncDirection.Upload;
                }
                else
                {
                    Log.Debug($"CatalogManager: Remote-Katalog ist neuer ({Math.Abs(difference.TotalMinutes):F1} Min) → DOWNLOAD");
                    return SyncDirection.Download;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Fehler bei Sync-Richtungsbestimmung: {ex.Message}");
                return SyncDirection.None;
            }
        }
        
        // geprüft!! 2026.07.05
        // Holt das Änderungsdatum einer lokalen Datei (OS-Zeit)
        private static DateTime? GetFileModificationTime(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;
                
                return File.GetLastWriteTime(filePath); // Alternativ für UTC: File.GetLastWriteTimeUtc
            }
            catch
            {
                return null;
            }
        }
        
        // geprüft!! 2026.07.05
        // Holt das Änderungsdatum einer remote Datei via rclone lsl
        private static DateTime? GetRemoteFileModificationTime(AppConfig config)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" lsl \"{GlobalConst.REMOTE_NAME}:{config.CatalogRemoteFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return null;
                    
                    p.WaitForExit();
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    
                    // Format von rclone lsl:
                    // "5042262016 2026-05-29 14:06:35.534552200 Lightroom-Katalog-v15.lrcat"
                    
                    if (p.ExitCode != 0 || string.IsNullOrEmpty(output))
                        return null;
                    
                    // Splitte Output in Teile (Größe Datum Zeit Dateiname)
                    string[] parts = output.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        // Konstruiere Datum/Zeit String und parse ihn
                        string dateStr = parts[1] + " " + parts[2];
                        
                        // Versuche Datumsformat zu parsen
                        string[] formats = new[]
                        {
                            "yyyy-MM-dd HH:mm:ss.fffffff",
                            "yyyy-MM-dd HH:mm:ss.ffffff",
                            "yyyy-MM-dd HH:mm:ss.fffff",
                            "yyyy-MM-dd HH:mm:ss.ffff",
                            "yyyy-MM-dd HH:mm:ss.fff",
                            "yyyy-MM-dd HH:mm:ss"
                        };
                        
                        foreach (string fmt in formats)
                        {
                            if (DateTime.TryParseExact(dateStr, fmt, 
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AssumeUniversal,
                                out DateTime result))
                            {
                                return result.ToUniversalTime();
                            }
                        }
                        
                        // Fallback: Standard Parse
                        if (DateTime.TryParse(dateStr, out DateTime fallbackResult))
                            return fallbackResult.ToUniversalTime();
                    }
                    
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }
        
        // geprüft!!
        // Erstellt Lightroom-Lock-Datei um Lightroom zu blockieren
        // Verwendet festen Namen [Katalogname].lrcat.lock
        private static void CreateLightroomLock(AppConfig config)
        {
            try
            {
                // Schreibe Sync-Info in Lock-Datei (Lightroom ignoriert Inhalt, prüft nur Existenz)
                File.WriteAllText(config.CatalogLockFile, $"LRCatSync={DateTime.Now:yyyy-MM-dd HH:mm:ss}\nSyncGuid={Guid.NewGuid():N}");
                
                Log.Debug($"CatalogManager: Lightroom-Lock erstellt: {config.CatalogLockFile}");
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Fehler beim Erstellen der Lightroom-Lock: {ex.Message}");
            }
        }
        
        // geprüft!! 2026.07.05
        // Erstellt ein gefiltertes ZIP-Backup des Katalogs vor dem Sync
        // WICHTIG: Das Backup sichert den AKTUELLEN Zustand des Ziels VOR dem Überschreiben!
        // - Upload (Lokal→NAS): Sichert was auf dem NAS ist -> ZIP auf NAS
        // - Download (NAS→Lokal): Sichert was lokal ist -> ZIP lokal
        private static void CreateZipBackup(AppConfig config, SyncDirection direction)
        {
            try
            {
                string ZipFilePath;
                
                // Upload: Backup auf NAS, Download: Backup lokal
                if (direction == SyncDirection.Upload)
                {
                    // Upload: Was auf dem NAS IST wird gesichert -> ZIP auf NAS
                    // Quelle = Remote (was existiert und überschrieben wird)
                    // Ziel = Remote (ZIP-Backup am selben Ort)

                    // ZipFilePath = "/SambaOrdner/LRCatSync_last_katalog.zip"
                    ZipFilePath = Path.Combine(config.CatalogRemotePath, GlobalConst.BACKUP_FILENAME);
                    Log.Info($"CatalogManager: Erstelle Upload-Backup auf NAS: {ZipFilePath}");
                    
                    // SMB-Verbindung herstellen
                    if (!SMBConnectionManager.Instance.EnsureConnected(config))
                    {
                        Log.Error("CatalogManager: SMB-Verbindung fehlgeschlagen");
                        return;
                    }
                    
                    // Alte ZIP-Datei auf NAS löschen falls vorhanden
                    // Relativer Pfad: BackupFilename direkt im CatalogRemotePath (ohne führendes /)
                    SMBConnectionManager.Instance.DeleteFile(GlobalConst.BACKUP_FILENAME);
                    
                    using (var memStream = new MemoryStream())
                    {
                        using (var zip = new ZipArchive(memStream, ZipArchiveMode.Create, leaveOpen: true))
                        {
                            // Remote-Katalog-Hauptdatei (.lrcat) per SMB lesen
                            AddRemoteFileToZip(zip, $"{config.CatalogName}.lrcat", $"{config.CatalogName}.lrcat");
                            
                            // Remote-Katalog-Datenordner (.lrcat-data) per SMB rekursiv lesen
                            AddRemoteDirectoryToZip(zip, $"{config.CatalogName}.lrcat-data", $"{config.CatalogName}.lrcat-data");
                            
                            // Remote-Helper.lrdata per SMB rekursiv lesen
                            AddRemoteDirectoryToZip(zip, $"{config.CatalogName} Helper.lrdata", $"{config.CatalogName} Helper.lrdata");
                            
                            // Remote-Sync.lrdata per SMB rekursiv lesen
                            AddRemoteDirectoryToZip(zip, $"{config.CatalogName} Sync.lrdata", $"{config.CatalogName} Sync.lrdata");
                            
                            // Remote-Smart Previews.lrdata per SMB rekursiv lesen
                            AddRemoteDirectoryToZip(zip, $"{config.CatalogName} Smart Previews.lrdata", $"{config.CatalogName} Smart Previews.lrdata");
                        }
                        
                        // Gesamtes ZIP als ByteArray remote schreiben
                        // Relativer Pfad: BackupFilename direkt im CatalogRemotePath (ohne führendes /)
                        if (!SMBConnectionManager.Instance.WriteFile(GlobalConst.BACKUP_FILENAME, memStream.ToArray()))
                        {
                            Log.Error($"CatalogManager: Schreiben des ZIP-Backups auf NAS fehlgeschlagen");
                            return;
                        }
                    }
                    
                    Log.Info($"CatalogManager: Upload-Backup erfolgreich auf NAS erstellt: {ZipFilePath}");
                }
                else if (direction == SyncDirection.Download)
                {
                    // Download: Was lokal IST wird gesichert -> ZIP lokal
                    // Quelle = Lokal (was existiert und überschrieben wird)
                    // Ziel = Lokal (ZIP-Backup am selben Ort)
                    // Beispiel: "C:/Benutzer/[Benutzername]/Bilder/Lightroom/LRCatSync_last_katalog.zip"
                    ZipFilePath = Path.Combine(config.CatalogLocalPath, GlobalConst.BACKUP_FILENAME);
                    Log.Info($"CatalogManager: Erstelle Download-Backup Lokal: {ZipFilePath}");
                    
                    // Lösche alte ZIP-Datei falls vorhanden (Ringspeicher mit 1 Slot)
                    if (File.Exists(ZipFilePath))
                    {
                        File.Delete(ZipFilePath);
                        Log.Debug($"CatalogManager: Altes Backup gelöscht: {ZipFilePath}");
                    }
                    
                    // Erstelle ZIP-Datei lokal
                    using (var zip = ZipFile.Open(ZipFilePath, ZipArchiveMode.Create))
                    {
                        // Katalog-Hauptdatei (.lrcat)
                        string catalogFile = Path.Combine(config.CatalogLocalPath, $"{config.CatalogName}.lrcat");
                        if (File.Exists(catalogFile))
                            zip.CreateEntryFromFile(catalogFile, $"{config.CatalogName}.lrcat");
                        
                        // Katalog-Datenordner (.lrcat-data)
                        string catalogDataDir = Path.Combine(config.CatalogLocalPath, $"{config.CatalogName}.lrcat-data");
                        if (Directory.Exists(catalogDataDir))
                            AddDirectoryToZip(zip, catalogDataDir, $"{config.CatalogName}.lrcat-data");
                        
                        // Helper.lrdata
                        string helperDir = Path.Combine(config.CatalogLocalPath, $"{config.CatalogName} Helper.lrdata");
                        if (Directory.Exists(helperDir))
                            AddDirectoryToZip(zip, helperDir, $"{config.CatalogName} Helper.lrdata");
                        
                        // Sync.lrdata
                        string syncDir = Path.Combine(config.CatalogLocalPath, $"{config.CatalogName} Sync.lrdata");
                        if (Directory.Exists(syncDir))
                            AddDirectoryToZip(zip, syncDir, $"{config.CatalogName} Sync.lrdata");
                        
                        // Smart Previews.lrdata
                        string smartPreviewsDir = Path.Combine(config.CatalogLocalPath, $"{config.CatalogName} Smart Previews.lrdata");
                        if (Directory.Exists(smartPreviewsDir))
                            AddDirectoryToZip(zip, smartPreviewsDir, $"{config.CatalogName} Smart Previews.lrdata");                    
                    }
                    
                    Log.Info($"CatalogManager: Download-Backup erfolgreich lokal erstellt: {ZipFilePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: Backup-Fehler: {ex.Message}");
            }
        }
        
        // geprüft!!
        // Fügt Dateien aus einem lokalen Verzeichnis rekursiv in das ZIP-Archiv ein
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
        
        // NEU: Fügt Dateien aus einem Remote-Verzeichnis rekursiv in das ZIP-Archiv ein (per SMB)
        // relativeDir: Relativer Pfad innerhalb der SMB-Freigabe (z.B. "Katalog.lrcat-data")
        private static void AddRemoteDirectoryToZip(ZipArchive zip, string relativeDir, string entryPath)
        {
            try
            {
                // Dateien im Verzeichnis auflisten
                var files = SMBConnectionManager.Instance.ListFiles(relativeDir);
                
                foreach (string fileName in files)
                {
                    string fileRelPath = relativeDir + "/" + fileName;
                    
                    // Prüfen ob es ein Verzeichnis ist (endet mit / oder \)
                    // Wir müssen die Dateiinformationen abrufen um das zu wissen
                    // Vereinfacht: Versuchen die Datei zu lesen, wenn's ein Verzeichnis ist -> rekursiv
                    byte[]? fileData = SMBConnectionManager.Instance.ReadFile(fileRelPath);
                    
                    if (fileData != null)
                    {
                        // Es ist eine Datei -> ins ZIP aufnehmen
                        string entryName = Path.Combine(entryPath, fileName).Replace('\\', '/');
                        var entry = zip.CreateEntry(entryName);
                        using (var entryStream = entry.Open())
                        {
                            entryStream.Write(fileData, 0, fileData.Length);
                        }
                    }
                    else
                    {
                        // Wahrscheinlich ein Verzeichnis -> rekursiv weiter
                        AddRemoteDirectoryToZip(zip, fileRelPath, Path.Combine(entryPath, fileName).Replace('\\', '/'));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"CatalogManager: Fehler beim Hinzufügen von Remote-Verzeichnis {relativeDir}: {ex.Message}");
            }
        }
        
        // NEU: Fügt eine einzelne Remote-Datei zum ZIP hinzu
        // relativeFilePath: Relativer Pfad innerhalb der SMB-Freigabe (z.B. "Katalog.lrcat")
        private static void AddRemoteFileToZip(ZipArchive zip, string relativeFilePath, string entryName)
        {
            try
            {
                byte[]? fileData = SMBConnectionManager.Instance.ReadFile(relativeFilePath);
                
                if (fileData != null)
                {
                    var entry = zip.CreateEntry(entryName);
                    using (var entryStream = entry.Open())
                    {
                        entryStream.Write(fileData, 0, fileData.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"CatalogManager: Fehler beim Hinzufügen von Remote-Datei {relativeFilePath}: {ex.Message}");
            }
        }
        
        // geprüft!!
        // Führt rclone sync aus (Upload oder Download) mit dynamischen Excludes
        private static void RunRcloneSync(AppConfig config, SyncDirection direction)
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
                    destPath = config.CatalogRemoteFile;
                    Log.Info("CatalogManager: Starte rclone upload (lokal → NAS)");
                }
                else if (direction == SyncDirection.Download)
                {
                    sourcePath = config.CatalogRemoteFile;
                    destPath = config.CatalogLocalPath;
                    Log.Info("CatalogManager: Starte rclone download (NAS → lokal)");
                }
                else
                {
                    return;
                }
                
                // Baue Exclude-Filter mit vollen Ornder-/Dateinamen
                var excludes = new System.Collections.Generic.List<string>
                {
                    $"--exclude \"{config.CatalogName}.lrcat.lock\"",
                    $"--exclude \"{config.CatalogName}.lrcat-shm\"",
                    $"--exclude \"{config.CatalogName}.lrcat-wal\"",
                    $"--exclude \"{GlobalConst.BACKUP_FILENAME}\"",
                    $"--exclude \"{config.CatalogName} Previews.lrdata/\"",
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

                // Kombiniere Excludes in einen String für rclone
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
                    SyncPreviewsData(config);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CatalogManager: rclone sync Fehler: {ex.Message}");
            }
        }
        
        // geprüft!!
        // Parst Transfer-Statistiken aus rclone Output
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
        
        // geprüft!!
        // Führt separaten Sync für Previews.lrdata durch (nur wenn SyncPreviewData=true)
        private static void SyncPreviewsData(AppConfig config)
        {
            try
            {
                Log.Info("CatalogManager: Starte separaten Sync für Previews.lrdata");

                // Nur den spezifischen Previews-Ordner inkludieren
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" bisync \"{config.CatalogLocalPath}\" \"{config.CatalogRemoteFile}\" --include \"{config.CatalogName} Previews.lrdata/**\" --log-level {config.LogLevel}",
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
