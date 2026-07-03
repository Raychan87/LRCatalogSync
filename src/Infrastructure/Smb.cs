// SMBLibrary from https://github.com/TalAloni/SMBLibrary
// Verwaltung von SMB-Verbindungen f�r Remote-Dateizugriff
// Step	Funktion	Status
// 1	Connect/Disconnect	✅ Implementiert
// 2	Login/Logoff	✅ Implementiert
// 3	TreeConnect/TreeDisconnect	✅ Implementiert
// 4	ListFiles (Dateiauflistung)	✅ Implementiert
// 5a	ReadFile (Datei lesen)	✅ Implementiert
// 5b	WriteFile (Datei schreiben)	✅ Implementiert
// 6	DeleteFile (Datei löschen)	✅ Implementiert

using SMBLibrary;
using SMBLibrary.Client;

namespace LRCatalogSync.Infrastructure;

/// <summary>
/// SMB-Client f�r den Zugriff auf Remote-Freigaben
/// </summary>
public class SmbClient
{
    private SMB2Client _client = new SMB2Client();
    private bool _isConnected = false;
    private ISMBFileStore? _fileStore = null;
    private bool _isTreeConnected = false;

    /// <summary>
    /// Verbindet mit einem SMB-Server
    /// </summary>
    /// <param name="hostnameOrIp">Hostname oder IP-Adresse des Servers</param>
    /// <param name="transportType">Transporttyp (Standard: DirectTCPTransport f�r SMB2/3)</param>
    /// <returns>true bei erfolgreicher Verbindung, sonst false</returns>
    public bool Connect(string hostnameOrIp, SMBTransportType transportType = SMBTransportType.DirectTCPTransport)
    {
        // Verbindung herstellen
        _isConnected = _client.Connect(hostnameOrIp, transportType);
        
        return _isConnected;
    }

    /// <summary>
    /// Pr�ft, ob eine Verbindung zum Server besteht
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Trennt die Verbindung zum SMB-Server
    /// </summary>
    public void Disconnect()
    {
        if (_isTreeConnected)
        {
            _fileStore?.Disconnect();
            _isTreeConnected = false;
        }
        
        if (_isConnected)
        {
            _client.Disconnect();
            _isConnected = false;
        }
    }

    /// <summary>
    /// Authentifiziert beim SMB-Server
    /// </summary>
    /// <param name="domain">Dom�ne (leer f�r lokale Benutzer oder Workgroup)</param>
    /// <param name="username">Benutzername</param>
    /// <param name="password">Passwort</param>
    /// <returns>true bei erfolgreicher Authentifizierung, sonst false</returns>
    public bool Login(string domain, string username, string password)
    {
        if (!_isConnected)
        {
            return false;
        }

        // Authentifizieren
        NTStatus status = _client.Login(domain, username, password);
        
        return status == NTStatus.STATUS_SUCCESS;
    }

    /// <summary>
    /// Meldet den Benutzer vom Server ab
    /// </summary>
    public void Logoff()
    {
        if (_isConnected)
        {
            _client.Logoff();
        }
    }

    /// <summary>
    /// Verbindet mit einer SMB-Freigabe (Tree Connect)
    /// </summary>
    /// <param name="shareName">Name der Freigabe (z.B. "Bilder" f�r \\server\Bilder)</param>
    /// <returns>true bei erfolgreicher Verbindung, sonst false</returns>
    public bool TreeConnect(string shareName)
    {
        if (!_isConnected)
        {
            return false;
        }

        // Mit Freigabe verbinden
        NTStatus status;
        _fileStore = _client.TreeConnect(shareName, out status);
        
        _isTreeConnected = (status == NTStatus.STATUS_SUCCESS);
        
        return _isTreeConnected;
    }

    /// <summary>
    /// Pr�ft, ob eine Verbindung mit einer Freigabe besteht
    /// </summary>
    public bool IsTreeConnected => _isTreeConnected;

    /// <summary>
    /// Trennt die Verbindung zur Freigabe (Tree Disconnect)
    /// </summary>
    public void TreeDisconnect()
    {
        if (_isTreeConnected)
        {
            _fileStore?.Disconnect();
            _isTreeConnected = false;
        }
    }

    /// <summary>
    /// Listet Dateien und Verzeichnisse im Root-Verzeichnis auf
    /// </summary>
    /// <returns>Liste der Datei- und Verzeichnisnamen</returns>
    public List<string> ListFiles()
    {
        return ListFiles(string.Empty);
    }

