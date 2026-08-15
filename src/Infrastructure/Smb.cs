// SMBLibrary from https://github.com/TalAloni/SMBLibrary
// Verwaltung von SMB-Verbindungen für Remote-Dateizugriff
// Funktion
// (Connect, Disconnect, IsConnected
// Login/Logoff 
// TreeConnect, TreeDisconnect, IsTreeConnected
// ListFiles (Dateiauflistung)	
// ReadFile (Datei lesen)
// WriteFile (Datei schreiben)	
// DeleteFile (Datei löschen)

using System;
using System.Net;
using SMBLibrary;
using SMBLibrary.Client;

namespace LRCatalogSync.Infrastructure;

// SMB-Client für den Zugriff auf Remote-Freigaben
public class SmbClient
{
    private SMB2Client _client;
    private bool _isConnected = false;
    private bool _isLoggedIn = false;
    private ISMBFileStore? _fileStore = null;
    private bool _isTreeConnected = false;

    public SmbClient()
    {
        _client = new SMB2Client();
    }

    private void ResetSessionState()
    {
        _isTreeConnected = false;
        _fileStore = null;
        _isLoggedIn = false;
        _isConnected = false;
    }

    // Erzwingt einen vollständigen Session-Reset, damit bei einer invaliden Session
    // kein Logoff() mehr auf einem veralteten Login-Status ausgeführt wird.
    public void InvalidateSession()
    {
        try
        {
            _fileStore?.Disconnect();
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: InvalidateSession TreeDisconnect warf Exception: {ex.Message}");
        }

        _fileStore = null;
        _isTreeConnected = false;
        _isLoggedIn = false;
        _isConnected = false;

        // Nach einem invaliden State muss der SMB-Client neu initialisiert werden,
        // damit keine veralteten Credits / Session-Handles aus der alten Verbindung
        // in den nächsten Reconnect mitlaufen.
        try
        {
            _client.Disconnect();
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: InvalidateSession Disconnect warf Exception: {ex.Message}");
        }

        _client = new SMB2Client();
    }

