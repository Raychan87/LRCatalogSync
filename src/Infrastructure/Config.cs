using System;
using System.IO;
using LRCatalogSync.Core;

namespace LRCatalogSync.Infrastructure
{
    // Konfigurationsklasse für das Lightroom Sync Programm.
    // Speichert alle Einstellungen wie Pfade, Intervalle usw.
    public class AppConfig
    {
        // ==================== EIGENSCHAFTEN ====================

        // Lokaler Pfad zur Lightroom Katalog-Datei (.lrcat)
        public string CatalogLocalFile = "C:/Benutzer/[Benutzername]/Bilder/Lightroom/[Katalogname].lrcat";

        // Lokaler Pfad zum Backups Ordner (für die Sicherung der Lightroom Kataloge) 
        public string BackupsLocalPath = "C:/Benutzer/[Benutzername]/Bilder/Lightroom/Backups/";

        // Remote Pfad zum Backups Ordner (auf dem Samba Server)
        public string BackupsRemotePath = "/SambaOrdner/Backups/";

        // Aktiviert/Deaktiviert die Backup-Synchronisierung
        public bool EnableBackups = true;

        // Aktiviert/Deaktiviert rclone copy (Backup vor Sync)
        public bool EnableRcloneCopy = true;

        // Ordnername für rclone copy Backup
        public string RcloneCopyFolderName = "Last_catalog_backup";

        // Syncen von Preview-Daten (aktiviert *Smart Previews.lrdata, deaktiviert auch *Previews.lrdata)
        public bool SyncPreviewData = false;

        // IP von Remote Pfad (Samba Server)
        public string RemoteIP = "xxx.xxx.xxx.xxx";

        // Remote Pfad zum Lightroom Katalog-Ordner (auf dem Samba Server)
        public string CatalogRemotePath = "/SambaOrdner/";

        // Ordner in dem rclone.exe liegt (z.B. "./rclone" oder "C:\Program Files\rclone")
        public string RcloneFolder = "./rclone";

        // Samba Benutzername
        public string SambaUser = "";

        // Passwort für Rclone (verschlüsselt mit rclone obscure)
        public string SambaPasswordRclone = "";

        // Passwort für Samba (verschlüsselt mit AES-256)
        public string SambaPasswordAes = "";

        // Absolute Pfade (werden beim Laden berechnet)
        public string RclonePath { get; private set; } = null!;

        //Einstellung von LogLevel = DEBUG/INFO/NOTICE/ERROR
        public string LogLevel { get; set; } = "INFO";

        // ==================== BERECHNETE EIGENSCHAFTEN (Read-Only) ====================

        // Extrahiert den lokalen Pfad ohne Dateiname (z.B. "C:/Benutzer/[Benutzername]/Bilder/Lightroom/")
        // Beispiel: "C:/Benutzer/[Benutzername]/Bilder/Lightroom/"
        public string CatalogLocalPath => Path.GetDirectoryName(CatalogLocalFile) ?? string.Empty;
        
        // Extrahiert den Dateinamen (z.B. "MeineFotos.lrcat")
        // Beispiel: "[Katalogname].lrcat"
        public string CatalogFileName => Path.GetFileName(CatalogLocalFile);
        
        // Extrahiert den Katalognamen ohne Endung (z.B. "MeineFotos")
        // Beispiel: "[Katalogname]"
        public string CatalogName => Path.GetFileNameWithoutExtension(CatalogLocalFile);

        // Remote: Vollständiger Pfad zur Lightroom Katalog-Datei auf dem Samba Server
        // Beispiel: "/SambaOrdner/[Katalogname].lrcat"
        public string CatalogRemoteFile => Path.Combine(CatalogRemotePath, CatalogFileName);

        // Vollständiger Pfad zur Lightroom Lock-Datei (.lrcat.lock)
        // Beispiel: "C:/Benutzer/[Benutzername]/Bilder/Lightroom/[Katalogname].lrcat.lock"
        public string CatalogLockFile => Path.Combine(CatalogLocalPath, $"{CatalogName}.lrcat.lock");

        // Vollständiger Pfad zur lokalen LRCatSync Lock-Datei (LocalPath + Dateiname)
        // Beispiel: "C:/Benutzer/[Benutzername]/Bilder/Lightroom/LRCatSync.lock"
        public string SyncLocalLockFile => Path.Combine(CatalogLocalPath, GlobalConst.LOCK_FILE);

        // Vollständiger Pfad zur remote LRCatSync Lock-Datei (RemotePath + Dateiname)
        // Beispiel: "/SambaOrdner/LRCatSync.lock"
        public string SyncRemoteLockFile => Path.Combine(CatalogRemotePath, GlobalConst.LOCK_FILE);

