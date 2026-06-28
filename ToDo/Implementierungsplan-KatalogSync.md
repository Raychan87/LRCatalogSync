# 🛠️ Implementierungsplan: Katalog-Synchronisation (CatalogManager)

> **Erstellt:** 2026-06-28
> **Bezug:** [Konzept-KatalogSync.md](Konzept-KatalogSync.md)

---

## 📊 Ist-Stand-Analyse

### Was bereits existiert:

| Datei | Status | Bemerkung |
|-------|--------|-----------|
| [src/Program.cs](../src/Program.cs) | ✅ Vorhanden | Einstiegspunkt `Application.Run(new LRCatSync())` |
| [src/Core/LRCatSync.cs](../src/Core/LRCatSync.cs) | ⚠️ Teilvorhanden | Hauptklasse mit Timer – ruft aktuell **direkt** `BackupManager.RunBackupProcess()` auf. Muss auf Coordinator umgestellt werden. |
| [src/Core/BackupManager.cs](../src/Core/BackupManager.cs) | ✅ Vorhanden | Backup-Logik mit `rclone bisync`. Voll funktionsfähig. |
| [src/UI/TrayManager.cs](../src/UI/TrayManager.cs) | ✅ Vorhanden | TrayIcons grün/gelb/rot/blau/weiß bereits integriert |
| [src/UI/SettingsForm.cs](../src/UI/SettingsForm.cs) | ⚠️ Anpassung nötig | Feldnamen müssen nach Config-Umbenennung aktualisiert werden |
| [src/Infrastructure/Config.cs](../src/Infrastructure/Config.cs) | ⚠️ Anpassung nötig | Felder müssen umbenannt werden (`LocalPath` → `CatalogLocalPath`) |

### Was laut Konzept FEHLT:

| Komponente | Status | Abhängigkeit |
|------------|--------|--------------|
| `CatalogManager.cs` | ❌ Komplett fehlend | – |
| `Coordinator.cs` | ❌ Komplett fehlend | CatalogManager |
| `SYNC_LOCK_TIMEOUT_MIN` in GlobalConst | ❌ Fehlend | – |
| Config-Feld-Umbenennung | ❌ Offen | – |
| `IsBackupInsideCatalogPath()` Helper | ❌ Fehlend | Config-Umbenennung |
| Coordinator-Integration in LRCatSync.cs | ❌ Offen | Coordinator |
| Crash-Recovery (Startup-Lock-Cleanup) | ❌ Fehlend | CatalogManager |

---

## 🗺️ Architektur-Zielbild

```mermaid
flowchart TD
    A[Program.cs<br/>Application.Run] --> B[LRCatSync.cs<br/>ApplicationContext]
    B --> C[Coordinator]
    C -->|"1. Sequenziell"| D[BackupManager<br/>rclone bisync]
    C -->|"2. Nach Backup"| E[CatalogManager<br/>rclone sync]
    
    D --> D2[(BackupsLocalPath<br/>→ NAS)]
    E --> E2[(CatalogLocalPath<br/>→ NAS)]
    
    B -.->|"Status Updates"| F[TrayManager<br/>🟢🟡🔴🔵⚪]
    
    subgraph "Neue Komponenten"
        C[Coordinator]
        E[CatalogManager]
        G[LockManager<br/>neu]
    end
    
    subgraph "Bestehende Komponenten"
        B[LRCatSync]
        D[BackupManager]
        F[TrayManager]
    end
```

---

## 📋 Implementierungsphasen

Die Umsetzung erfolgt in **6 Phasen**, die streng sequenziell abgearbeitet werden sollten.

---

### Phase 1: Fundament – Config & GlobalData erweitern

**Ziel:** Namenskonvention an Konzept anpassen und globale Konstante ergänzen.

#### 1.1 GlobalData.cs erweitern

In [src/GlobalData.cs](../src/GlobalData.cs) folgende Konstante hinzufügen:

```csharp
public static class GlobalConst
{
    public const int BACKUP_CHECK_INTERVAL = 10;        // bestehend
    public const int SYNC_LOCK_TIMEOUT_MIN = 30;        // NEU: Stale-Lock Timeout
    
    // NEU: Heartbeat-Intervall für Lock-Aktualisierung
    public const int HEARTBEAT_INTERVAL_SEC = 150;      // 2,5 Minuten
    
    // NEU: Katalog-Sync Intervall (sekunden)
    public const int CATALOG_SYNC_CHECK_INTERVAL = 30;  // Häufigkeit der Prüfzyklen
}
```

