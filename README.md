# LRCatalogSync

Synchronisiert Adobe Lightroom Classic‑Kataloge über SMB/Samba (z. B. Synology NAS). Das Programm läuft im System‑Tray, erkennt Lightroom‑Locks und führt sichere Kopien per **rclone** aus.

## Voraussetzungen
- Windows 8.1 + (empfohlen 10/11)
- **.NET 10 (Windows‑Spezifisch)** – das Projekt verwendet das SDK‑Target `net10.0-windows`.
- **rclone** (https://rclone.org)

## Installation
1. **.NET 10 (Windows‑Spezifisch)** Runtime installieren – das Projekt nutzt das SDK‑Target `net10.0-windows`. Die aktuelle Runtime finden Sie unter https://dotnet.microsoft.com/download/dotnet/10.0.
2. rclone herunterladen, `rclone.exe` z. B. nach `C:\Programme\rclone` entpacken.
3. LRCatalogSync von GitHub herunterladen, entpacken und `LRCatalogSync.exe` starten – das Symbol erscheint im Tray.

## Konfiguration (grafisch)
| Feld | Beschreibung |
|------|--------------|
| **rclone‑Pfad** | Pfad zur `rclone.exe` |
| **Log‑Level** | `DEBUG`, `INFO`, `NOTICE`, `ERROR` |
| **Katalog‑Datei** | Pfad zur `.lrcat`‑Datei |
| **Remote‑Pfad** | Zielpfad auf dem SMB‑Server |
| **Server‑IP / Host** | IP oder Hostname des SMB‑Servers |
| **Benutzer / Passwort** | Zugangsdaten (verschlüsselt) |
| **Backup aktivieren** | Optional, lokale und Remote‑Backups synchronisieren |

Einstellungen werden in `data/config/` gespeichert.

## Nutzung
*Start:* Doppelklick auf `LRCatalogSync.exe` (kann beim Systemstart aktiviert werden). 
*Stop:* Rechtsklick auf das Tray‑Icon → **Beenden**.

Tray‑Icon‑Status:
- 🟢 Standby – bereit
- 🟡 Syncing – Synchronisation läuft
- 🔵 Lock – Lightroom ist geöffnet, wartet
- 🔴 Error – Fehler, siehe Log

Logs finden Sie unter `data/logs/`.

## Fehlersuche (Kurz)
- *rclone.exe nicht gefunden*: Pfad prüfen.
- *Samba‑Verbindung fehlgeschlagen*: IP, Benutzer, Passwort und Netzwerk prüfen.
- *Kein *.lrcat* gefunden*: Pfad zum Katalog korrekt angeben.

## Ressourcen
- GitHub: https://github.com/Raychan87/LRCatalogSync
- rclone: https://rclone.org
- Lightroom Classic: https://adobe.com/products/lightroom

*Version **0.9.2‑beta** – Stand: Juli 2026*