    // Verbindet mit einem SMB-Server
    // returns: true bei erfolgreicher Verbindung, sonst false
    public bool Connect(string hostnameOrIp, SMBTransportType transportType = SMBTransportType.DirectTCPTransport)
    {
        // Vorherige Session sauber aufräumen, falls sie bereits veraltet ist.
        // Besonders wichtig nach Sleep/Wake: Der alte SMB-Client-State kann veraltete
        // Credits oder Handles behalten, obwohl der Server bereits wieder erreichbar ist.
        if (_isConnected || _isTreeConnected || _fileStore != null || _isLoggedIn)
        {
            Disconnect();
        }

        // Wenn der Client nach einem früheren Fehler bereits in einem ungültigen Zustand steckt,
        // muss er vollständig neu erzeugt werden, damit der nächste Connect sauber startet.
        try
        {
            if (!_client.IsConnected)
            {
                _client = new SMB2Client();
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: Client-Recreation beim Connect warf Exception: {ex.Message}");
            _client = new SMB2Client();
        }

        // Verbindung herstellen
        _isConnected = _client.Connect(hostnameOrIp, transportType);
        if (!_isConnected)
        {
            ResetSessionState();
        }

        return _isConnected;
    }

    // Prüft, ob die Verbindungs-Flags gesetzt sind (kein aktiver Test!)
    public bool IsConnected => _isConnected;

    // Trennt die Verbindung zum SMB-Server
    public void Disconnect()
    {
        if (_isTreeConnected || _fileStore != null)
        {
            try
            {
                _fileStore?.Disconnect();
            }
            catch (Exception ex)
            {
                Log.Debug($"SMB: TreeDisconnect beim Reset warf Exception: {ex.Message}");
            }

            _fileStore = null;
            _isTreeConnected = false;
        }

        if (_isConnected && _isLoggedIn)
        {
            try
            {
                _client.Logoff();
            }
            catch (Exception ex)
            {
                Log.Debug($"SMB: Logoff beim Reset warf Exception: {ex.Message}");
            }

            _isLoggedIn = false;
        }
        else
        {
            _isLoggedIn = false;
        }

        if (_isConnected)
        {
            try
            {
                _client.Disconnect();
            }
            catch (Exception ex)
            {
                Log.Debug($"SMB: Disconnect beim Reset warf Exception: {ex.Message}");
            }
        }

        ResetSessionState();
    }

    // Authentifiziert beim SMB-Server
    // returns: true bei erfolgreicher Authentifizierung, sonst false
    public bool Login(string domain, string username, string encryptedPassword)
    {
        if (!_isConnected)
        {
            return false;
        }

        // Passwort entschlüsseln
        string password;
        try
        {
            password = Cryptor.Decrypt(encryptedPassword);
        }
        catch (Exception ex)
        {
            Log.Error($"Smb: Passwort-Entschlüsselung fehlgeschlagen: {ex.Message}");
            return false;
        }

        // Authentifizieren
        NTStatus status = _client.Login(domain, username, password);
        _isLoggedIn = status == NTStatus.STATUS_SUCCESS;

        if (!_isLoggedIn)
        {
            try
            {
                _client.Logoff();
            }
            catch
            {
                // Keine weitere Aktion, da die Login-Session ungültig ist.
            }

            _client.Disconnect();
            ResetSessionState();
        }

        return _isLoggedIn;
    }

    // Meldet den Benutzer vom Server ab
    public void Logoff()
    {
        if (!_isConnected || !_isLoggedIn)
        {
            _isLoggedIn = false;
            return;
        }

        try
        {
            _client.Logoff();
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: Logoff warf Exception: {ex.Message}");
        }

        _isLoggedIn = false;
    }

    // Verbindet mit einer SMB-Freigabe (Tree Connect)
    // returns: true bei erfolgreicher Verbindung, sonst false    
    public bool TreeConnect(string shareName)
    {
        if (!_isConnected || !_isLoggedIn)
        {
            return false;
        }

        // Mit Freigabe verbinden
        NTStatus status;
        _fileStore = _client.TreeConnect(shareName, out status);

        if (status != NTStatus.STATUS_SUCCESS || _fileStore == null)
        {
            _fileStore = null;
            _isTreeConnected = false;
            return false;
        }

        _isTreeConnected = true;
        return true;
    }

    // Prüft, ob eine Verbindung mit einer Freigabe besteht
    public bool IsTreeConnected => _isTreeConnected;

    // Trennt die Verbindung zur Freigabe (Tree Disconnect)
    public void TreeDisconnect()
    {
        if (_isTreeConnected || _fileStore != null)
        {
            try
            {
                _fileStore?.Disconnect();
            }
            catch (Exception ex)
            {
                Log.Debug($"SMB: TreeDisconnect warf Exception: {ex.Message}");
            }

            _fileStore = null;
            _isTreeConnected = false;
        }
    }

    // Leitet ListShares an die SMB2Client Library weiter
    public List<string> ListShares(out NTStatus status)
    {
        return _client.ListShares(out status);
    }

    // Listet Dateien und Verzeichnisse in einem Verzeichnis auf
    // returns: Liste der Datei- und Verzeichnisnamen
    public List<string> ListFiles(string directoryPath)
    {
        if (!_isTreeConnected || _fileStore == null)
        {
            return new List<string>();
        }

        List<string> fileList = new List<string>();

        try
        {
            object directoryHandle;
            FileStatus fileStatus;

            // Verzeichnis öffnen (SMB2/3 verwendet leere Zeichenkette als Pfad)
            string searchPath = directoryPath;

            NTStatus status = _fileStore.CreateFile(out directoryHandle, out fileStatus, searchPath, 
                SMBLibrary.AccessMask.GENERIC_READ, SMBLibrary.FileAttributes.Directory, 
                SMBLibrary.ShareAccess.Read | SMBLibrary.ShareAccess.Write, 
                SMBLibrary.CreateDisposition.FILE_OPEN, 
                SMBLibrary.CreateOptions.FILE_DIRECTORY_FILE, null);

            if (status == NTStatus.STATUS_SUCCESS)
            {
                // Dateiliste abfragen
                List<QueryDirectoryFileInformation> fileListInfo;
                status = _fileStore.QueryDirectory(out fileListInfo, directoryHandle, "*", FileInformationClass.FileDirectoryInformation);

                if (status == NTStatus.STATUS_SUCCESS)
                {
                    foreach (FileDirectoryInformation fileInfo in fileListInfo)
                    {
                        // . und .. Einträge überspringen
                        if (fileInfo.FileName != "." && fileInfo.FileName != "..")
                        {
                            fileList.Add(fileInfo.FileName);
                        }
                    }
                }

                // Handle schließen
                _fileStore.CloseFile(directoryHandle);
            }
        }
        catch
        {
            // Fehler ignorieren und leere Liste zurückgeben
        }

        return fileList;
    }

    // Liest eine komplette Datei vom Remote-Server
    // returns: Dateiinhalt als Byte-Array, oder null bei Fehler
    public byte[]? ReadFile(string filePath)
    {
        if (!_isTreeConnected || _fileStore == null)
        {
            return null;
        }

        try
        {
            object fileHandle;
            FileStatus fileStatus;

            // Datei öffnen (SMB2/3 verwendet leere Zeichenkette als Pfad)
            string remotePath = filePath;

            // Datei öffnen
            NTStatus status = _fileStore.CreateFile(out fileHandle, out fileStatus, remotePath,
                SMBLibrary.AccessMask.GENERIC_READ | SMBLibrary.AccessMask.SYNCHRONIZE,
                SMBLibrary.FileAttributes.Normal,
                SMBLibrary.ShareAccess.Read,
                SMBLibrary.CreateDisposition.FILE_OPEN,
                SMBLibrary.CreateOptions.FILE_NON_DIRECTORY_FILE | SMBLibrary.CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);

            if (status != NTStatus.STATUS_SUCCESS)
            {
                return null;
            }

            // Dateigröße ermitteln
            FileInformation fileInfo;
            status = _fileStore.GetFileInformation(out fileInfo, fileHandle, FileInformationClass.FileStandardInformation);
            
            if (status != NTStatus.STATUS_SUCCESS)
            {
                _fileStore.CloseFile(fileHandle);
                return null;
            }

            // Cast zu FileStandardInformation
            FileStandardInformation? standardInfo = fileInfo as FileStandardInformation;
            if (standardInfo == null)
            {
                _fileStore.CloseFile(fileHandle);
                return null;
            }

            long fileSize = (int)standardInfo.EndOfFile;
            
            // Dateiinhalt lesen
            using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream())
            {
                long bytesRead = 0;
                while (bytesRead < fileSize)
                {
                    byte[]? data;
                    
                    status = _fileStore.ReadFile(out data, fileHandle, bytesRead, (int)_client.MaxReadSize);
                    
                    if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_END_OF_FILE)
                    {
                        _fileStore.CloseFile(fileHandle);
                        return null;
                    }

                    if (status == NTStatus.STATUS_END_OF_FILE || data.Length == 0)
                    {
                        break;
                    }
                    memoryStream.Write(data, 0, data.Length);
                    bytesRead += data.Length;
                }
                _fileStore.CloseFile(fileHandle);
                return memoryStream.ToArray();
            }
        }
        catch
        {
            // Fehler ignorieren und null zurückgeben
            return null;
        }
    }

    // Schreibt eine komplette Datei auf den Remote-Server
    // returns: true bei erfolgreichem Schreibvorgang, sonst false
    public bool WriteFile(string filePath, byte[]? data)
    {
        if (!_isTreeConnected || _fileStore == null)
        {
            return false;
        }

        if (data == null || data.Length == 0)
        {
            return false;
        }

        try
        {
            object fileHandle;
            FileStatus fileStatus;

            // Datei öffnen oder erstellen (SMB2/3 verwendet leere Zeichenkette als Pfad)
            string remotePath = filePath;

            // Datei öffnen oder erstellen
            NTStatus status = _fileStore.CreateFile(out fileHandle, out fileStatus, remotePath,
                SMBLibrary.AccessMask.GENERIC_WRITE | SMBLibrary.AccessMask.SYNCHRONIZE,
                SMBLibrary.FileAttributes.Normal,
                SMBLibrary.ShareAccess.Read | SMBLibrary.ShareAccess.Write,
                SMBLibrary.CreateDisposition.FILE_OVERWRITE_IF,
                SMBLibrary.CreateOptions.FILE_NON_DIRECTORY_FILE | SMBLibrary.CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);

            if (status != NTStatus.STATUS_SUCCESS)
            {
                return false;
            }

            // Daten schreiben
            long bytesWritten = 0;
            while (bytesWritten < data.Length)
            {
                int bytesToWrite = (int)Math.Min(data.Length - bytesWritten, _client.MaxWriteSize);
                byte[] chunk = new byte[bytesToWrite];
                Array.Copy(data, bytesWritten, chunk, 0, bytesToWrite);

                int bytesWrittenThisIteration = 0;
                status = _fileStore.WriteFile(out bytesWrittenThisIteration, fileHandle, bytesWritten, chunk);
                
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    _fileStore.CloseFile(fileHandle);
                    return false;
                }

                bytesWritten += bytesWrittenThisIteration;
            }

            _fileStore.CloseFile(fileHandle);
            return true;
        }
        catch
        {
            // Fehler ignorieren und false zur�ckgeben
            return false;
        }
    }

    // Löscht eine Datei auf dem Remote-Server
    // filePath: Pfad zur Datei (relativ zur Freigabe)
    // Rueckgabe: true bei erfolgreichem Löschen, sonst false
    public bool DeleteFile(string filePath)
    {
        if (!_isTreeConnected || _fileStore == null)
        {
            return false;
        }

        try
        {
            object fileHandle;
            FileStatus fileStatus;

            // Datei mit Löschmodus öffnen (SMB2/3 verwendet leere Zeichenkette als Pfad)
            string remotePath = filePath;

            // Datei mit Löschmodus öffnen
            NTStatus status = _fileStore.CreateFile(out fileHandle, out fileStatus, remotePath,
                SMBLibrary.AccessMask.DELETE | SMBLibrary.AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Normal,
                SMBLibrary.ShareAccess.Read | SMBLibrary.ShareAccess.Write,
                SMBLibrary.CreateDisposition.FILE_OPEN,
                0, null);

            if (status != NTStatus.STATUS_SUCCESS)
            {
                return false;
            }

            // Datei als gelöscht markieren
            FileDispositionInformation dispositionInfo = new FileDispositionInformation();
            dispositionInfo.DeletePending = true;
            status = _fileStore.SetFileInformation(fileHandle, dispositionInfo);
            
            if (status != NTStatus.STATUS_SUCCESS)
            {
                _fileStore.CloseFile(fileHandle);
                return false;
            }

            _fileStore.CloseFile(fileHandle);
            return true;
        }
        catch
        {
            // Fehler ignorieren und false zurückgeben
            return false;
        }
    }
}

