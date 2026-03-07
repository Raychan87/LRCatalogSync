using System;
using System.IO;

namespace LRCatalogSync
{
    // Konfigurationsklasse für das Lightroom Sync Programm.
    // Speichert alle Einstellungen wie Pfade, Intervalle usw.
    public class AppConfig
    {
        // ==================== EIGENSCHAFTEN ====================

        // Lokaler Pfad zum Lightroom Ordner
        public string LocalPath = "";

        // Lokaler Pfad zum Backups Ordner (für die Sicherung der Lightroom Kataloge)
        public string BackupsLocalPath = "";

        // Remote Pfad zum Backups Ordner (auf dem Samba Server)
        public string BackupsRemotePath = "";

        // Aktiviert/Deaktiviert die Backup-Synchronisierung
        public bool EnableBackups = true;

        // IP von Remote Pfad (Samba Server)
        public string RemoteIP = "";

        // Pfad auf dem Remote (z.B. "Lightroom")
        public string RemotePath = "";

        // Ordner in dem rclone.exe liegt (z.B. "./rclone" oder "C:\Program Files\rclone")
        public string RcloneFolder = "./rclone";

        // Samba Benutzername
        public string SambaUser = "";

        // Samba Passwort (verschlüsselt mit rclone obscure)
        public string SambaPassword = "";

        // Absolute Pfade (werden beim Laden berechnet)
        public string RclonePath { get; private set; }

        //Einstellung von LogLevel = Debug/Info/Warn/Error
        public string LogLevel { get; set; } = "Info";

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
                        // Teile die Zeile am "=" Zeichen
                        string key = line.Split('=')[0].Trim();      // Linke Seite (z.B. "CheckInterval")
                        string value = line.Split('=')[1].Trim();    // Rechte Seite (z.B. "15")

                        // Weise die Werte den Eigenschaften zu
                        if (key == "LocalPath") LocalPath = value;
                        if (key == "BackupsLocalPath") BackupsLocalPath = value;
                        if (key == "BackupsRemotePath") BackupsRemotePath = value;
                        if (key == "EnableBackups") EnableBackups = bool.TryParse(value, out bool result) && result;
                        if (key == "RemotePath") RemotePath = value;
                        if (key == "RcloneFolder") RcloneFolder = value;
                        if (key == "RemoteIP") RemoteIP = value;
                        if (key == "SambaUser") SambaUser = value;
                        if (key == "SambaPassword") SambaPassword = value;
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
                "LocalPath=" + LocalPath,
                "BackupsLocalPath=" + BackupsLocalPath,
                "BackupsRemotePath=" + BackupsRemotePath,
                "EnableBackups=" + EnableBackups,
                "RemotePath=" + RemotePath,
                "RcloneFolder=" + RcloneFolder,
                "RemoteIP=" + RemoteIP,
                "SambaUser=" + SambaUser,
                "SambaPassword=" + SambaPassword,
                "LogLevel=" + LogLevel
            };

            // Schreibe in die Datei
            File.WriteAllLines(path, lines);
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