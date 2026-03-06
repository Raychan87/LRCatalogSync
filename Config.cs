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
        public string BackupsRelativePath = "Backups";  // Standard: Backups

        // IP von Remote Pfad (Samba Server)
        public string RemoteIP = "";

        // Pfad auf dem Remote (z.B. "Lightroom")
        public string RemotePath = "";

        // Relativer Pfad zu rclone.exe (z.B. "./rclone/rclone.exe")
        public string RcloneRelativePath = "./rclone/rclone.exe";        

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
                        if (key == "BackupsRelativePath") BackupsRelativePath = value;
                        if (key == "RemotePath") RemotePath = value;
                        if (key == "RcloneRelativePath") RcloneRelativePath = value;
                        if (key == "RemoteIP") RemoteIP = value;
                        if (key == "SambaUser") SambaUser = value;
                        if (key == "SambaPassword") SambaPassword = value;
                        if (key == "LogLevel") LogLevel = value;
                    }
                }
            }

            // Berechne absolute Pfade
            RclonePath = GetAbsolutePath(RcloneRelativePath, baseDir);
        }

        // Wandelt einen relativen Pfad in einen absoluten Pfad um.
        // relativePath" --> Relativer Pfad (z.B. "./rclone/rclone.exe")
        // baseDir" --> Basis-Verzeichnis
        // returns --> Absoluter Pfad
        private string GetAbsolutePath(string relativePath, string baseDir)
        {
            // Füge "rclone.exe" hinzu, falls nicht vorhanden
            string path = relativePath;
            if (!path.EndsWith("rclone.exe", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(path, "rclone.exe");
            }

            // Wenn bereits absolut, nimm direkt
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            // Kombiniere mit baseDir
            return Path.GetFullPath(Path.Combine(baseDir, path));
        }

        // Speichert die Konfiguration in eine Datei.
        // path --> Ziel-Pfad
        public void Save(string path)
        {
            // Erstelle Array mit allen Einstellungen
            string[] lines = new string[]
            {
                "LocalPath=" + LocalPath,
                "BackupsRelativePath=" + BackupsRelativePath,
                "RemotePath=" + RemotePath,
                "RcloneRelativePath=" + RcloneRelativePath,
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