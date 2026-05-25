# LRCatalogSync

**Automatische Synchronisation von Adobe Lightroom Classic Katalogen mit Samba/SMB-Servern**

---

## 📋 Inhaltsverzeichnis (Deutsch)

- [Übersicht](#-übersicht)
- [Systemanforderungen](#-systemanforderungen)
- [Installation](#-installation)
- [Konfiguration](#-konfiguration)
- [Verwendung](#-verwendung)
- [Technische Details](#-technische-details)
- [Fehlerbehebung](#-fehlerbehebung)

---

## 🎯 Übersicht

**LRCatalogSync** ist ein Windows-Anwendung, die es ermöglicht ein Adobe Lightroom Classic Katalog über zwei Rechner und einen Samba/SMB-Server (z.B. Synology NAS) synchronisiert.
Das Programm läuft diskret im Systemtray und kümmert sich um die Synchronisation im Hintergrund.

### Warum LRCatalogSync statt Syncthing, Synology Drive usw.?

- Es ist speziell für den Adobe Lightroom Katalog entwickelt worden
- Erkennt, wenn Lightroom läuft, und wartet bis es geschlossen ist um ein Sync zu starten
- Wenn ein Sync läuft, wird der Katalog für Lightroom gesperrt
- Da der Katalog eine SQL-Datenbank ist darf diese nicht kopiert werden, wenn diese in Verwendung ist.
- Zusätzlich kann auch der Lightroom Katalog Sicherungsordner überwacht und mit Samba/SMB-Server synchronisiert werden.
- Einfache grafische Konfiguration
- Detailliertes Logging aller Operationen

---

## 🔧 Systemanforderungen

### Software
- **Windows 7 oder später** (Windows 10/11 empfohlen)
- **.NET Framework 4.8** oder höher
- **rclone** (kostenlos, Open-Source)

### Netzwerk
- Zugang zu einem **Samba/SMB-Server** (z.B. Synology NAS, Windows Fileshare, Nextcloud, etc.)
- Gültige Zugangsdaten (Benutzername + Passwort)

### Lightroom
- **Adobe Lightroom Classic** (CC 2015 oder später)

---

## 📦 Installation

### Schritt 1: .NET Framework 4.8 installieren (falls nicht vorhanden)

1. Laden Sie die neueste Version von [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework) herunter
2. Führen Sie den Installer aus und folgen Sie den Anweisungen
3. Starten Sie Ihren Computer neu

Zu prüfen ob .NET 4.8 installiert ist:
- Öffnen Sie die Windows-Systemsteuerung
- Gehen Sie zu: Programme > Programme und Funktionen
- Suchen Sie nach ".NET Framework" in der Liste

### Schritt 2: rclone installieren

1. Laden Sie rclone von [rclone.org/downloads](https://rclone.org/downloads/) herunter
2. Laden Sie die **Windows-Version** (64-bit) herunter
3. Entpacken Sie die Zip-Datei in einen Ordner Ihrer Wahl (z.B. `C:\Programme\rclone`)
4. Merken Sie sich diesen Pfad für die Konfiguration!

### Schritt 3: LRCatalogSync installieren

1. Laden Sie die neueste Version von [GitHub](https://github.com/Raychan87/LRCatalogSync/releases) herunter
2. Entpacken Sie die Zip-Datei in einen Ordner Ihrer Wahl
3. Starten Sie `LRCatalogSync.exe` (oder doppelklick auf die ausführbare Datei)
4. Das Programm erscheint als Symbol im Systemtray

---

## ⚙️ Konfiguration

#### Allgemein
| Feld | Beschreibung | Beispiel |
|------|-------------|---------|
| **Rclone Pfad** | Pfad zur rclone.exe | `C:\Programme\rclone` oder `./rclone` |
| **Log-Level** | Detailgrad des Loggings | DEBUG, INFO, NOTICE, ERROR, Aus |

#### Lightroom Katalog
| Feld | Beschreibung | Beispiel |
|------|-------------|---------|
| **Lokaler Pfad** | Pfad zu Ihrem Lightroom Katalog | `C:\Benutzer\Max\Bilder\Lightroom` |
| **Remote Pfad** | Pfad auf dem Samba-Server | `/Lightroom` oder `/NAS/Kataloge` |

#### Backup (optional)
| Feld | Beschreibung | Beispiel |
|------|-------------|---------|
| **Sicherungsordner aktivieren** | Backups synchronisieren? | Ja/Nein |
| **Lokaler Backup Pfad** | Pfad zu lokalen Backups | `C:\Benutzer\Max\Bilder\Lightroom\Backups` |
| **Remote Backup Pfad** | Pfad zu Remote-Backups | `/Backups` oder `/NAS/Backups` |

#### Samba Server
| Feld | Beschreibung | Beispiel |
|------|-------------|---------|
| **Remote Server IP/Name** | IP oder Hostname des Servers | `192.168.1.100` oder `nas.local` |
| **Remote Benutzer** | Samba-Benutzername | `administrator` oder `max` |
| **Remote Passwort** | Samba-Passwort (wird verschlüsselt) | Ihr Samba-Passwort |

---

## 🚀 Verwendung

### Starten und Stoppen

**Starten:**
- Doppelklick auf `LRCatalogSync.exe`
- Das Programm startet automatisch beim Windows-Start (optional konfigurierbar)

**Stoppen:**
- Rechtsklick auf das Tray-Icon → "Beenden"
- Das Programm wird vom Windows-Start entfernt

### Systemtray-Icons und Status

| Icon | Status | Bedeutung |
|------|--------|-----------|
| 🟢 Grün | Standby | Alles OK, warte auf Änderungen |
| 🟡 Orange/Gelb | Syncing | Synchronisation läuft |
| 🔵 Blau | Lock | Lightroom läuft, warte auf Abschluss |
| 🔴 Rot | Error | Fehler - siehe Logs |
| ⚪ Weiß | Fehler | Keine Verbindung oder Einstellungen fehlen |

---

## 🔍 Technische Details

### Konfigurationsdateien

| Datei | Beschreibung |
|------|-------------|
| `data/config/LRSync.conf` | Hauptkonfigurationsdatei von LRCatalogSync (wird automatisch erstellt) |
| `data/config/rclone.conf` | Konfigurationsdatei von rclone (wird automatisch erstellt) |
| `data/logs/` | Ordner für Log-Dateien (AUS, DEBUG, INFO, NOTICE, ERROR) |

---

### Log-Levels erklärt

- **Aus**: Keine Logs
- **DEBUG**: Detaillierte technische Informationen (für Entwicklung/Debugging)
- **INFO**: Wichtige Informationen (Sync-Start, Sync-Ende)
- **NOTICE**: Warnungen (Lock erkannt, Verbindung unterbrochen)
- **ERROR**: Nur Fehler

---

## 🛠️ Fehlerbehebung

### Problem: "rclone.exe nicht gefunden"

**Lösung:**
1. Überprüfen Sie, ob rclone installiert ist
2. Vergewissern Sie sich, dass der Pfad korrekt ist (mit Klick auf "...")
3. Prüfen Sie, ob die `rclone.exe` im angegebenen Verzeichnis existiert

### Problem: "Samba-Verbindung fehlgeschlagen"

**Mögliche Ursachen:**
1. Netzwerkverbindung unterbrochen
2. Samba-Server ist nicht erreichbar
3. Benutzername oder Passwort ist falsch
4. IP-Adresse ist falsch

**Lösung:**
1. Testen Sie die Netzwerkverbindung (Ping zum Server)
2. Überprüfen Sie Benutzername und Passwort
3. Überprüfen Sie die Server-IP-Adresse
4. Schauen Sie in die Logs für mehr Informationen

### Problem: "Keine *.lrcat Datei gefunden"

**Lösung:**
1. Überprüfen Sie den lokalen Pfad
2. Stellen Sie sicher, dass ein Lightroom-Katalog vorhanden ist
3. Der Katalog muss die Dateiendung `.lrcat` haben

### Problem: Synchronisation läuft nicht

**Mögliche Ursachen:**
1. Lightroom ist noch geöffnet (Lock-Datei erkannt)
2. Keine Änderungen seit letztem Sync
3. Netzwerkverbindung unterbrochen
4. Programm läuft nicht

**Lösung:**
1. Überprüfen Sie, ob Lightroom geschlossen ist
2. Schauen Sie im Systemtray nach dem Status-Icon
3. Überprüfen Sie die Logs für Fehler
4. Starten Sie das Programm neu

### Logs Debug-Modus aktivieren

1. Öffnen Sie die Einstellungen
2. Setzen Sie "Log-Level" auf "Debug"
3. Speichern Sie
4. Starten Sie das Programm neu
5. Schauen Sie in die Logs für detaillierte Informationen

---

## 🔗 Links & Ressourcen

- **GitHub Project**: [github.com/Raychan87/LRCatalogSync](https://github.com/Raychan87/LRCatalogSync)
- **Autor**: [Fototour-und-Technik.de](https://Fototour-und-Technik.de)
- **rclone**: [rclone.org](https://rclone.org)
- **Adobe Lightroom**: [adobe.com/products/lightroom](https://adobe.com/products/lightroom)

---

**Version 0.9.0-beta1** | Letzte Aktualisierung: März 2026

---
