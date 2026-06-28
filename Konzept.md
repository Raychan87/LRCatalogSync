# 📘 Konzept: Lightroom-Katalog-Synchronisation (Multi-Client → NAS)

---

## 1️⃣ Lightroom Lock-Dateien (SQLite-Spezifikation)

Wenn Lightroom einen Katalog öffnet, erstellt es **drei SQLite-Lock-Dateien**:

```
📁 [Katalogname]/
├── 📄 [Katalogname].lrcat           ← SQLite-Datenbank
├── 📄 [Katalogname].lrcat.lock      ← Lock-Datei (Haupt-Lock)
├── 📄 [Katalogname].lrcat-shm       ← temporäre Shared Memory
└── 📄 [Katalogname].lrcat-wal       ← Journal-Datei um unvollständige Datensätze im Katalog zu verwalten
```

### Beispiel Inhalt der .lrcat.lock die von Lightroom erzeugt wird:
```
C:\Program Files\Adobe\Adobe Lightroom Classic\Lightroom.exe
25616
```

### Verhalten:

| Zustand | Dateien existieren | Sync erlaubt? |
|---------|-------------------|---------------|
| **Lightroom geöffnet** | `.lrcat.lock` + `.lrcat-shm` + `.lrcat-wal` | ❌ **NEIN!** (SQLite würde korrupt) |
| **Lightroom geschlossen** | Alle drei Dateien entfernt | ✅ Ja |
| **Sync läuft (Programm-Lock)** | `.lrcat.lock` (vom Programm erstellt) | 🔒 Sync im Gange, Lightroom blockiert |

---

## 2️⃣ Katalog-Struktur & Behandlung

```
📁 [Katalogname]/
├── 📄 [Katalogname].lrcat                    ← KRITISCH (SQLite-DB, wird auf Änderung geprüft)
├── 📄 [Katalogname].lrcat.lock               ← Lightroom-Lock (NICHT syncen!)
├── 📄 [Katalogname].lrcat-shm                ← SQLite-Shm (NICHT syncen!)
├── 📄 [Katalogname].lrcat-wal                ← SQLite-Wal (NICHT syncen!)
├── 📁 [Katalogname].lrcat-data/              ← KRITISCH (wichtig! - wird immer Vollständig und nie inkrementell übertragen, [Katalogname].lrcat geändert wurde)
├── 📁 [Katalogname] Helper.lrdata/           ← KRITISCH (wichtig! - wird immer Vollständig und nie inkrementell übertragen, [Katalogname].lrcat geändert wurde)
├── 📁 [Katalogname] Sync.lrdata/             ← KRITISCH (wichtig! - wird immer Vollständig und nie inkrementell übertragen, [Katalogname].lrcat geändert wurde)
├── 📁 [Katalogname] Smart Previews.lrdata/   ← KRITISCH (wichtig! - wird immer Vollständig und nie inkrementell übertragen, [Katalogname].lrcat geändert wurde)
├── 📁 [Katalogname] Previews.lrdata/         ← OPTIONAL (nur wenn SyncPreviewData = true)
└── 📁 [BackupsLocalPath]                     ← AUSSCHLIESSEN (Weil wird von BackupManager.cs behandelt.)
```

### Datei-Behandlung im Sync-Prozess:

| Datei/Ordner | In ZIP-Backup? | Vor Sync löschen? | Von rclone ausnehmen? | Verantwortlich |
|--------------|----------------|-------------------|----------------------|---------------|
| `.lrcat` | ✅ Ja | ❌ Nein | ❌ Nein (immer syncen) | CatalogManager |
| `.lrcat.lock` | ❌ Nein | ❌ Nein | ✅ **Ja (niemals syncen!)** | CatalogManager |
| `.lrcat-shm` | ❌ Nein | ❌ Nein | ✅ **Ja (niemals syncen!)** | CatalogManager |
| `.lrcat-wal` | ❌ Nein | ❌ Nein | ✅ **Ja (niemals syncen!)** | CatalogManager |
| `.lrcat-data/` | ✅ Ja | ✅ Ja | ❌ Nein (immer syncen) | CatalogManager |
| `Helper.lrdata/` | ✅ Ja | ✅ Ja | ❌ Nein (immer syncen) | CatalogManager |
| `Sync.lrdata/` | ✅ Ja | ✅ Ja | ❌ Nein (immer syncen) | CatalogManager |
| `Smart Previews.lrdata/` | ✅ Ja | ✅ Ja | ❌ Nein (immer syncen) | CatalogManager |
| `Previews.lrdata/` | ❌ Nein | ❌ Nein | ✅ **Ja (wenn Flag=false)** | CatalogManager |
| `[BackupsLocalPath]` | ❌ Nein | ❌ Nein | ✅ **Ja (wenn im Katalog-Pfad!)** | **BackupManager** |