// ============================================================
// SMBConnectionManager - Singleton für zentrale SMB-Verbindungsverwaltung
// Schritt 1 der SMB-Integration
// ============================================================

// Singleton-Klasse für die zentrale Verwaltung einer SMB-Verbindung
// Stellt sicher, dass nur eine einzige SMB-Verbindung gleichzeitig existiert
public sealed class SMBConnectionManager
{
    private static readonly Lazy<SMBConnectionManager> _instance = 
        new Lazy<SMBConnectionManager>(() => new SMBConnectionManager());
    
    public static SMBConnectionManager Instance => _instance.Value;
    
    // Retry-Parameter für Auto-Reconnect
    private const int MAX_CONNECT_RETRIES = 3;
    private const int CONNECT_RETRY_DELAY_MS = 1000;
    
    private SmbClient _client = new SmbClient();
    private AppConfig? _lastConfig = null;
    
    private SMBConnectionManager() { }
    
    private void ExecuteHardResetWithLogging(string reason)
    {
        Log.Debug(reason);

        try
        {
            Log.Debug("SMB: Step 1/3: InvalidateSession() start");
            _client.InvalidateSession();
            Log.Debug("SMB: Step 1/3: InvalidateSession() OK");
        }
        catch (Exception ex)
        {
            Log.Error($"[SMB] Step 1/3: InvalidateSession() warf Exception: {ex.Message}");
        }

        try
        {
            Log.Debug("SMB: Step 2/3: Disconnect() start");
            _client.Disconnect();
            Log.Debug("SMB: Step 2/3: Disconnect() OK");
        }
        catch (Exception ex)
        {
            Log.Error($"[SMB] Step 2/3: Disconnect() warf Exception: {ex.Message}");
        }
    }