    /// <summary>
    /// Listet Dateien und Verzeichnisse in einem Verzeichnis auf
    /// </summary>
    /// <param name="directoryPath">Pfad zum Verzeichnis (leer f�r Root)</param>
    /// <returns>Liste der Datei- und Verzeichnisnamen</returns>
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

            // Verzeichnis �ffnen
            // F�r SMB1 muss der Pfad mit \\ beginnen, SMB2 verwendet leere Zeichenkette
            string searchPath = directoryPath;
            if (_fileStore is SMB1FileStore)
            {
                searchPath = "\\" + searchPath;
            }

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
                        // . und .. Eintr�ge �berspringen
                        if (fileInfo.FileName != "." && fileInfo.FileName != "..")
                        {
                            fileList.Add(fileInfo.FileName);
                        }
                    }
                }

                // Handle schlie�en
                _fileStore.CloseFile(directoryHandle);
            }
        }
        catch
        {
            // Fehler ignorieren und leere Liste zur�ckgeben
        }

        return fileList;
    }

    /// <summary>
    /// Liest eine komplette Datei vom Remote-Server
    /// </summary>
    /// <param name="filePath">Pfad zur Datei (relativ zur Freigabe)</param>
    /// <returns>Dateiinhalt als Byte-Array, oder null bei Fehler</returns>
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

            // F�r SMB1 muss der Pfad mit \\ beginnen, SMB2 verwendet leere Zeichenkette
            string remotePath = filePath;
            if (_fileStore is SMB1FileStore)
            {
                remotePath = "\\" + remotePath;
            }

            // Datei �ffnen
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

            // Dateigr��e ermitteln
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
            // Fehler ignorieren und null zur�ckgeben
            return null;
        }
    }

    /// <summary>
    /// Liest einen Teilbereich einer Datei vom Remote-Server
    /// </summary>
    /// <param name="filePath">Pfad zur Datei (relativ zur Freigabe)</param>
    /// <param name="offset">Startposition im Byte</param>
    /// <param name="length">Anzahl der zu lesenden Bytes</param>
    /// <returns>Byte-Array mit den gelesenen Daten, oder null bei Fehler</returns>
    public byte[]? ReadFilePartial(string filePath, long offset, int length)
    {
        if (!_isTreeConnected || _fileStore == null)
        {
            return null;
        }

        try
        {
            object fileHandle;
            FileStatus fileStatus;

            // F�r SMB1 muss der Pfad mit \\ beginnen, SMB2 verwendet leere Zeichenkette
            string remotePath = filePath;
            if (_fileStore is SMB1FileStore)
            {
                remotePath = "\\" + remotePath;
            }

            // Datei �ffnen
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

            // Daten lesen
            byte[]? data;
            status = _fileStore.ReadFile(out data, fileHandle, offset, length);

            _fileStore.CloseFile(fileHandle);

            if (status == NTStatus.STATUS_SUCCESS || status == NTStatus.STATUS_END_OF_FILE)
            {
                return data;
            }

            return null;
        }
        catch
        {
            // Fehler ignorieren und null zur�ckgeben
            return null;
        }
    }
    /// </summary>
    /// <param name="filePath">Pfad zur Datei (relativ zur Freigabe)</param>
    /// <param name="data">Zu schreibende Daten als Byte-Array</param>
    /// <returns>true bei erfolgreichem Schreibvorgang, sonst false</returns>
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

            // F�r SMB1 muss der Pfad mit \\ beginnen, SMB2 verwendet leere Zeichenkette
            string remotePath = filePath;
            if (_fileStore is SMB1FileStore)
            {
                remotePath = "\\" + remotePath;
            }

            // Datei �ffnen oder erstellen
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

    // L�scht eine Datei auf dem Remote-Server
    // filePath: Pfad zur Datei (relativ zur Freigabe)
    // Rueckgabe: true bei erfolgreichem L�schen, sonst false
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

            // F�r SMB1 muss der Pfad mit \\ beginnen, SMB2 verwendet leere Zeichenkette
            string remotePath = filePath;
            if (_fileStore is SMB1FileStore)
            {
                remotePath = "\\" + remotePath;
            }

            // Datei mit L�schmodus �ffnen
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

            // Datei als gel�scht markieren
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
            // Fehler ignorieren und false zur�ckgeben
            return false;
        }
    }
}

