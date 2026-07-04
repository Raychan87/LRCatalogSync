# Konzept: SMBConnectionManager (Hybrid-Verbindungsmodell)

## Ausgangslage

Das Projekt nutzt SMBLibrary fur Netzwerkfreigaben-Zugriff. Es gibt zwei Stellen, die SMB-Funktionalitat benotigen:

1. **LockManager**: Remote-Lock-Datei fur atomare Synchronisation
2. **CatalogManager**: ZIP-Backup-Erstellung auf Remote-Share

### Aktuelle Situation
- SMBLibrary-Funktionen (Connect, Login, TreeConnect, ReadFile, WriteFile, DeleteFile) sind in `src/Infrastructure/smb.cs` implementiert
- Werden aktuell NICHT verwendet - Projekt nutzt direkte UNC-Pfade + FileStream
- rclone fur Haupt-Sync zwischen lokal und NAS

### Konfigurationsintegration
Die SMB-Verbindung verwendet folgende Werte aus `Config.cs`:

| Symbol | Beschreibung | Beispiel |
|--------|--------------|----------|
| `#sym:RemoteIP` | IP-Adresse des Samba-Servers | `192.168.1.100` |
| `#sym:SambaUser` | Benutzername fur Authentifizierung | `admin` |
| `#sym:SambaPassword` | Passwort (verschlusselt mit rclone obscure) | `xxxxxx` |
| `#sym:CatalogRemotePath` | Remote-Pfad zum Lightroom Katalog-Ordner | `/Lightroom/Katalog/` |

---

## Architekturentscheidung: Hybrid-Modell

### Optionen-Vergleich

| Modell | Vorteile | Nachteile | Geeignet fur |
|--------|----------|-----------|--------------|
| **Singleton (einmalig)** | Schnellste Performance | Kein Reconnect bei Netzwerkfehler | Kurze Sessions, stabile Netze |
| **Per-Call (jedes Mal neu)** | Immer frische Verbindung | Langsam (Overhead pro Op) | Seltene Einzeloperationen |
| **Hybrid (Lazy + Reconnect)** | Balance aus Speed & Robustheit | Mittlere Komplexitat | Regelmaßige Ops mit Fehlerrecovery |

### Entscheidung: Hybrid-Modell

```
SMBConnectionManager (statisches Singleton)
├── Instance           → Lazy-Initialisierung
├── IsConnected        → Status-Flag
├── Connect()          → Session + TreeConnect (einmalig)
├── EnsureConnected()  → Auto-Reconnect wenn notig
├── ReadFile()
├── WriteFile()  
├── DeleteFile()
├── ListFiles()
└── Disconnect()       → Bei Bedarf / Programmende
```

---

## Komponenten-Design

### SMBConnectionManager-Klasse

```csharp
public sealed class SMBConnectionManager : IDisposable
{
    // Singleton-Zugriff
    public static SMBConnectionManager Instance { get; }
    
    // Verbindungsstatus
    public bool IsConnected { get; private set; }
    
    // Konfiguration
    public string Server { get; set; }
    public string Share { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    
    // Verbindung herstellen
    public bool Connect();
    
    // Stellt sicher dass verbunden ist (auto-reconnect)
    public bool EnsureConnected();
    
    // Datei-Operationen
    public byte[]? ReadFile(string relativePath);
    public bool WriteFile(string relativePath, byte[] data);
    public bool DeleteFile(string relativePath);
    public string[]? ListFiles(string relativePath = "");
    
    // Verbindung trennen
    public void Disconnect();
    
    // IDisposable
    public void Dispose();
}
```

### Connection-Lifecycle

```
Programmstart
     │
     ▼
+-----------------+
| Erstes Ensure.. | ◀--- Lazy: Verbindung beim ersten Zugriff
+--------+--------+
         │
         ▼
+-----------------+
|   Connect()     | ---▶ SMB.SessionConnect + Login + TreeConnect
+--------+--------+
         │
         ▼
+-----------------+
|  IsConnected=T  |
+--------+--------+
         │
         ▼
+-----------------+     +-----------------+
|  Datei-Op       | ... |  Datei-Op       | ◀--- Mehrere Ops ohne Reconnect
+-----------------+     +--------+--------+
                                 |
              +------------------+------------------+
              ▼                  ▼                  ▼
       +------------+     +------------+     +------------+
       | ReadFile() |     |WriteFile() |     |DeleteFile()|
       +------------+     +------------+     +------------+
                                 │
                                 ▼
                        +-----------------+
                        |Fehler/Timeout   | ---▶ Auto-Reconnect versuchen
                        +--------+--------+
                                 │
                                 ▼
                        +-----------------+
                        | Programmende    | ---▶ Disconnect() aufrufen
                        +-----------------+
```

