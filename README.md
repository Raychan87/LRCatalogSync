# ACHTUNG!
Aktuell ist es noch eine Beta und kann/wird Fehler enthalten. Versioniertes Backup eures Lightroom Katalogs ist immer zu empfehlen.

# LRCatalogSync

Synchronisiert Adobe Lightroom Classic‑Kataloge über Samba Server/NAS.
Das Programm erkennt, wenn Lightroom Classic läuft, und verzichtet dann auf den Sync, um Katalogkorruptionen zu vermeiden.
Es synchronisiert alle vom Lightroom Katalog benötigten Hilfsdateien.
LRCatalogSync ist dafür gedacht, Lightroom Classic auf mehreren Rechnern zu betreiben und diese über ein NAS mit Samba zu synchronisieren.
Es zeigt über ein Symbol im Traymenü den Status des Programms an und kann im Autostart hinterlegt werden.

## Funktionsweise

### Kopierte Dateien
Beim Sync werden folgende Lightroom‑Dateien und Ordner synchronisiert:
- `*.lrcat` – Die Hauptkatalogdatei (SQL)
- `*.lrcat-data/` – Katalog-Datenbank (Masken, KI-Auswahlen)
- `* Sync.lrdata/` – Für Adobe Creative Cloud
- `* Smart Previews.lrdata/` – kleine Vorschaudateien von Raw/DNG
- `* Helper.lrdata/` – Hilfsdaten für Katalogfunktionen
Optional(siehe Einstellungen)):
- `* Previews.lrdata/` – Standard und 1:1 Vorschaudateien 
- `Katalog Backups *.zip` - Automatische Sicherungsdaten von Lightroom

### Lock-Dateien (Lightroom-Erkennung)
Das Programm erkennt automatisch, wenn Lightroom geöffnet ist, und verzichtet dann auf den Sync:
- `*.lrcat.lock` – Haupt-Lock-Datei
- `*.lrcat-shm` – Shared Memory Segment
- `*.lrcat-wal` – Write-Ahead Log

Diese Dateien werden von Lightroom beim Öffnen des Katalogs erstellt und beim Schließen wieder gelöscht.

## Voraussetzungen
- Windows 8.1 + (empfohlen 10/11)
- **.NET 10 (Windows‑Spezifisch)** – das Projekt verwendet das SDK‑Target `net10.0-windows`.
- **rclone** (https://rclone.org)

## Installation
1. **.NET 10 (Windows‑Spezifisch)** Runtime installieren – das Projekt nutzt das SDK‑Target `net10.0-windows`. Die aktuelle Runtime finden Sie unter https://dotnet.microsoft.com/download/dotnet/10.0.
2. rclone herunterladen, `rclone.exe` z. B. nach `C:\Programme\rclone` entpacken.
3. LRCatalogSync von GitHub herunterladen, entpacken und `LRCatalogSync.exe` starten – das Symbol erscheint im Tray.

## Nutzung
*Start:* Doppelklick auf `LRCatalogSync.exe` (kann beim Systemstart aktiviert werden). 
*Stop:* Rechtsklick auf das Tray‑Icon → **Beenden**.

## Konfiguration (grafisch)
![alt text](docs/images/config_menu.png)
| Feld | Beschreibung |
|------|--------------|
| **Auto-Start** | Programm beim Windows-Start automatisch ausführen |
| **rclone‑Pfad** | Pfad zur `rclone.exe` (z. B. `C:\Programme\rclone\rclone.exe`) |
| **Log‑Level** | `DEBUG`, `INFO`, `NOTICE`, `ERROR` |
| **Katalog‑Datei** | Pfad zur `.lrcat`‑Datei (lokal) |
| **Remote‑Pfad** | Zielpfad auf dem SMB‑Server (z. B. `/Lightroom/`) |
| **letzten Katalog behalten?** | Speichert vor den Sync den Katalog in ein Extra Ordner |
| **Ordnername** | Für die letzten Katalogspeicherung |
| **Backup Pfad** | Für die Lightroom Sicherungsdateien (Optional für den Sync) |
| **Server‑IP / Host** | IP oder Hostname des SMB‑Servers |
| **Benutzer / Passwort** | Zugangsdaten (verschlüsselt gespeichert) |
| **Backup aktivieren** | Optional, lokale und Remote‑Backups synchronisieren |

Einstellungen werden in `data/config/` gespeichert.

## TrayIcon

Tray‑Icon‑Status:
- 🟢 Standby – bereit, kein Sync aktiv
- 🟡 Syncing – Synchronisiere Backups und Katalog
- 🔵 Lock – Lightroom ist geöffnet, Sync übersprungen
- 🔴 Error – Fehler, siehe Log
- ⚪ Error – Konfigurationsdatei fehlt

Logs finden Sie unter `data/logs/`.

## Fehlersuche (Kurz)
- *rclone.exe nicht gefunden*: Pfad prüfen.
- *Samba‑Verbindung fehlgeschlagen*: IP, Benutzer, Passwort und Netzwerk prüfen.
- *Kein *.lrcat* gefunden*: Pfad zum Katalog korrekt angeben.
- *Lock erkannt*: Lightroom läuft, Sync wird automatisch übersprungen.

## ToDo

- Multilanguage (english)
- Darkmode
- Programm Icon
- Transparente und Konstante Beispieltexte im Einstellungsmenü

## Ressourcen
- GitHub: https://github.com/Raychan87/LRCatalogSync
- rclone: https://rclone.org
- Lightroom Classic: https://adobe.com/products/lightroom

*Version **0.9.4‑beta** – Stand: Juli 2026*