    // Stellt sicher, dass eine aktive SMB-Verbindung besteht
    // Mit Auto-Reconnect bei Verbindungsproblemen
    public bool EnsureConnected(AppConfig config)
    {
        // Prüfe nur den aktuellen Zustand und starte bei Fehler einen Reconnect.
        if (_client.IsConnected && _client.IsTreeConnected && _lastConfig != null)
        {
            if (_lastConfig.RemoteIP == config.RemoteIP &&
                _lastConfig.SambaUser == config.SambaUser &&
                _lastConfig.CatalogRemotePath == config.CatalogRemotePath)
            {
                try
                {
                    _client.ListShares(out NTStatus status);
                    Log.Debug($"SMB: ListShares Status = {status}");

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        Log.Error($"SMB: ListShares fehlgeschlagen (Status: {status}), trenne und reconnecte...");
                        ExecuteHardResetWithLogging("SMB: Vor dem Reconnect wird die Verbindung hart zurückgesetzt.");
                        Log.Debug("SMB: Step 3/3: Reconnect-Start nach ListShares-Fehler");
                        Thread.Sleep(3000);
                        return TryConnectWithRetry(config);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error($"SMB: ListShares warf Exception: {ex.Message}, trenne und reconnecte...");
                    ExecuteHardResetWithLogging("SMB: Vor dem Reconnect wird die Verbindung hart zurückgesetzt.");
                    Log.Debug("SMB: Step 3/3: Reconnect-Start nach Exception");
                    Thread.Sleep(3000);
                    Log.Debug("SMB: Thread.Sleep(3000) nach Reconnect-Reset.");
                    return TryConnectWithRetry(config);
                }
            }

            Log.Debug("SMB: Parameter geändert, verbinde neu...");
            _client.TreeDisconnect();
            _client.Disconnect();
            Thread.Sleep(3000);
            return TryConnectWithRetry(config);
        }

        Log.Debug("SMB: Keine gültige Verbindung erkannt, starte Reconnect.");
        return TryConnectWithRetry(config);
    }
    
