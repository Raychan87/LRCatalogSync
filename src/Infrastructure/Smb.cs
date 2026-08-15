// SMB-Client für Remote-Zugriffe über SMBLibrary.
// Die Klasse kapselt Verbindungsaufbau, Login, TreeConnect und Dateizugriff
// für die Synchronisierung mit einer freigegebenen SMB-Freigabe.

using System;
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

    // Erzwingt einen sauberen Session-Reset, wenn der bisherige SMB-Status
    // als ungültig erkannt wurde. Dabei wird nur der lokale State zurückgesetzt,
    // und der Client-Wrapper wird für den nächsten Connect neu aufgebaut.
    public void InvalidateSession()
    {
        try
        {
            _fileStore?.Disconnect();
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: InvalidateSession - TreeDisconnect fehlgeschlagen: {ex.Message}");
        }

        _fileStore = null;
        _isTreeConnected = false;
        _isLoggedIn = false;
        _isConnected = false;

        try
        {
            _client.Disconnect();
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: InvalidateSession - Disconnect fehlgeschlagen: {ex.Message}");
        }

        _client = new SMB2Client();
    }

    // Verbindet mit einem SMB-Server und setzt vorher nur den lokalen Zustand zurück.
    public bool Connect(string hostnameOrIp, SMBTransportType transportType = SMBTransportType.DirectTCPTransport)
    {
        if (_isConnected || _isTreeConnected || _fileStore != null || _isLoggedIn)
        {
            Disconnect();
        }

        _client = new SMB2Client();

        _isConnected = _client.Connect(hostnameOrIp, transportType);
        if (!_isConnected)
        {
            ResetSessionState();
        }

        return _isConnected;
    }

    public bool IsConnected => _isConnected;

    // Schließt die aktuelle Session sauber und setzt den internen State zurück.
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
                Log.Debug($"SMB: Disconnect - TreeDisconnect fehlgeschlagen: {ex.Message}");
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
                Log.Debug($"SMB: Disconnect - Logoff fehlgeschlagen: {ex.Message}");
            }
        }

        if (_isConnected)
        {
            try
            {
                _client.Disconnect();
            }
            catch (Exception ex)
            {
                Log.Debug($"SMB: Disconnect - Disconnect fehlgeschlagen: {ex.Message}");
            }
        }

        ResetSessionState();
    }

    // Authentifiziert beim SMB-Server und setzt im Fehlerfall den State zurück.
    public bool Login(string domain, string username, string encryptedPassword)
    {
        if (!_isConnected)
        {
            return false;
        }

        string password;
        try
        {
            password = Cryptor.Decrypt(encryptedPassword);
        }
        catch (Exception ex)
        {
            Log.Error($"SMB: Passwort-Entschlüsselung fehlgeschlagen: {ex.Message}");
            return false;
        }

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
                // Die Session ist bereits ungültig; hier gibt es nichts mehr zu bereinigen.
            }

            try
            {
                _client.Disconnect();
            }
            catch
            {
                // Ignorieren, da der Login-Status bereits fehlerhaft ist.
            }

            ResetSessionState();
        }

        return _isLoggedIn;
    }

    // Meldet den Benutzer vom Server ab, sofern eine gültige Session aktiv ist.
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
            Log.Debug($"SMB: Logoff fehlgeschlagen: {ex.Message}");
        }

        _isLoggedIn = false;
    }

    // Verbindet mit einer SMB-Freigabe.
    public bool TreeConnect(string shareName)
    {
        if (!_isConnected || !_isLoggedIn)
        {
            return false;
        }

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

    public bool IsTreeConnected => _isTreeConnected;

    // Trennt die Freigabe-Verbindung, falls eine aktive Tree-Session existiert.
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
                Log.Debug($"SMB: TreeDisconnect fehlgeschlagen: {ex.Message}");
            }

            _fileStore = null;
            _isTreeConnected = false;
        }
    }

    // Prüft den Serverzustand mit einer kurzen SMB-Anfrage.
    public List<string> ListShares(out NTStatus status)
    {
        return _client.ListShares(out status);
    }

    // Listet Dateien und Ordner in einem Remote-Verzeichnis auf.
    public List<string> ListFiles(string directoryPath)
    {
        if (!_isTreeConnected || _fileStore == null)
        {
            return new List<string>();
        }

        var fileList = new List<string>();

        try
        {
            object directoryHandle;
            FileStatus fileStatus;

            NTStatus status = _fileStore.CreateFile(out directoryHandle, out fileStatus, directoryPath,
                SMBLibrary.AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Directory,
                SMBLibrary.ShareAccess.Read | SMBLibrary.ShareAccess.Write,
                SMBLibrary.CreateDisposition.FILE_OPEN,
                SMBLibrary.CreateOptions.FILE_DIRECTORY_FILE,
                null);

            if (status == NTStatus.STATUS_SUCCESS)
            {
                List<QueryDirectoryFileInformation> fileListInfo;
                status = _fileStore.QueryDirectory(out fileListInfo, directoryHandle, "*", FileInformationClass.FileDirectoryInformation);

                if (status == NTStatus.STATUS_SUCCESS)
                {
                    foreach (FileDirectoryInformation fileInfo in fileListInfo)
                    {
                        if (fileInfo.FileName != "." && fileInfo.FileName != "..")
                        {
                            fileList.Add(fileInfo.FileName);
                        }
                    }
                }

                _fileStore.CloseFile(directoryHandle);
            }
        }
        catch
        {
            // Keine weitere Verarbeitung, da der Rückgabewert als leerer Bestand behandelt wird.
        }

        return fileList;
    }

    // Liest eine komplette Datei vom Remote-Server und gibt sie als Byte-Array zurück.
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

            NTStatus status = _fileStore.CreateFile(out fileHandle, out fileStatus, filePath,
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

            FileInformation fileInfo;
            status = _fileStore.GetFileInformation(out fileInfo, fileHandle, FileInformationClass.FileStandardInformation);

            if (status != NTStatus.STATUS_SUCCESS)
            {
                _fileStore.CloseFile(fileHandle);
                return null;
            }

            if (fileInfo is not FileStandardInformation standardInfo)
            {
                _fileStore.CloseFile(fileHandle);
                return null;
            }

            long fileSize = (int)standardInfo.EndOfFile;

            using (var memoryStream = new System.IO.MemoryStream())
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
            return null;
        }
    }

    // Schreibt eine komplette Datei auf den Remote-Server.
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

            NTStatus status = _fileStore.CreateFile(out fileHandle, out fileStatus, filePath,
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

            long bytesWritten = 0;
            while (bytesWritten < data.Length)
            {
                int bytesToWrite = (int)Math.Min(data.Length - bytesWritten, _client.MaxWriteSize);
                var chunk = new byte[bytesToWrite];
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
            return false;
        }
    }

    // Löscht eine Datei auf dem Remote-Server.
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

            NTStatus status = _fileStore.CreateFile(out fileHandle, out fileStatus, filePath,
                SMBLibrary.AccessMask.DELETE | SMBLibrary.AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Normal,
                SMBLibrary.ShareAccess.Read | SMBLibrary.ShareAccess.Write,
                SMBLibrary.CreateDisposition.FILE_OPEN,
                0,
                null);

            if (status != NTStatus.STATUS_SUCCESS)
            {
                return false;
            }

            var dispositionInfo = new FileDispositionInformation { DeletePending = true };
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
            return false;
        }
    }
}