---

## 3️⃣ NAS-Erreichbarkeitstest (2-Stufen-Test)

**Zweck:** Verhindert unnötige SYNC-Versuche, wenn das NAS nicht erreichbar ist.

**Design-Entscheidung:**  
- Erreichbarkeitsprüfung wird in einer separaten Klasse `SmbChecker.cs` ausgelagert  
- Wird in `LRCatSync.cs` *vor* Aufruf von `BackupManager.cs` und `CatalogManager.cs` durchgeführt  
- Bei fehlgeschlagenem Test: Beide Prozesse blockiert, TrayIcon weiß mit Fehlermeldung "Keine Netzwerkverbindung"  
- Logik nicht hardcodet, sondern konfigurierbar gestalten (z. B. per Feature-Flag `EnableNasCheck`)

**Ablauf:**
1. `SmbChecker.IsNasReachable()` wird von `LRCatSync` aufgerufen  
2. Prüft SMB-Freigabe (Timeout: 5 Sekunden) → prüft Zugriff auf UNC-Pfad (`\\IP\Path`)  
3. Erst wenn der Test positiv ist, wird SYNC fortgesetzt  

```csharp
bool IsNasReachable()
{
    // SMB-Freigabe test (max 5 Sekunden)
    if (!TestSmbShare($"\\\\{config.RemoteIP}\\{config.RemotePath}", 5000))
    {
        Log.Debug("NAS: SMB-Test fehlgeschlagen");
        return false;
    }
    
    Log.Debug("NAS: Test bestanden");
    return true;
}
```
---

## 4️⃣ Ablaufsteuerung & Synchronisation (Coordinator.cs)

Diese Komponente koordiniert den sequenziellen Ablauf zwischen Backupmanager und Catalogmanager. 
Es ist entscheidend, dass beide Module **nacheinander** und **nicht parallel** ausgeführt werden, um Dateninkonsistenzen zu vermeiden.

### Ablaufprinzip

Der Prozess folgt einem klaren Muster:

- **Backupmanager** wird zuerst ausgeführt und bearbeitet alle fotografischen Dateien.
- Erst wenn der Backupmanager vollständig abgeschlossen ist, startet **Catalogmanager** mit der Synchronisation der Lightroom-Datenbank.
- Danach beginnt der Zyklus von vorne mit dem Backupmanager.

> **Hinweis für die Implementierung:** Es ist strikt darauf zu achten, dass jeweils nur ein Modul aktiv ist. Eine gleichzeitige oder überlappende Ausführung ist zu verhindern.
---

## 5️⃣ Catalogmanager.cs

### 🔵 Phase 0: Vorbereitung

Wenn eines der 3 Dateien exestiert:
├── 📄 [Katalogname].lrcat.lock      ← Lock-Datei (Haupt-Lock)
├── 📄 [Katalogname].lrcat-shm       ← temporäre Shared Memory
└── 📄 [Katalogname].lrcat-wal       ← Journal-Datei um unvollständige Datensätze im Katalog zu verwalten
Dann darf Catalog Sync nicht gestartet werden. Sondern wird in der Statemaschine übersprungen.
Wenn die im nächsten zug immer noch da sind wieder überspringen bis keine der 3 Dateien mehr vorhanden ist.

**❗ Wichtig:** nur die von Lightroom erzeugte .lrcat.lock nicht die von Catalogmanager.cs erzeugte Datei.


### 🔵 Phase 1: Versionsvergleich (mit rclone)

```
1.1 → Überprüfen mit rclone check ob Download/Upload angewendet werden soll

      BEISPIEL: rclone check Lokal remote --include "[Katalogname].lrcat" --one-way     

      AUSWERTUNG:
      - Meldet rclone "0 differences found" → Beide Dateien identisch → Kein Sync nötig
      - Meldet rclone Unterschiede → Terminal-Ausgabe zeigt Zeitstempel (Timestamp)
      - Anhand Timestamp vergleichen welche Datei neuer ist
      
1.2 → Entscheidung basierend auf rclone-Ergebnis:
      - Wenn LOCAL neuer → Upload (Lokal → NAS)
      - Wenn NAS neuer → Download (NAS → Lokal)
      - Wenn gleich → Kein Sync nötig
      
1.3 → Entscheidung:
      - Wenn KEINE Änderungen → Abbruch! KEINE Lock-Dateien erstellen!
      - Wenn Änderungen → Weiter zu Phase 2
```