    private bool TryConnectWithRetry(AppConfig config)
    {
        for (int attempt = 1; attempt <= MAX_CONNECT_RETRIES; attempt++)
        {
            Log.Debug($"SMB: Verbindungsversuch {attempt}/{MAX_CONNECT_RETRIES}");

            if (TryConnect(config))
            {
                _lastConfig = config;
                Log.Debug($"SMB: Reconnect erfolgreich nach Versuch {attempt}.");
                return true;
            }

            if (attempt < MAX_CONNECT_RETRIES)
            {
                int delay = CONNECT_RETRY_DELAY_MS * attempt;
                Log.Debug($"SMB: Verbindung fehlgeschlagen, warte {delay}ms vor Retry...");
                Thread.Sleep(delay);
            }
        }

        Log.Error($"SMB: Verbindung nach {MAX_CONNECT_RETRIES} Versuchen fehlgeschlagen");
        return false;
    }

    private void ResetBeforeReconnectWithLogging()
    {
        Log.Debug("SMB: TryConnect - vor Connect: InvalidateSession() / Disconnect()");

        try
        {
            _client.InvalidateSession();
            Log.Debug("SMB: TryConnect - InvalidateSession() OK");
        }
        catch (Exception ex)
        {
            Log.Error($"[SMB] TryConnect - InvalidateSession() warf Exception: {ex.Message}");
        }

        try
        {
            _client.Disconnect();
            Log.Debug("SMB: TryConnect - Disconnect() OK");
        }
        catch (Exception ex)
        {
            Log.Error($"[SMB] TryConnect - Disconnect() warf Exception: {ex.Message}");
        }
    }