#### 1.2 Config-Felder umbenennen

In [src/Infrastructure/Config.cs](../src/Infrastructure/Config.cs):

| Alt | Neu | Grund |
|-----|-----|-------|
| `LocalPath` | `CatalogLocalPath` | Konzept-Vorgabe §6 |
| `RemotePath` | `CatalogRemotePath` | Konzept-Vorgabe §6 |

⚠️ **WICHTIG:** Alle Referenzen in [SettingsForm.cs](../src/UI/SettingsForm.cs), [LRCatSync.cs](../src/Core/LRCatSync.cs) und [BackupManager.cs](../src/Core/BackupManager.cs) müssen aktualisiert werden!

#### 1.3 IsBackupInsideCatalogPath() Helper hinzufügen

In [Config.cs](../src/Infrastructure/Config.cs) neue Methode ergänzen:

```csharp
public bool IsBackupInsideCatalogPath()
{
    if (string.IsNullOrEmpty(BackupsLocalPath) || 
        string.IsNullOrEmpty(CatalogLocalPath))
        return false;
    
    string backupNormalized = Path.GetFullPath(BackupsLocalPath).ToLower();
    string catalogNormalized = Path.GetFullPath(CatalogLocalPath).ToLower();
    
    return backupNormalized.StartsWith(catalogNormalized);
}

public string GetRelativeBackupExcludePattern()
{
    if (!IsBackupInsideCatalogPath())
        return null;
    
    string relativeBackupPath = Path.GetRelativePath(CatalogLocalPath, BackupsLocalPath);
    return $"--exclude \"{relativeBackupPath}/**\"";
}
```

#### 1.4 SettingsForm anpassen

Feldumbenennungen in [SettingsForm.cs](../src/UI/SettingsForm.cs) nachziehen:
- `config.LocalPath` → `config.CatalogLocalPath`
- `config.RemotePath` → `config.CatalogRemotePath`

---

### Phase 2: LockManager – Atomare Lock-Akquise

**Ziel:** Eigenständige Klasse für atomares Locking lokal + remote.

#### Neue Datei: `src/Core/LockManager.cs`

```mermaid
flowchart LR
    A[AcquireLocks] --> B{Lokale Lock<br/>erstellbar?}
    B -->|Ja| C{Remote Lock<br/>erstellbar?<br/>Stale?}
    B -->|Nein| D[❌ Sync abbrechen]
    C -->|Ja| E[✅ Beide Locks aktiv]
    C -->|Nein/Stale| F[Stale Lock<br/>überschreiben]
    F --> E
    C -->|Aktiv&lt;30min| G[❌ Anderer Client<br/>aktiv]
```

**Verantwortlichkeiten:**
- Atomare Akquise von `LRCatSync.lock` lokal + remote (`FileShare.None`)
- Heartbeat-Thread starten/stoppen (alle ~2,5 min Timestamp aktualisieren)
- Stale-Lock-Erkennung (>30 min → überschreiben)
- Cleanup im `finally`-Block

**API-Skizze:**

```csharp
public class LockManager : IDisposable
{
    // Erzeugt pro Sync neue GUID für Tracking
    public string SyncGuid { get; }
    
    // Atomare Lock-Akquise lokal + remote
    public bool AcquireLocks(AppConfig config);
    
    // Heartbeat starten (alle HEARTBEAT_INTERVAL_SEC Sekunden)
    public void StartHeartbeat(CancellationTokenSource cts);
    
    // Sauberes Cleanup – IMMER im finally!
    public void ReleaseLocks();
    
    public void Dispose();
}
```

---

### Phase 3: CatalogManager – Kernkomponente

**Ziel:** Die eigentliche Katalog-Synchronisation gemäß Konzept §5.

#### Neue Datei: `src/Core/CatalogManager.cs`