// Zentraler SMB-Manager für eine einzelne aktive Session.
public sealed class SMBConnectionManager
{
    private static readonly Lazy<SMBConnectionManager> _instance =
        new Lazy<SMBConnectionManager>(() => new SMBConnectionManager());

    public static SMBConnectionManager Instance => _instance.Value;

    private const int MAX_CONNECT_RETRIES = 3;
    private const int CONNECT_RETRY_DELAY_MS = 1000;

    private SmbClient _client = new SmbClient();
    private AppConfig? _lastConfig = null;
    private Action<string>? _trayStatusCallback;

    private SMBConnectionManager() { }

    public void SetStatusCallback(Action<string>? callback)
    {
        _trayStatusCallback = callback;
    }

    private void NotifyTrayStatus(string status)
    {
        _trayStatusCallback?.Invoke(status);
    }

    private void ExecuteHardResetWithLogging(string reason)
    {
        Log.Debug(reason);

        try
        {
            _client.InvalidateSession();
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: HardReset - InvalidateSession fehlgeschlagen: {ex.Message}");
        }

        try
        {
            _client.Disconnect();
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: HardReset - Disconnect fehlgeschlagen: {ex.Message}");
        }
    }

    // Prüft, ob eine SMB-Verbindung noch gültig ist und startet bei Bedarf den Reconnect.
    public bool EnsureConnected(AppConfig config)
    {
        if (_client.IsConnected && _client.IsTreeConnected && _lastConfig != null)
        {
            if (_lastConfig.RemoteIP == config.RemoteIP &&
                _lastConfig.SambaUser == config.SambaUser &&
                _lastConfig.CatalogRemotePath == config.CatalogRemotePath)
            {
                try
                {
                    _client.ListShares(out NTStatus status);
                    Log.Debug($"SMB: ListShares-Status = {status}");

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        Log.Debug($"SMB: ListShares ungültig ({status}), starte Reset und Reconnect.");
                        NotifyTrayStatus("NoSamba");
                        ExecuteHardResetWithLogging("SMB: Vor dem Reconnect wird die Verbindung hart zurückgesetzt.");
                        Thread.Sleep(3000);
                        return TryConnectWithRetry(config);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Log.Debug($"SMB: ListShares fehlgeschlagen: {ex.Message}, starte Reset und Reconnect.");
                    NotifyTrayStatus("NoSamba");
                    ExecuteHardResetWithLogging("SMB: Vor dem Reconnect wird die Verbindung hart zurückgesetzt.");
                    Thread.Sleep(3000);
                    return TryConnectWithRetry(config);
                }
            }

            Log.Debug("SMB: Konfiguration geändert, verbinde neu.");
            NotifyTrayStatus("NoSamba");
            _client.TreeDisconnect();
            _client.Disconnect();
            Thread.Sleep(3000);
            return TryConnectWithRetry(config);
        }

        Log.Debug("SMB: Keine gültige Verbindung erkannt, starte Re-/connect.");
        NotifyTrayStatus("NoSamba");
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
                NotifyTrayStatus("Standby");
                return true;
            }