// ============================================================
// SMBConnectionManager - Singleton für zentrale SMB-Verbindungsverwaltung
// Schritt 1 der SMB-Integration
// ============================================================

/// <summary>
/// Singleton-Klasse für die zentrale Verwaltung einer SMB-Verbindung
/// Stellt sicher, dass nur eine einzige SMB-Verbindung gleichzeitig existiert
/// </summary>
public sealed class SMBConnectionManager
{
    private static readonly Lazy<SMBConnectionManager> _instance = 
        new Lazy<SMBConnectionManager>(() => new SMBConnectionManager());
    
    public static SMBConnectionManager Instance => _instance.Value;
    
    private SmbClient _client = new SmbClient();
    private AppConfig? _lastConfig = null;
    private bool _ownsConnection = false;
    
    private SMBConnectionManager() { }
    
    /// <summary>
    /// Stellt sicher, dass eine aktive SMB-Verbindung besteht
    /// </summary>
    /// <param name="config">AppConfig mit RemoteIP, SambaUser, SambaPassword, CatalogRemotePath</param>
    /// <returns>true wenn verbunden, sonst false</returns>
    public bool EnsureConnected(AppConfig config)
    {
        // Prüfe ob bereits verbunden mit gleichen Parametern
        if (_client.IsConnected && _client.IsTreeConnected && _lastConfig != null)
        {
            if (_lastConfig.RemoteIP == config.RemoteIP && 
                _lastConfig.SambaUser == config.SambaUser &&
                _lastConfig.CatalogRemotePath == config.CatalogRemotePath)
            {
                return true; // Bereits verbunden
            }
            // Parameter unterschiedlich -> neu verbinden
            Disconnect();
        }
        
        // Extrahiere Share-Name aus CatalogRemotePath (z.B. "\\NAS\Freigabe\subdir" -> "Freigabe")
        string shareName = ExtractShareName(config.CatalogRemotePath);
        string serverIP = config.RemoteIP;
        
        // Verbinde mit Server
        if (!_client.Connect(serverIP))
        {
            Log.Error($"[SMB] Verbindung zum Server {serverIP} fehlgeschlagen");
            return false;
        }
        
        // Anmelden
        if (!_client.Login(string.Empty, config.SambaUser, config.SambaPassword))
        {
            Log.Error($"[SMB] Anmeldung als {config.SambaUser} fehlgeschlagen");
            _client.Disconnect();
            return false;
        }
        
        // Mit Freigabe verbinden
        if (!_client.TreeConnect(shareName))
        {
            Log.Error($"[SMB] TreeConnect zu Freigabe '{shareName}' fehlgeschlagen");
            _client.Logoff();
            _client.Disconnect();
            return false;
        }
        
        _lastConfig = config;
        _ownsConnection = true;
        Log.Info($"[SMB] Verbunden mit {serverIP}/{shareName}");
        return true;
    }
    
    /// <summary>
    /// Extrahiert den Share-Namen aus einem UNC-Pfad
    /// </summary>
    private string ExtractShareName(string uncPath)
    {
        // Erwartet: \\Server\Share\subdir oder \\Server\Share
        if (uncPath.StartsWith(@"\\"))
        {
            var parts = uncPath.Substring(2).Split('\\');
            if (parts.Length >= 1)
            {
                return parts[0]; // Share-Name ist erster Teil nach \\
            }
        }
        return uncPath; // Fallback
    }
    
    /// <summary>
    /// Prüft ob aktuell verbunden
    /// </summary>
    public bool IsConnected => _client.IsConnected && _client.IsTreeConnected;
    
    /// <summary>
    /// Trennt die SMB-Verbindung
    /// </summary>
    public void Disconnect()
    {
        if (_ownsConnection)
        {
            if (_client.IsTreeConnected)
                _client.TreeDisconnect();
            if (_client.IsConnected)
            {
                _client.Logoff();
                _client.Disconnect();
            }
            _ownsConnection = false;
            _lastConfig = null;
            Log.Info("[SMB] Verbindung getrennt");
        }
    }
    
    // Wrapper für Dateioperationen (Step 5)
    
    public byte[]? ReadFile(string relativePath) => _client.ReadFile(relativePath);
    
    public bool WriteFile(string relativePath, byte[]? data) => _client.WriteFile(relativePath, data);
    
    public bool DeleteFile(string relativePath) => _client.DeleteFile(relativePath);
    
    public List<string> ListFiles(string relativePath) => _client.ListFiles(relativePath);
}