**❗ Wichtig:** In dieser Phase werden NOCH KEINE Lock-Dateien erstellt!
- Vermeidet unnötige Blockaden anderer Clients
- Lightroom kann weiterhin normal arbeiten
- Erst bei festgestellten Änderungen → Locks in Phase 2

---

### 🔵 Phase 2: Lock-Akquise (Atomar!)

```
2.1 → Versuche LRCatSync.lock auf LOKAL zu erstellen (atomar mit FileShare.None)
      Pfad: [CatalogLocalPath]/LRCatSync.lock
      Inhalt: {
        "client": "PC1",
        "started": "2026-01-15T14:30:00Z",
        "pid": 12345,
        "guid": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
        "side": "local"
      }
      
      WICHTIG: File.Open(lockPath, FileMode.Create, FileAccess.Write, FileShare.None)
      → Wenn IOException → Lock existiert bereits (von anderer Instanz)
      
2.2 → Wenn lokale LRCatSync.lock erfolgreich → Versuche LRCatSync.lock auf NAS zu erstellen
      Pfad: [CatalogRemotePath]/LRCatSync.lock
      Inhalt: {
        "client": "PC1",
        "started": "2026-01-15T14:30:00Z",
        "pid": 12345,
        "guid": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
        "side": "remote"
      }
      
      WICHTIG: Wenn NAS-LRCatSync.lock bereits existiert:
      - Prüfe Alter der Datei
      - Wenn älter als SYNC_LOCK_TIMEOUT_MIN (30 Min) 
        → Stale Lock! Überschreiben mit Warnung im Log
      - Wenn aktueller als 30 Minuten → ABBRUCH! Anderer Client hat aktive Sync
      - ACHTUNG: FileShare.None verhindert gleichzeitiges Erstellen!
      
2.3 → Wenn BEIDE Lock-Dateien erfolgreich erstellt → Weiter zu Phase 3
      (Lokal + Remote = sicheres Sync)
      
2.4 → .lrcat.lock erstellen (Lightroom blockieren)
      Pfad: [CatalogLocalPath]/[Katalogname].lrcat.lock
      → Gleicher Name wie Lightroom-Lock!
      → Verhindert dass Lightroom Katalog während Sync öffnet
      
2.5 → Heartbeat-Thread starten (alle 2-3 Minuten)
      - Aktualisiert Timestamp in BEIDEN LRCatSync.lock Dateien
      - Verhindert Timeout bei langen Syncs (>20 Min)
      - Läuft parallel zum Sync-Prozess
      - WICHTIG: Thread-Referenz speichern für Cleanup!
      - GUID wird im DEBUG-Log gespeichert für Nachverfolgung
```

**Warum BEIDSEITIGE Lock-Dateien?**
- ✅ LOKAL: Verhindert dass eigene Instanz zweimal startet
- ✅ REMOTE: Verhindert dass ANDERE Clients syncen
- ✅ FileShare.None: Macht Erstellen ATOMAR (keine Race Condition)
- ✅ GUID: Eindeutige Identifikation jeder Instanz (pro Sync-Durchlauf neu generiert)
- ✅ GUID wird im DEBUG-Log gespeichert für Nachverfolgung
- ✅ Heartbeat: Verhindert Timeout bei großen Katalogen
- ✅ .lrcat.lock: Blockiert Lightroom erst WENN Sync läuft

---

### 🔵 Phase 3: Vorbereitung (Backup + Löschen)

```
3.1 → Erstelle ZIP-Backup auf NAS (LRCatSync_last_katalog.zip):
      
      WENN bereits vorhanden → Löschen → Neu erstellen
      
      ZIP-Inhalt (kritische Dateien der QUELLE):
      - [Katalogname].lrcat
      - [Katalogname].lrcat-data/
      - [Katalogname] Helper.lrdata/
      - [Katalogname] Sync.lrdata/
      - [Katalogname] Smart Previews.lrdata/
      
      NICHT im ZIP:
      - *.lrcat.lock, *.lrcat-shm, *.lrcat-wal (Lock-Dateien)
      - [Katalogname] Previews.lrdata/ (wird separat behandelt)
      - Backups/ (wird von BackupManager behandelt)
    
```

---

### 🔵 Phase 4: Vollübertragung (mit rclone)

