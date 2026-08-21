# Konzept: Mehrsprachigkeit für LRCatalogSync

> **Entscheidungen (Stand 2026-06-28)**
>
> - Unterstützte Sprachen: **Deutsch (de)** als Standard/Fallback + **Englisch (en)**
> - Architektur ist offen für weitere Sprachen – neue JSON-Datei hinzufügen genügt
> - **Dynamischer Sprachwechsel ohne Neustart**
> - Speicherung als **embedded Resource** in der EXE (passt zu `PublishSingleFile=true`)
> - Sprachdateien sind **kein** Bestandteil des `data/`-Verzeichnisses und liegen nicht neben der EXE
> - Logging bleibt weiterhin auf **Deutsch** (keine Lokalisierung der Log-Nachrichten)
> - Interne Status-Identifier (`"Standby"`, `"Error"`, `"Lockfile"`, …) bleiben unverändert

---

## 1. Architektur-Übersicht

```
┌──────────────────────────────────────────────────────────────┐
│  Build-Zeit                                                 │
│  ──────────                                                  │
│  Lang/de.json  ──► EmbeddedResource  ──► Assembly in .EXE    │
│  Lang/en.json  ──► EmbeddedResource  ──► Assembly in .EXE    │
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│  Laufzeit                                                   │
│  ────────                                                    │
│  Localizer (Singleton)                                       │
│    └─ Lädt JSON via Assembly.GetManifestResourceStream()     │
│    └─ Hält aktuelle Strings im Speicher                      │
│    └─ Feuert LanguageChanged-Event                           │
│                                                               │
│  Abonnenten (= UI-Bereiche):                                 │
│    ├─ TrayManager    → setzt Tray.Text neu                   │
│    ├─ SettingsForm   → ruft ApplyLocalization() neu          │
│    └─ LRCatSync      → baut Menü neu auf                     │
└──────────────────────────────────────────────────────────────┘
```

```
[Programmstart]
       ↓
[Language aus Config laden]
       ↓
   <Gesetzt?>
   ├── Nein ──► [System-Sprache prüfen] ──► [Falls unterstützt: übernehmen, sonst de]
   └── Ja ──► [de / en]
       ↓
[Localizer.Load(language)]
       ↓
[SettingsForm: ComboBox mit Sprachen füllen]
       ↓
<Sprache in Settings geändert?>
   ├── Nein  → [Anwendung läuft]
   └── Ja    → [Localizer.SetLanguage(lang)]
                    ↓
              [LanguageChanged-Event]
                    ↓
              [Alle UI-Bereiche aktualisieren sich]
                    ↓
              [Sprache in Config speichern]
```

---

## 2. Verzeichnisstruktur (Erweiterung)

```
src/
├── Infrastructure/
│   └── Localization/
│       ├── Localizer.cs             ← Hauptklasse (Singleton)
│       ├── LanguageInfo.cs          ← Sprach-Metadaten
│       └── Lang/
│           ├── de.json              ← Deutsche Strings
│           └── en.json              ← Englische Strings
```

Die Dateien werden im `.csproj` als `<EmbeddedResource>` markiert – sie landen damit beim Build in der Assembly und bei `PublishSingleFile=true` in der EXE.

---

## 3. Storage-Form: JSON

### 3.1 Aufbau einer Sprachdatei

Jede Sprache ist eine JSON-Datei mit zwei Sektionen: `meta` (Anzeigename, Code) und `strings` (Key/Value-Paare).

```json
{
  "meta": {
    "code": "de",
    "display": "Deutsch"
  },
  "strings": {
    "menu_settings": "Einstellungen",
    "menu_exit": "Beenden",
    "tray_standby": "wartet auf Änderungen…",
    "settings_title": "Einstellungen",
    "label_auto_run": "Automatisch beim Systemstart ausführen",
    "btn_save": "Speichern",
    "btn_cancel": "Abbrechen",
    "msg_link_error": "Link konnte nicht geöffnet werden.",
    "msg_save_success": "Einstellungen erfolgreich gespeichert!",
    "msg_rclone_missing": "rclone.exe nicht gefunden!",
    "msg_no_catalog": "Katalog-Datei fehlt",
    "msg_remote_sync_active": "Lightroom Classic ist aktiv.",
    "msg_remote_backup_sync": "synchronisiere Lightroom Sicherungsordner.",
    "msg_remote_catalog_sync": "synchronisiere Lightroom Katalog.",
    "msg_no_cfg": "Konfigurationsdateien fehlen!",
    "msg_no_samba": "Keine Verbindung zum Samba Server!",
    "msg_rclonerc_missing": "rclone Konfigurationsdatei fehlt!",
    "msg_rcloneexe_missing": "rclone.exe fehlt!"
  }
}
```