    // Versucht einmalig eine Verbindung herzustellen
    private bool TryConnect(AppConfig config)
    {
        // Extrahiere Share-Name aus CatalogRemotePath (z.B. "\\NAS\Freigabe\subdir" -> "Freigabe")
        string shareName = ExtractShareName(config.CatalogRemotePath);
        string serverIP = config.RemoteIP;

        Log.Debug($"SMB: TryConnect start - Server={serverIP}, Share={shareName}");

        // Sicherstellen, dass eine eventuell kaputte Session vorher sauber zurückgesetzt wird.
        ResetBeforeReconnectWithLogging();

        // Verbinde mit Server
        if (!_client.Connect(serverIP))
        {
            Log.Error($"SMB: TCP-Verbindung zu {serverIP} fehlgeschlagen");
            return false;
        }
        Log.Debug($"SMB: TCP-Verbindung zu {serverIP} hergestellt.");
        
        // Anmelden
        if (!_client.Login(string.Empty, config.SambaUser, config.SambaPasswordAes))
        {
            Log.Error($"SMB: Anmeldung als {config.SambaUser} fehlgeschlagen");
            Log.Debug("SMB: TryConnect - Login fehlgeschlagen, Disconnect() wird aufgerufen.");
            _client.Disconnect();
            return false;
        }
        Log.Debug($"SMB: Login als {config.SambaUser} erfolgreich.");
        
        // Mit Freigabe verbinden
        if (!_client.TreeConnect(shareName))
        {
            Log.Error($"SMB: TreeConnect zu Freigabe '{shareName}' fehlgeschlagen");
            Log.Debug("SMB: TryConnect - TreeConnect fehlgeschlagen, sichere Session-Reset wird aufgerufen.");
            try
            {
                _client.Logoff();
                Log.Debug("SMB: TryConnect - Logoff() nach TreeConnect-Fehler OK");
            }
            catch (Exception ex)
            {
                Log.Debug($"SMB: TreeConnect-Fehler Logoff() ignored: {ex.Message}");
            }

            try
            {
                _client.InvalidateSession();
                Log.Debug("SMB: TryConnect - InvalidateSession() nach TreeConnect-Fehler OK");
            }
            catch (Exception ex)
            {
                Log.Error($"[SMB] TryConnect - InvalidateSession() nach TreeConnect-Fehler warf Exception: {ex.Message}");
            }

            try
            {
                _client.Disconnect();
                Log.Debug("SMB: TryConnect - Disconnect() nach TreeConnect-Fehler OK");
            }
            catch (Exception ex)
            {
                Log.Error($"[SMB] TryConnect - Disconnect() nach TreeConnect-Fehler warf Exception: {ex.Message}");
            }

            return false;
        }
        Log.Debug($"SMB: TreeConnect zu Freigabe '{shareName}' erfolgreich.");
        
        Log.Debug($"SMB: Verbunden mit {serverIP}/{shareName}");
        return true;
    }
    
    // Extrahiert den Share-Namen aus einem UNC-Pfad
    // Entfernt alle / und \ und gibt den ersten Teil zurück
    private string ExtractShareName(string uncPath)
    {
        // Entferne alle / und \ am Anfang des Pfads
        string trimmed = uncPath.TrimStart('/', '\\');
        
        // Teile beim ersten / oder \ und nimm nur den ersten Teil
        int firstSeparator = trimmed.IndexOfAny(new char[] { '/', '\\' });
        if (firstSeparator > 0)
        {
            return trimmed.Substring(0, firstSeparator);
        }
        
        // Wenn kein Separator gefunden wurde, ist der gesamte String der Share-Name
        return trimmed;
    }
    
    // Prüft ob aktuell verbunden
    public bool IsConnected => _client.IsConnected && _client.IsTreeConnected;
    
    public byte[]? ReadFile(string relativePath) => _client.ReadFile(relativePath);
    
    public bool WriteFile(string relativePath, byte[]? data) => _client.WriteFile(relativePath, data);
    
    public bool DeleteFile(string relativePath) => _client.DeleteFile(relativePath);
    
    public List<string> ListFiles(string relativePath) => _client.ListFiles(relativePath);
}