```mermaid
flowchart TD
    Start([Start]) --> P0[Phase 0:<br/>Lock-Erkennung]
    P0 -->|"*.lrcat-lock/*.shm/*.wal<br/>vorhanden?"| P0Yes[🔵 Tray Blau<br/>Warten]
    P0Yes --> Wait[Warte Zyklus ab]
    Wait --> Start
    
    P0 -->|"Keine Locks"| P1[Phase 1:<br/>Versionsvergleich]
    P1 --> P1Check[rclone check<br/>lokal vs remote]
    P1Check -->|"identisch"| Ende([Ende:<br/>Kein Sync nötig])
    P1Check -->|"Unterschied"| P2[Phase 2:<br/>Lock akquirieren]
    
    P2 --> P2Lock[LRCatSync.lock lokal+remote<br/>atomar erstellen]
    P2Lock --> P3[Phase 3:<br/>ZIP Backup]
    
    P3 --> P3Zip[LRCatSync_last_katalog.zip<br/>auf NAS erstellen]
    P3Zip --> P4[Phase 4:<br/>rclone sync]
    
    P4 --> P4Sync["rclone sync lokal→NAS<br/>--exclude *.lrcat.lock/shm/wal"]
    P4 --> P4Prev["Separater Sync:<br/>Previews.lrdata<br/>if SyncPreviewData=true"]
    
    P4Sync --> P5[Phase 5:<br/>Cleanup]
    P4Prev --> P5
    
    P5 --> P5Clean[Lokale+Remote Locks löschen<br/>Heartbeat stoppen]
    P5Clean --> Ende2([Ende:<br/>Sync erfolgreich])
```

#### Methodenstruktur:

```csharp
public static class CatalogManager
{
    // Hauptmethode – wird vom Coordinator aufgerufen
    public static void RunCatalogSync(AppConfig config, TrayManager trayManager)
    {
        // Phase 0: Prüfe ob Lightroom läuft (.lrcat.lock vorhanden?)
        // Phase 1: rclone check – Versionsvergleich lokal vs remote
        // Phase 2: Lock akquirieren (atomar via LockManager)
        // Phase 3: ZIP Backup erstellen auf NAS  
        // Phase 4: rclone sync ausführen (--exclude Patterns)
        // Phase 5: Cleanup (IMMER im finally!)
        
        // WICHTIG: try-finally für sichere Lock-Freigabe!
    }
}
```

#### Excludes für rclone sync:

```
--exclude "*.lrcat.lock"
--exclude "*.lrcat-shm"
--exclude "*.lrcat-wal"
--exclude "LRCatSync_last_katalog.zip"
--exclude "[relativer Backup-Pfad]/**"   ← dynamisch via GetRelativeBackupExcludePattern()
```

---

### Phase 4: Coordinator – Sequenzielle Ablaufsteuerung

**Ziel:** Sicherstellen dass Backup und Catalog NACHEINANDER laufen.

#### Neue Datei: `src/Core/Coordinator.cs`

```mermaid
flowchart LR
    A[Zyklus Start] --> B[Sperre prüfen<br/>isRunning?]
    B -->|"ja"| C[Warte Zyklus ab]
    B -->|"nein"| D[BackupManager<br/>.RunBackupProcess]
    D --> E[Katalog-Sync?<br/>nur wenn Änderungen]
    E --> F[Fertig – nächste<br/>Iteration warten]
```

#### Integration in LRCatSync:

In [LRCatSync.cs](../src/Core/LRCatSync.cs) muss der direkte Aufruf von `BackupManager.RunBackupProcess()` durch den Coordinator ersetzt werden:

```csharp
// ALT:
private System.Threading.Timer backupTimer;
// ...
BackupManager.RunBackupProcess(config, trayManager);

// NEU:
private System.Threading.Timer syncCycleTimer;
private readonly object cycleLock = new object();
private bool isCycleRunning = false;

// Im Constructor:
syncCycleTimer = new System.Threading.Timer(
    SyncCycleCallback, null, 0, GlobalConst.CATALOG_SYNC_CHECK_INTERVAL * 1000);
```

---

### Phase 5: Crash-Recovery beim Programmstart

**Ziel:** Verwaiste Locks beim Programmstart erkennen und entfernen.

In [LRCatSync.cs](../src/Core/LRCatSync.cs) Constructor ergänzen:

```csharp
// VOR Timer-Start:
CleanupStaleLocks(config);
```

