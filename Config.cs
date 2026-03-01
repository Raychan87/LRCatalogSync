using System;
using System.IO;

namespace LightroomSync
{
    // Konfigurationsklasse für das Lightroom Sync Programm.
    // Speichert alle Einstellungen wie Pfade, Intervalle usw.
    public class AppConfig
    {
        // ==================== EIGENSCHAFTEN ====================

        // Intervall in Sekunden wie oft der Status geprüft wird</summary>
        public int CheckInterval = 15;

        // Lokaler Pfad zum Lightroom Ordner</summary>
        public string LocalPath = "";

        // Lokaler Pfad zum Backups Ordner (für die Sicherung der Lightroom Kataloge)
        public string BackupsRelativePath = "Backups";  // Standard: Backups

        // Name des rclone Remotes (aus rclone config)</summary>
        public string RemoteName = "synology";

        // Pfad auf dem Remote (z.B. "Lightroom")</summary>
        public string RemotePath = "";

        // Relativer Pfad zu rclone.exe (z.B. "./rclone/rclone.exe")</summary>
        public string RcloneRelativePath = "./rclone/rclone.exe";

        // Absolute Pfade (werden beim Laden berechnet)
        public string RclonePath { get; private set; }

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
                    if (line.Contains("="))
                    {
                        // Teile die Zeile am "=" Zeichen
                        string key = line.Split('=')[0].Trim();      // Linke Seite (z.B. "CheckInterval")
                        string value = line.Split('=')[1].Trim();    // Rechte Seite (z.B. "15")

                        // Weise die Werte den Eigenschaften zu
                        if (key == "CheckInterval") CheckInterval = int.Parse(value);  // Text zu Zahl
                        if (key == "LocalPath") LocalPath = value;
                        if (key == "RemoteName") RemoteName = value;
                        if (key == "RemotePath") RemotePath = value;
                        if (key == "RcloneRelativePath") RcloneRelativePath = value;
                    }
                }
            }

            // Berechne absolute Pfade aus den relativen Pfaden
            RclonePath = GetAbsolutePath(RcloneRelativePath, baseDir);
        }

        // Wandelt einen relativen Pfad in einen absoluten Pfad um.
        // relativePath" --> Relativer Pfad (z.B. "./rclone/rclone.exe")
        // baseDir" --> Basis-Verzeichnis
        // returns --> Absoluter Pfad
        private string GetAbsolutePath(string relativePath, string baseDir)
        {
            // Wenn der Pfad bereits absolut ist (mit C:\...),nimm ihn direkt
            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }

            // Sonst kombiniere basisDir + relativePath
            return Path.GetFullPath(Path.Combine(baseDir, relativePath));
        }

        // Speichert die Konfiguration in eine Datei.
        // path --> Ziel-Pfad
        public void Save(string path)
        {
            // Erstelle Array mit allen Einstellungen
            string[] lines = new string[]
            {
                "CheckInterval=" + CheckInterval,
                "LocalPath=" + LocalPath,
                "RemoteName=" + RemoteName,
                "RemotePath=" + RemotePath,
                "RcloneRelativePath=" + RcloneRelativePath
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