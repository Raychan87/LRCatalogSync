using Microsoft.Win32;

namespace LRCatalogSync.Infrastructure
{
    // Verwaltet die Autorun-Einstellung für den Windows-Start
    public static class Autorun
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegistryValueName = "LRCatalogSync";

        // Aktiviert den automatischen Start beim Systemstart
        public static void Enable(string exePath)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                key?.SetValue(RegistryValueName, $"\"{exePath}\"", RegistryValueKind.String);
            }
            catch
            {
                // Fehler werden von außen abgefangen
            }
        }

        // Deaktiviert den automatischen Start beim Systemstart
        public static void Disable()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                key?.DeleteValue(RegistryValueName, false);
            }
            catch
            {
                // Fehler werden von außen abgefangen
            }
        }

        // Prüft, ob der automatische Start aktiviert ist
        // returns: true, wenn Autorun aktiviert ist, sonst false
        public static bool IsEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                object? value = key?.GetValue(RegistryValueName);
                return value != null;
            }
            catch
            {
                // Fehler werden von außen abgefangen
            }
            return false;
        }
    }
}