Logik:
1. Prüfe ob `[CatalogLocalPath]/LRCatSync.lock` existiert
2. Wenn ja + älter als `SYNC_LOCK_TIMEOUT_MIN` → löschen (Crash-Recovery)
3. Wenn ja + jünger → WARNUNG: Anderer Client aktiv?

---

### Phase 6: TrayIcon-Erweiterung & Finalisierung

**Ziel:** TrayIcon-Zustände korrekt setzen.

#### Status-Mapping:

| Zustand | TrayIcon-Farbe | Quelle |
|---------|---------------|--------|
| 🟢 Standby (kein Sync aktiv) | Grün | Coordinator |
| 🟡 Sync läuft (Phase 4) | Gelb | CatalogManager / BackupManager |
| 🔴 Fehler / rclone Error | Rot | Catch-Block |
| 🔵 Lightroom läuft (Lock erkannt) | Blau | Phase 0 Erkennung |
| ⚪ Keine Samba-Verbindung | Weiß | Connection Check |

#### Anpassungen an TrayManager:

Die bestehenden Icons in [TrayManager.cs](../src/UI/TrayManager.cs) sind bereits vorhanden.
Lediglich die Status-Zustände müssen korrekt gesetzt werden:

```csharp
trayManager.UpdateStatus("Standby");   // 🟢 Standardzustand  
trayManager.UpdateStatus("Syncing");   // 🟡 Während rclone sync  
trayManager.UpdateStatus("Error");     // 🔴 Bei Fehlern  
trayManager.UpdateStatus("Lockfile");  // 🔵 Lightroom läuft  
trayManager.UpdateStatus("NoSamba");   // ⚪ Keine Verbindung  
```

---

## 📋 Abarbeitungs-Reihenfolge

Die Phasen sollten STRENG SEQUENZIELL abgearbeitet werden:

```mermaid
flowchart LR
    P1[Phase 1:<br/>Config &<br/>GlobalData] --> P2[Phase 2:<br/>LockManager]
    P2 --> P3[Phase 3:<br/>CatalogManager<br/>Kernlogik]
    P3 --> P4[Phase 4:<br/>Coordinator<br/>Integration]
    P4 --> P5[Phase 5:<br/>Crash-Recovery<br/>beim Startup]
    P5 --> P6[Phase 6:<br/>Finalisierung<br/>& Tests]
```

### Empfohlene Reihenfolge:

1. **Phase 1:** Config-Umbenennung & GlobalConst erweitern *(Fundament)*
2. **Phase 2:** LockManager-Klasse erstellen *(Voraussetzung für Phase 3)*
3. **Phase 3:** CatalogManager-Klasse implementieren *(Hauptarbeit)*
4. **Phase 4:** Coordinator erstellen & in LRCatSync integrieren *(Verknüpfung)*
5. **Phase 5:** Crash-Recovery beim Programmstart *(Sicherheit)*
6. **Phase 6:** TrayIcon-Zustände & abschließende Tests *(Finalisierung)*

---

## ✅ Konkrete Aufgaben-Checkliste

### Phase 1: Fundament – Config & GlobalData

- [ ] `SYNC_LOCK_TIMEOUT_MIN` Konstante in [GlobalData.cs](../src/GlobalData.cs) ergänzen
- [ ] `CATALOG_SYNC_CHECK_INTERVAL` Konstante ergänzen
- [ ] Config-Felder umbenennen: `LocalPath` → `CatalogLocalPath`
- [ ] Config-Felder umbenennen: `RemotePath` → `CatalogRemotePath`
- [ ] `IsBackupInsideCatalogPath()` Helper-Methode in [Config.cs](../src/Infrastructure/Config.cs) ergänzen
- [ ] `GetRelativeBackupExcludePattern()` Helper-Methode ergänzen
- [ ] Referenzen in [SettingsForm.cs](../src/UI/SettingsForm.cs) aktualisieren (`config.LocalPath` → `config.CatalogLocalPath`)
- [ ] Referenzen in [LRCatSync.cs](../src/Core/LRCatSync.cs) aktualisieren
- [ ] Build testen → ✅ kompiliert ohne Fehler

### Phase 2: LockManager