            if (attempt < MAX_CONNECT_RETRIES)
            {
                int delay = CONNECT_RETRY_DELAY_MS * attempt;
                Log.Debug($"SMB: Verbindung fehlgeschlagen, warte {delay}ms vor erneutem Versuch.");
                Thread.Sleep(delay);
            }
        }

        Log.Error($"SMB: Verbindung nach {MAX_CONNECT_RETRIES} Versuchen fehlgeschlagen.");
        return false;
    }

    private void ResetBeforeReconnectWithLogging()
    {
        try
        {
            _client.InvalidateSession();
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: ResetBeforeReconnect - InvalidateSession fehlgeschlagen: {ex.Message}");
        }

        try
        {
            _client.Disconnect();
        }
        catch (Exception ex)
        {
            Log.Debug($"SMB: ResetBeforeReconnect - Disconnect fehlgeschlagen: {ex.Message}");
        }
    }

    private bool TryConnect(AppConfig config)
    {
        string shareName = ExtractShareName(config.CatalogRemotePath);
        string serverIP = config.RemoteIP;

        Log.Debug($"SMB: Verbinden mit Server={serverIP}, Share={shareName}");
        ResetBeforeReconnectWithLogging();

        if (!_client.Connect(serverIP))
        {
            Log.Debug($"SMB: TCP-Verbindung zu {serverIP} fehlgeschlagen.");
            NotifyTrayStatus("NoSamba");
            return false;
        }

        if (!_client.Login(string.Empty, config.SambaUser, config.SambaPasswordAes))
        {
            Log.Debug($"SMB: Anmeldung als {config.SambaUser} fehlgeschlagen.");
            NotifyTrayStatus("NoSamba");
            _client.Disconnect();
            return false;
        }

        if (!_client.TreeConnect(shareName))
        {
            Log.Debug($"SMB: TreeConnect zu Freigabe '{shareName}' fehlgeschlagen.");
            NotifyTrayStatus("NoSamba");
            try
            {
                _client.Logoff();
            }
            catch (Exception ex)
            {
                Log.Debug($"SMB: TreeConnect-Fehler - Logoff ignoriert: {ex.Message}");
            }

            try
            {
                _client.InvalidateSession();
            }
            catch (Exception ex)
            {
                Log.Debug($"SMB: TreeConnect-Fehler - InvalidateSession fehlgeschlagen: {ex.Message}");
            }

            try
            {
                _client.Disconnect();
            }
            catch (Exception ex)
            {
                Log.Debug($"SMB: TreeConnect-Fehler - Disconnect fehlgeschlagen: {ex.Message}");
            }

            return false;
        }

        Log.Debug($"SMB: Verbindung zu {serverIP}/{shareName} hergestellt.");
        NotifyTrayStatus("Standby");
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