### Reconnect-Strategie

```csharp
private const int MAX_RECONNECT_ATTEMPTS = 3;
private const int RECONNECT_DELAY_MS = 1000;

public bool EnsureConnected()
{
    if (IsConnected && _client.IsConnected)
        return true;
    
    // Versuche Reconnect mit exponential backoff
    for (int attempt = 1; attempt <= MAX_RECONNECT_ATTEMPTS; attempt++)
    {
        Log.Info($"SMBConnectionManager: Reconnect-Versuch {attempt}/{MAX_RECONNECT_ATTEMPTS}");
        
        Disconnect(); // Sauber trennen
        
        if (Connect())
            return true;
        
        Thread.Sleep(RECONNECT_DELAY_MS * attempt);
    }
    
    Log.Error("SMBConnectionManager: Reconnect fehlgeschlagen");
    return false;
}
```

---

## Integrationspunkte

### 1. LockManager-Integration

**Vorher (UNC-Pfad + FileStream):**
```csharp
_remoteLockStream = new FileStream(config.SyncRemoteLockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
```

**Nachher (SMBConnectionManager):**
```csharp
if (!SMBConnectionManager.Instance.EnsureConnected())
    return false;
    
byte[] lockData = Encoding.UTF8.GetBytes($"SyncGuid={SyncGuid}\nTimestamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
if (!SMBConnectionManager.Instance.WriteFile(remoteLockPath, lockData))
    return false;
```

### 2. CatalogManager-Integration (KRITISCH!)

**Grundsatz:**
- **Download**: ZIP wird lokal erstellt (Dateien sind bereits lokal)
- **Upload**: ZIP wird DIREKT auf dem Samba-Server erstellt (NIEMALS lokal!)
- Keine Übertragung eines fertigen Zips in irgendeine Richtung!

**Workflow bei Upload:**
```
1. SMBConnectionManager.EnsureConnected() → Verbindung zum NAS
2. Lokale Katalog-Dateien LESEN (via normaler FileStream)
3. ZIP in MemoryStream erstellen
4. SMBConnectionManager.WriteFile() → Gesamtes ZIP remote schreiben
5. Ergebnis: Kein lokales ZIP, keine doppelte Speicherung
```

**Umsetzung:**
```csharp
private static void CreateZipBackup(AppConfig config, SyncDirection direction)
{
    if (direction == SyncDirection.Upload)
    {
        // === UPLOAD: ZIP DIREKT auf NAS erstellen ===
        if (!SMBConnectionManager.Instance.EnsureConnected())
            return;
            
        using (var memStream = new MemoryStream())
        {
            using (var zip = new ZipArchive(memStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Lokale Katalog-Dateien lesen und in ZIP packen...
            }
            // Gesamtes ZIP als ByteArray remote schreiben
            string remoteBackupPath = Path.Combine(config.CatalogRemotePath, GlobalConst.BACKUP_FILENAME);
            SMBConnectionManager.Instance.WriteFile(remoteBackupPath, memStream.ToArray());
        }
    }
    else if (direction == SyncDirection.Download)
    {
        // === DOWNLOAD: ZIP lokal erstellen ===
        string backupPath = Path.Combine(config.CatalogLocalPath, GlobalConst.BACKUP_FILENAME);
        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            // Remote Katalog-Dateien via rclone holen und in ZIP packen...
        }
    }
}
```

**Wichtig:** Das ZIP wird im Speicher (MemoryStream) erstellt und dann als Ganzes via SMB geschrieben. Dadurch:
- Kein lokales ZIP-File bei Upload
- Keine doppelte Speicherung auf dem NAS
- Atomare Übertragung (ganz oder gar nicht)

