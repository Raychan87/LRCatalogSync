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
        // Phase 3: rclone sync ausführen (Upload oder Download)
        // Phase 4: Cleanup (IMMER im finally!)
        public static void RunCatalogSync(AppConfig config, TrayManager trayManager)
        {
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
                
                // ========== PHASE 3: RCLONE SYNC AUSFÜHREN ==========
                Log.Info($"CatalogManager: Starte rclone {syncDirection.ToString().ToLower()}");
                trayManager.UpdateStatus("LSyncing");  // 🟡 Gelb
                
                if (syncDirection == SyncDirection.Upload)
                {
                    RunRcloneSync(config, SyncDirection.Upload);
                }
                else if (syncDirection == SyncDirection.Download)
                {
                    RunRcloneSync(config, SyncDirection.Download);
                }
                
                // ========== PHASE 4: CLEANUP ==========
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
        
        // geprüft!! 2026.07.24
        // Führt rclone sync aus (Upload oder Download) mit dynamischen Excludes
        private static void RunRcloneSync(AppConfig config, SyncDirection direction)
        {
            try
            {
                DateTime syncStartTime = DateTime.Now;                             
                int transferredFiles = 0;
                long transferredBytes = 0;    
                string sourcePath, destPath, copyBackupPath, copySourcePath;               
                string tempLog = Path.Combine(GlobalData.BaseDir, "data", "logs", "rclone_backup_sync.log");
                string logsDir = Path.Combine(GlobalData.BaseDir, "data", "logs");
                
                // ========== Richtung festlegen  ==========
                // Bestimme Quelle und Ziel basierend auf Sync-Richtung
                if (direction == SyncDirection.Upload)
                {
                    sourcePath = config.CatalogLocalPath;
                    destPath =  $"synology:{config.CatalogRemotePath}";
                    Log.Debug("CatalogManager: Starte rclone upload (lokal → NAS)");
                }
                else if (direction == SyncDirection.Download)
                {
                    sourcePath =  $"synology:{config.CatalogRemotePath}";
                    destPath = config.CatalogLocalPath;
                    Log.Debug("CatalogManager: Starte rclone download (NAS → lokal)");
                }
                else
                {
                    Log.Debug("CatalogManager: Keine Sync-Richtung erkannt, breche ab");
                    return;
                }
                
                // ========== Include-Filter  ==========
                // Baue Include-Filter mit vollen Ornder-/Dateinamen
                // WICHTIG: Der Exclude-Filter für den Backup-Ordner MUSS am Ende stehen,
                // damit er nach den Include-Filtern angewendet wird und Vorrang hat
                var includes = new System.Collections.Generic.List<string>
                {
                    $"--include \"/{config.CatalogName}.lrcat\"",
                    $"--include \"/{config.CatalogName}.lrcat-data/**\"",
                    $"--include \"/{config.CatalogName} Sync.lrdata/**\"",
                    $"--include \"/{config.CatalogName} Smart Previews.lrdata/**\"",
                    $"--include \"/{config.CatalogName} Helper.lrdata/**\"",
                };

                // Kombiniere Includes in einen String für rclone
                string includeArgs = string.Join(" ", includes);
                
                // ========== RCLONE COPY AUSFÜHREN (Backup) ==========                
                // Prüfe ob rclone copy aktiviert ist
                if (!config.EnableRcloneCopy)
                {
                    Log.Debug("CatalogManager: rclone copy ist deaktiviert, überspringe Backup");
                }
                else
                {
                    // Bestimme Backup-Pfad basierend auf Sync-Richtung   
                    if (direction == SyncDirection.Upload)
                    {
                        copyBackupPath = $"synology:{config.CatalogRemotePath}{config.RcloneCopyFolderName}"; 
                        copySourcePath = $"synology:{config.CatalogRemotePath}";

                        // Remote-Backup-Ordner löschen
                        var deleteBackupPsi = new ProcessStartInfo
                        {
                            FileName = config.RclonePath,
                            Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" delete \"{copyBackupPath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };                        
                        using (var deleteProc = Process.Start(deleteBackupPsi))
                        {
                            Log.Debug($"CatalogManager: Remote Backup-Ordner gelöscht: {copyBackupPath}");
                            deleteProc?.WaitForExit();
                        }
                    }
                    else
                    { 
                        copyBackupPath = Path.Combine(config.CatalogLocalPath, config.RcloneCopyFolderName);
                        copySourcePath = config.CatalogLocalPath;

                        // Lokalen Backup-Ordner löschen
                        if (Directory.Exists(copyBackupPath))
                        {
                            Directory.Delete(copyBackupPath, recursive: true);
                            Log.Debug($"CatalogManager: Lokalen Backup-Ordner gelöscht: {copyBackupPath}");
                        }
                    }

                    // Erstelle Logs-Ordner falls nicht vorhanden
                    if (!Directory.Exists(logsDir))
                        Directory.CreateDirectory(logsDir);

                    // Lösche temporäre Log-Datei falls vorhanden
                    if (File.Exists(tempLog))
                        File.Delete(tempLog);

                    Log.Debug("CatalogManager: rclone copy starten");
                    
                    // Baue ProcessStartInfo für rclone copy
                    var copyPsi = new ProcessStartInfo
                    {
                        FileName = config.RclonePath,
                        Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" copy \"{copySourcePath}\" \"{copyBackupPath}\" {includeArgs} --log-file \"{tempLog}\" --log-level {config.LogLevel}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    // Führe rclone copy aus und warte auf Beendigung
                    using (var copyProc = Process.Start(copyPsi))
                    {
                        if (copyProc != null)
                        {
                            string copyOutput = copyProc.StandardOutput.ReadToEnd();
                            string copyError = copyProc.StandardError.ReadToEnd();
                            copyProc.WaitForExit();
                            
                            if (copyProc.ExitCode == 0)
                            {
                                Log.Debug($"CatalogManager: rclone copy erfolgreich → {config.RcloneCopyFolderName}");
                            }
                            else
                            {
                                Log.Error($"CatalogManager: rclone copy fehlgeschlagen (ExitCode: {copyProc.ExitCode})");
                            }
                        }
                    }
                }
                
                // ========== RCLONE DELETE AUSFÜHREN  ==========
                // Erstelle Logs-Ordner falls nicht vorhanden
                if (!Directory.Exists(logsDir))
                    Directory.CreateDirectory(logsDir);

                // Lösche temporäre Log-Datei falls vorhanden
                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                Log.Debug($"CatalogManager: rclone delete starten");

                // Baue ProcessStartInfo für rclone delete
                // WICHTIG: Exclude für Backup-Ordner MUSS vor den Include-Filtern stehen!
                var deletePsi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" delete \"{destPath}\" {includeArgs} --rmdirs --log-file \"{tempLog}\" --log-level {config.LogLevel}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Führe rclone delete aus und warte auf Beendigung
                using (var deleteProc = Process.Start(deletePsi))
                {
                    if (deleteProc != null)
                    {
                        string deleteOutput = deleteProc.StandardOutput.ReadToEnd();
                        string deleteError = deleteProc.StandardError.ReadToEnd();
                        deleteProc.WaitForExit();
                        
                        if (deleteProc.ExitCode == 0)
                        {
                            Log.Debug($"CatalogManager: rclone delete erfolgreich");
                        }
                        else
                        {
                            Log.Error($"CatalogManager: rclone delete fehlgeschlagen (ExitCode: {deleteProc.ExitCode})");
                        }
                    }
                }
                
                // ========== RCLONE SYNC AUSFÜHREN ==========
                // Erstelle Logs-Ordner falls nicht vorhanden
                if (!Directory.Exists(logsDir))
                    Directory.CreateDirectory(logsDir);

                // Lösche temporäre Log-Datei falls vorhanden
                if (File.Exists(tempLog))
                    File.Delete(tempLog);

                Log.Debug($"CatalogManager: rclone sync starten");

                // Baue ProcessStartInfo für rclone sync
                var psi = new ProcessStartInfo
                {
                    FileName = config.RclonePath,
                    Arguments = $"--config \"{GlobalData.RcloneConfigPath}\" sync \"{sourcePath}\" \"{destPath}\" {includeArgs} --log-file \"{tempLog}\" --log-level {config.LogLevel}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                // Führe rclone sync aus und warte auf Beendigung
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
                        
                        Log.Debug($"CatalogManager: rclone {direction.ToString().ToLower()} erfolgreich");
                        Log.Debug($"CatalogManager: Transfer-Statistiken:");
                        Log.Debug($"  - Richtung: {direction}");
                        Log.Debug($"  - Dateien: {transferredFiles}");
                        Log.Debug($"  - Bytes: {transferredBytes:N0}");
                        Log.Debug($"  - Dauer: {duration:hh\\:mm\\:ss}");
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