```
4.1 → Rclone sync OHNE --dry-run ausführen
      
      Basis-Befehl:
      rclone sync [Lokal] [NAS] --delete-befores
      
      Ausschlüsse (immer):
        --exclude "*.lrcat.lock"
        --exclude "*.lrcat-shm"
        --exclude "*.lrcat-wal"
        --exclude "[Katalogname] Previews.lrdata/"
        --exclude "[BackupsLocalPath]/**" (wenn BackupsLocalPath UNTER CatalogLocalPath)
        --exclude "LRCatSync_last_katalog.zip"
      
      HINWEIS: [BackupsLocalPath] ist der relative Pfad vom Katalog zum Backup-Ordner
      (wird dynamisch aus Config.BackupsLocalPath berechnet)
          
      WICHTIG: --exclude "LRCatSync_last_katalog.zip" schützt das ZIP-Backup vor Löschung!

4.2 → Extra Rclone sync für "[Katalogname] Previews.lrdata/"

    WICHTIG: Nur wenn SyncPreviewData=true
    WICHTIG: Separater Sync weil Previews.lrdata sehr groß ist und nicht vorher gelöscht werden soll!

      Ausschlüsse (immer):
        --exclude "*.lrcat.lock"
        --exclude "*.lrcat-shm"
        --exclude "*.lrcat-wal"
        --exclude "[Katalogname].lrcat-data/"
        --exclude "[Katalogname] Helper.lrdata/"
        --exclude "[Katalogname] Sync.lrdata/"
        --exclude "[Katalogname] Smart Previews.lrdata/"
        --exclude "[BackupsLocalPath]/**" (wenn BackupsLocalPath UNTER CatalogLocalPath)
        --exclude "LRCatSync_last_katalog.zip"
      
4.3 → Transfer-Überwachung:
      - Log-Ausgabe parsen (wie BackupManager.WriteRcloneStats())
      - Bei Fehler → KEIN automatisches Rollback!
      - User muss manuell LRCatSync_last_katalog.zip zurückkopieren
```

---

### 🔵 Phase 5: Cleanup & Freigabe

```
5.1 → Heartbeat-Thread stoppen
      - CancellationToken auslösen
      - Thread sauber beenden
      - Verhindert Schreibzugriffe auf gelöschte Lock-Dateien
      
5.2 → Lösche BEIDE LRCatSync.lock Dateien:
      - Lokal: [CatalogLocalPath]/LRCatSync.lock
      - Remote: [CatalogRemotePath]/LRCatSync.lock
      
5.3 → Lösche lokale .lrcat.lock Datei (vom Programm erstellt in 2.4)
      
5.4 → LRCatSync_last_katalog.zip BLEIBT auf NAS (1 Version permanent)
      → Ringspeicher mit 1 Slot (keine Historie)
      
5.5 → SyncCoordinator.EndCatalogSync() aufrufen
      → Ermöglicht nächsten Sync-Durchlauf
      
5.6 → Log-Eintrag im lokalen Log (INFO-Level):
      "Katalog-Sync erfolgreich abgeschlossen"
      "Richtung: Upload/Download"
      "Dateien: X, Bytes: Y"
      "Dauer: HH:MM:SS"
      
5.7 → Warte bis gesamten Durchlauf abgeschlossen ist
5.8 → Erst DANN nächster Check-Interval starten

WICHTIG: Phase 5.1-5.3 werden IMMER ausgeführt (auch bei Fehler in Phase 4)!
→ Locks müssen auch bei Sync-Fehler entfernt werden (try-finally Block)
```

---

## 6️⃣ Besondere Szenarien

### Szenario A: Backup-Ordner LIEGT IM Katalog-Pfad

```
Beispiel:
CatalogLocalPath = "C:/Bilder/Lightroom/MeinKatalog/"
BackupsLocalPath = "C:/Bilder/Lightroom/MeinKatalog/Backups/"
→ Relativer Pfad: "Backups"
→ rclone Exclude: --exclude "Backups/**"
```

→ **BackupsLocalPath** wird vom Nutzer konfiguriert (beliebiger Ordnername)
→ Backup-Ordner wird AUSSCHLIESSLICH von `BackupManager.cs` verwaltet
→ CatalogManager MUSS diesen Ordner von rclone ausschließen

**Umsetzung in Config.cs:**
```csharp
// Prüfe ob BackupsLocalPath UNTER CatalogLocalPath liegt
bool IsBackupInsideCatalogPath()
{
    if (string.IsNullOrEmpty(BackupsLocalPath) || 
        string.IsNullOrEmpty(CatalogLocalPath))
        return false;
    
    string backupNormalized = Path.GetFullPath(BackupsLocalPath).ToLower();
    string catalogNormalized = Path.GetFullPath(CatalogLocalPath).ToLower();
    
    return backupNormalized.StartsWith(catalogNormalized);
}
```