- [ ] Neue Datei `src/Core/LockManager.cs` erstellen
- [ ] `SyncGuid` Property (pro Sync-Durchlauf neue GUID)
- [ ] `AcquireLocks(config)` – atomare Lock-Akquise lokal + remote
- [ ] Stale-Lock-Erkennung (>30 min → überschreiben)
- [ ] `StartHeartbeat(cts)` – Thread für regelmäßige Aktualisierung
- [ ] `ReleaseLocks()` – Cleanup im finally-Block
- [ ] `IDisposable` implementieren
- [ ] Build testen → ✅ kompiliert ohne Fehler

### Phase 3: CatalogManager (Hauptarbeit)

- [ ] Neue Datei `src/Core/CatalogManager.cs` erstellen
- [ ] **Phase 0:** Lightroom-Lock-Erkennung (`.lrcat.lock`, `.lrcat-shm`, `.lrcat-wal`)
- [ ] **Phase 1:** `rclone check` Versionsvergleich (lokal vs remote)
- [ ] **Phase 1:** Entscheidung Upload/Download/kein Sync
- [ ] **Phase 2:** Lock-Akquise via LockManager (atomar)
- [ ] **Phase 2:** `.lrcat.lock` erstellen (Lightroom blockieren)
- [ ] **Phase 3:** ZIP-Backup auf NAS erstellen (`LRCatSync_last_katalog.zip`)
- [ ] **Phase 4:** rclone sync mit Excludes ausführen
- [ ] **Phase 4:** Separater Previews-Sync (wenn `SyncPreviewData=true`)
- [ ] **Phase 5:** Cleanup (IMMER im finally!)
- [ ] Heartbeat stoppen im finally
- [ ] Logging via `Log.Debug/Info/Error`
- [ ] Build testen → ✅ kompiliert ohne Fehler

### Phase 4: Coordinator

- [ ] Neue Datei `src/Core/Coordinator.cs` erstellen
- [ ] Sequenzielle Ausführung Backup → Catalog sicherstellen
- [ ] `cycleLock` gegen parallele Ausführung
- [ ] Timer in [LRCatSync.cs](../src/Core/LRCatSync.cs) auf Coordinator umstellen
- [ ] Alte direkte `BackupManager.RunBackupProcess()` Aufrufe ersetzen
- [ ] Build testen → ✅ kompiliert ohne Fehler

### Phase 5: Crash-Recovery

- [ ] `CleanupStaleLocks(config)` in LRCatSync Constructor
- [ ] Beim Programmstart prüfen ob `LRCatSync.lock` existiert
- [ ] Stale Locks (>30 min) automatisch entfernen
- [ ] Log-Eintrag bei Crash-Recovery
- [ ] Build testen → ✅ kompiliert ohne Fehler

### Phase 6: Finalisierung & Tests

- [ ] TrayIcon-Zustände korrekt setzen (Blau/Gelb/Rot/Grün/Weiß)
- [ ] End-to-End Test: Lightroom offen → 🔵 Blau
- [ ] End-to-End Test: Sync läuft → 🟡 Gelb
- [ ] End-to-End Test: Sync erfolgreich → 🟢 Grün
- [ ] End-to-End Test: Keine Samba-Verbindung → ⚪ Weiß
- [ ] End-to-End Test: Fehlerfall → 🔴 Rot
- [ ] Build testen → ✅ kompiliert ohne Fehler
- [ ] Manueller Funktionstest mit echtem Lightroom-Katalog

---

### Try-Finally Pflicht!

Alle Lock-operationen MÜSSEN in try-finally Blöcken stehen – auch bei Fehlern!

```csharp
try 
{
    // Phase 2+: Lock akquirieren + Sync ausführen  
} 
finally 
{
    // IMMER ausführen – selbst bei Exceptions!
    lockManager.ReleaseLocks();
}
```

### Sequenzielle Ausführung sicherstellen!

Coordinator muss garantieren dass nicht parallel gesynct wird:

```csharp
private readonly object cycleLock = new object();
private bool isCycleRunning = false;
```

### Renaming-Konsistenz!

Nach Umbenennung von Config-Feldern ALLE Referenzen aktualisieren:
- [LRCatSync.cs](../src/Core/LRCatSync.cs)
- [SettingsForm.cs](../src/UI/SettingsForm.cs)
- [BackupManager.cs](../src/Core/BackupManager.cs)

---