---

## Dateistruktur

```
src/
├── Infrastructure/
│   └── Smb.cs                    # Alle SMB-Funktionen (SmbClient + SMBConnectionManager)
    
├── Core/
│   ├── LockManager.cs            # Anpassung: Nutzt SMB-Verbindung
│   └── CatalogManager.cs         # Anpassung: Nutzt SMB-Verbindung optional
    
ToDo/
└── Konzept-Sync.md               # Dieses Dokument
```

Hinweis: Alle SMB-Funktionen bleiben in `Smb.cs`. `SMBConnectionManager` ist nun eine eigenständige Klasse im gleichen Namespace.

---

## Sicherheitsaspekte

1. **Passwort-Speicherung**: Passworter NICHT im Klartext in Config speichern
2. **Credential-Sharing**: SMB-Verbindung soll AppConfig-Credentials verwenden (#sym:SambaUser, #sym:SambaPassword)
3. **Disconnect bei Exception**: Using-Statement oder try/finally garantiert Aufraumen

---

## Implementierungs-Schritte (Schritt fur Schritt)

### Step 1: Singleton-Struktur in smb.cs
- [x] Statische `Instance` Eigenschaft fur Singleton-Zugriff
- [x] `_isConnected` Flag und `_config` Referenz
- [x] Privater Konstruktor mit AppConfig-Parameter

### Step 2: Connect() mit Config-Integration
- [x] Verbindung zu `#sym:RemoteIP` herstellen
- [x] Authentifizierung mit `#sym:SambaUser` / `#sym:SambaPassword`
- [x] TreeConnect zu `#sym:CatalogRemotePath`

### Step 3: EnsureConnected() mit Auto-Reconnect
- [x] Prufung ob Verbindung aktiv
- [x] Reconnect-Logik mit exponential backoff
- [x] Max. 3 Versuche, 1s Wartezeit zwischen Versuchen

### Step 4: Disconnect() Methode
- [x] TreeDisconnect ausfuhren
- [x] Logoff/Disconnect vom Server
- [x] `_isConnected` auf false setzen

### Step 5: Datei-Operationen wrappern ✅ **ABGESCHLOSSEN**
- [x] `ReadFile(string relativePath)` - Wrapper um bestehendes ReadFile
- [x] `WriteFile(string relativePath, byte[] data)` - Wrapper um bestehendes WriteFile
- [x] `DeleteFile(string relativePath)` - Wrapper um bestehendes DeleteFile
- [x] `ListFiles(string relativePath = "")` - Wrapper um bestehendes ListFiles
- [x] `SMBConnectionManager` als eigenständige Klasse aus `SmbClient` ausgelagert

### Step 6: LockManager refaktorieren ✅ **ABGESCHLOSSEN**
- [x] UNC-Pfad/FileStream durch SMBConnectionManager ersetzt
- [x] `AcquireLocks()` nutzt SMBConnectionManager.Instance.EnsureConnected()
- [x] Remote Lock via WriteFile statt FileStream

### Step 7: Weitere Remote-Zugriffe identifizieren ✅ **ABGESCHLOSSEN**
- [x] BackupManager.cs auf Remote-Zugriffe geprüft → OK (nutzt rclone)
- [x] Andere CS-Dateien auf direkte File-Zugriffe mit Remote-Pfaden geprüft
- [x] Problem in CatalogManager.cs identifiziert → Upload verwendet ZipFile.Open() auf UNC-Pfad (→ Step 9)

### Step 8: Testen mit realer Netzwerkfreigabe
- [ ] Verbindung zu Samba-Server testen
- [ ] Lock akquirieren/entfernen testen
- [ ] Reconnect bei Verbindungsverlust testen

### Step 9: CatalogManager SMB-Integration (PFLICHT!) ✅ **ABGESCHLOSSEN**
- [x] CreateZipBackup() differenziert angepasst:
  - Upload: MemoryStream + SMBConnectionManager.WriteFile() → ZIP direkt remote
  - Download: ZipFile.Open() lokal (bleibt wie bisher)
- [x] EnsureConnected() vor Upload-ZIP aufgerufen
- [x] GetRelativePath() Hilfsmethode hinzugefügt