### 3.2 Englische Variante (`en.json`)

```json
{
  "meta": {
    "code": "en",
    "display": "English"
  },
  "strings": {
    "menu_settings": "Settings",
    "menu_exit": "Exit",
    "tray_standby": "waiting for changes…",
    ...
  }
}
```

### 3.3 Namenskonvention für Keys

- Bereichs-Präfix (`menu_*`, `tray_*`, `settings_*`, `label_*`, `btn_*`, `msg_*`)
- Snake_case
- Englischer Basisbezeichner (Sprachneutral)

---

## 4. Was wird NICHT übersetzt

| Bereich | Grund |
|---|---|
| Log-Strings (`Log.Debug/Info/Notic/Error`) | Debugging stabil, einheitlich für Support |
| Interne Status-Keys (`"Standby"`, `"Error"`, …) | Werden als Schlüssel benutzt, sprachneutral |
| Fenstertitel `LRCatalogSync v…` | Branding, bleibt |
| Webseiten-Linkbeschriftungen im SettingsForm | Branding (`GitHub Project`, `© Fototour und Technik`) |
| Technische Bezeichner | `.lrcat`, `rclone.exe`, `Samba`, ... |

---

## 5. Komponenten-Design

### 5.1 `Localizer.cs` (Singleton)

**Hauptklasse** - wird einmalig beim Start über `Localizer.Instance` angesprochen.

**Eigenschaften:**
- `static Localizer Instance` - Lazy-Thread-Safe-Singleton
- `IReadOnlyList<LanguageInfo> AvailableLanguages` - Liste der gefundenen Sprachen für ComboBox
- `string CurrentLanguage` - aktuell aktiver Sprach-Code (`"de"`, `"en"`)
- `event EventHandler? LanguageChanged` - feuert bei jedem Sprachwechsel

**Methoden:**
- `void Load(string language)` - Sprache aus embedded Resource laden
- `string Get(string key)` - String abrufen mit Fallback auf Key selbst
- `void SetLanguage(string language)` - Sprache wechseln + Event feuern
- `static string GetSystemLanguage()` - Windows-Sprache ermitteln (`"de"`/`"en"`/`null`)

**Verhalten:**
- Standard-Sprache und Fallback: `"de"`
- Lädt beim ersten Zugriff automatisch die aktive Sprache
- Bei unbekanntem Key -> gibt den Key selbst zurück (kein Crash)

### 5.2 `LanguageInfo.cs`

Record-Klasse für die ComboBox-Befüllung.

- `string Code` - Sprachcode (`"de"`, `"en"`)
- `string DisplayName` - Name in eigener Sprache (`"Deutsch"`, `"English"`)
- **`override ToString()`** -> liefert `DisplayName`

### 5.3 Sprachdateien automatisch entdecken

Beim Start scannt der Localizer per `Assembly.GetManifestResourceNames()` alle Resourcen mit dem Muster `*.Lang.*.json`. Für eine neue Sprache muss nur die JSON-Datei im `.csproj` als Embedded Resource markiert werden - kein C#-Code-Edit.

### 5.4 Sprachwechsel ohne Neustart

`Localizer.SetLanguage(...)` läuft intern:

1. Lädt die neue Sprachdatei in den Speicher.
2. Setzt `CurrentLanguage`.
3. Feuert `LanguageChanged`.

UI-Bereiche abonnieren `LanguageChanged` einmalig beim Start und reagieren selbständig:

| Bereich | Reaktion |
|---|---|
| `TrayManager` | `tray.Text` wird beim nächsten `UpdateStatus` neu gesetzt |
| `SettingsForm` | ruft `ApplyLocalization()` neu auf |
| `LRCatSync.SetupContextMenu()` | Menu wird beim Sprachwechsel neu aufgebaut |

---

## 6. System-Sprache Auto-Erkennung

Reihenfolge beim ersten Start (kein Wert in `config.Language`):

1. `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName` lesen (z. B. `"de"`, `"en"`, ...).
2. Prüfen, ob dieser Code in der Liste der unterstützten Sprachen enthalten ist.
3. Falls ja -> übernehmen.
4. Falls nein -> Fallback `"de"`.

Umsetzung als `Localizer.GetSystemLanguage()`.

---