**Wenn TRUE:** Dynamischen Exclude zu rclone-Befehl hinzufügen:
```csharp
// Extrahiere den relativen Pfad vom Katalog zum Backup-Ordner
string relativeBackupPath = Path.GetRelativePath(CatalogLocalPath, BackupsLocalPath);
// Füge hinzu: --exclude "[relativerPfad]/**"
// Beispiel: --exclude "Backups/**" oder --exclude "MeinBackup/**"
```

**WICHTIG:** 
- Backup-Ordner wird AUSSCHLIESSLICH von `BackupManager.cs` verwaltet
- `CatalogManager.cs` hat KEINE Verantwortung für Backups
- Bei verschachtelter Struktur (Backup IN Katalog) → Automatisch ausschließen
- **Ordnername ist beliebig** (was in `BackupsLocalPath` konfiguriert ist)

---

### Szenario B: Lightroom wird WÄHREND Sync gestartet

```
Problem: User öffnet Lightroom während Sync läuft
Risiko: SQLite-Korruption!

Lösung:
1. Programm-Lock (.lrcat.lock) verhindert Öffnen in Lightroom
2. Lightroom zeigt Fehler: "Katalog wird bereits verwendet"
3. User muss warten bis Sync fertig ist
4. Tray-Icon zeigt "Syncing" (gelb) → User sieht Status
   WICHTIG: Gelb nur wenn rclone sync läuft (Phase 4), nicht während Lock-Akquise (Phase 2)
```

---

### Szenario C: Programm-Crash während Sync

```
Problem: Programm stürzt ab, .lrcat.lock bleibt bestehen
Risiko: User kann Katalog nicht öffnen, Sync hängt

Lösung:
1. Beim Programm-Start: Prüfe ob eigene .lrcat.lock existiert
2. Wenn ja und die LRCatSync.lock älter als 30min ist → Löschen (war von abgestürzter Instanz)
3. LRCatSync.lock auf NAS hat 30-Min-Timeout → Automatisch bereinigt
4. LRCatSync.lock auf LOKAL wird beim Neustart auch gelöscht
5. LRCatSync_last_katalog.zip bleibt als manuelle Recovery-Option

WICHTIG: Lock-Cleanup in try-finally Block implementieren!
→ Auch bei Programm-Fehler werden Locks sicher entfernt
```

---

## 5️⃣ Entscheidungen & Konfiguration **⚠️ FINAL**

| # | Entscheidung | Umsetzung |
|---|--------------|-----------|
| 1 | **Sync-History auf NAS?** ❌ Nein | Nur lokales Log (DEBUG-Level) |
| 2 | **Tray-Status für Katalog-Sync?** 🟡 Gleiche Icons wie BackupManager.cs | Grün=Standby, Gelb=rclone läuft (Phase 4), Rot=Error, weiß=keine verbindung| blau=Lightroom läuft (lock gefunden) |
| 3 | **Logging?** 📄 Zusammengeführt | Alles in `LRCatalogSync.log` | Sinnvolle Aufteilung in DEBUG, INFO, NOTICE, ERROR (Wichtig log.cs verwenden) |
| 4 | **Rollback?** ✋ Manuell | User kopiert LRCatSync_last_katalog.zip bei Bedarf zurück |
| 5 | **GUID-Tracking?** ✅ Ja | Pro Sync-Durchlauf neue GUID generieren, im DEBUG-Log speichern |

---

## 6️⃣ Config-Erweiterungen

### In `Config.cs`:

```
┌──────────────────────────────────────────────────────────────┐
│  Umbenennung                                                 │
├──────────────────────────────────────────────────────────────┤
│  LocalPath         → CatalogLocalPath (Katalog-Pfad)         │
│  RemotePath        → CatalogRemotePath (Katalog Remote)      │
│  SyncPreviewData   → Steuert Previews.lrdata Behandlung      │
└──────────────────────────────────────────────────────────────┘
```

### In `GlobalData.cs` (GlobalConst):

```csharp
public const int SYNC_LOCK_TIMEOUT_MIN = 30;    // Globale Konstante
public const int CATALOG_CHECK_INTERVAL = 10;   // Sekunden
public const int WATCHDOG_TIME = 30;            // Sekunden
```

---

**Dokument erstellt:** 2026-06-28