        // ==================== METHODEN ====================
        // Lädt die Konfiguration aus einer Datei.
        // "path" --> Pfad zur config.txt
        // "baseDir" --> Basis-Verzeichnis des Programms
        public void Load(string path, string baseDir)
        {
            // Prüfe ob Datei existiert
            if (File.Exists(path))
            {
                // Lese alle Zeilen aus der Datei
                string[] lines = File.ReadAllLines(path);

                // Gehe jede Zeile durch
                foreach (string line in lines)
                {
                    // Nur Zeilen mit "=" verarbeiten
                    if (line.Contains("=") && !line.StartsWith("#"))
                    {
                        // Teile die Zeile am ERSTEN "=" Zeichen
                        // Wichtig: Split mit Limit 2, damit Werte mit "=" (z.B. Base64-Padding) erhalten bleiben
                        string[] parts = line.Split('=', 2);
                        string key = parts[0].Trim();      // Linke Seite (z.B. "CheckInterval")
                        string value = parts[1].Trim();    // Rechte Seite (z.B. "15")

                        // Weise die Werte den Eigenschaften zu
                        if (key == "CatalogLocalFile") CatalogLocalFile = value;
                        // Migration: Altes CatalogLocalPath zu CatalogLocalFile konvertieren
                        if (key == "CatalogLocalPath") 
                        {
                            // Wenn CatalogLocalPath ein Ordner ist, suche nach .lrcat Datei
                            if (Directory.Exists(value))
                            {
                                string[] lrcatFiles = Directory.GetFiles(value, "*.lrcat", SearchOption.TopDirectoryOnly);
                                if (lrcatFiles.Length > 0)
                                {
                                    CatalogLocalFile = lrcatFiles[0];
                                    Log.Debug($"Config: Migration von CatalogLocalPath zu CatalogLocalFile: {CatalogLocalFile}");
                                }
                            }
                        }
                        if (key == "BackupsLocalPath") BackupsLocalPath = value;
                        if (key == "BackupsRemotePath") BackupsRemotePath = value;
                        if (key == "EnableBackups") EnableBackups = bool.TryParse(value, out bool result) && result;
                        if (key == "EnableRcloneCopy") EnableRcloneCopy = bool.TryParse(value, out bool result3) && result3;
                        if (key == "RcloneCopyFolderName") RcloneCopyFolderName = value;
                        if (key == "SyncPreviewData") SyncPreviewData = bool.TryParse(value, out bool result2) && result2;
                        if (key == "CatalogRemotePath") CatalogRemotePath = value;
                        if (key == "RcloneFolder") RcloneFolder = value;
                        if (key == "RemoteIP") RemoteIP = value;
                        if (key == "SambaUser") SambaUser = value;
                        if (key == "SambaPassword") SambaPasswordRclone = value; // Migration: Altes SambaPassword → SambaPasswordRclone
                        if (key == "SambaPasswordRclone") SambaPasswordRclone = value;
                        if (key == "SambaPasswordAes") SambaPasswordAes = value;
                        if (key == "LogLevel") LogLevel = value;
                    }
                }
            }

            // Berechne absolute Pfade
            RclonePath = GetAbsoluteRclonePath(RcloneFolder, baseDir);
        }

        // Wandelt einen relativen Rclone-Ordnerpfad in den absoluten Pfad zur rclone.exe um.
        // rcloneFolder --> Rclone-Ordner (z.B. "./rclone" oder "C:\Program Files\rclone")
        // baseDir --> Basis-Verzeichnis
        // returns --> Absoluter Pfad zur rclone.exe
        private string GetAbsoluteRclonePath(string rcloneFolder, string baseDir)
        {
            string path = rcloneFolder;

            // Wenn bereits absolut, nutze es direkt
            if (Path.IsPathRooted(path))
            {
                return Path.Combine(path, "rclone.exe");
            }

            // Kombiniere mit baseDir
            string absoluteFolder = Path.GetFullPath(Path.Combine(baseDir, path));
            return Path.Combine(absoluteFolder, "rclone.exe");
        }

        // Speichert die Konfiguration in eine Datei.
        // path --> Ziel-Pfad
        public void Save(string path)
        {
            // Erstelle Array mit allen Einstellungen
            string[] lines = new string[]
            {
                "CatalogLocalFile=" + CatalogLocalFile,
                "BackupsLocalPath=" + BackupsLocalPath,
                "BackupsRemotePath=" + BackupsRemotePath,
                "EnableBackups=" + EnableBackups,
                "EnableRcloneCopy=" + EnableRcloneCopy,
                "RcloneCopyFolderName=" + RcloneCopyFolderName,
                "SyncPreviewData=" + SyncPreviewData,
                "CatalogRemotePath=" + CatalogRemotePath,
                "RcloneFolder=" + RcloneFolder,
                "RemoteIP=" + RemoteIP,
                "SambaUser=" + SambaUser,
                "SambaPasswordRclone=" + SambaPasswordRclone,
                "SambaPasswordAes=" + SambaPasswordAes,
                "LogLevel=" + LogLevel
            };

            // Schreibe in die Datei
            File.WriteAllLines(path, lines);
        }

        // Prüft ob der Backup-Pfad innerhalb des Katalog-Pfads liegt
        public bool IsBackupInsideCatalogPath()
        {
            if (string.IsNullOrEmpty(BackupsLocalPath) || 
                string.IsNullOrEmpty(CatalogLocalPath))
                return false;
            
            string backupNormalized = Path.GetFullPath(BackupsLocalPath).ToLower();
            string catalogNormalized = Path.GetFullPath(CatalogLocalPath).ToLower();
            
            return backupNormalized.StartsWith(catalogNormalized);
        }

        // Gibt das relative Exclude-Pattern für rclone zurück, wenn Backup im Katalog-Pfad liegt
        public string GetRelativeBackupExcludePattern()
        {
            if (!IsBackupInsideCatalogPath())
                return string.Empty;
            
            string relativeBackupPath = Path.GetRelativePath(CatalogLocalPath, BackupsLocalPath);
            return relativeBackupPath.Replace("\\", "/");  // Unix-Pfade für rclone
        }

        // Statische Methode um Config zu laden (Komfort-Methode)
        public static AppConfig LoadFromFile(string path, string baseDir)
        {
            AppConfig config = new AppConfig();
            config.Load(path, baseDir);
            return config;
        }
    }
}