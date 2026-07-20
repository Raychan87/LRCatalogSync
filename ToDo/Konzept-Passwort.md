# Passwort-Konzept - Implementierungsstatus

## Stand: [Datum eintragen]

## Konzept-Dokument
- **Datei**: `ToDo/Konzept-Passwort.md`
- **Status**: ✅ Vollständig erstellt
- **Inhalt**: 
  - Ausgangssituation und Zielsetzung
  - Technische Umsetzung (AES-256 für Samba, rclone obscure für Rclone)
  - Code-Beispiele für alle betroffenen Klassen
  - Ablaufdiagramm
  - Teststrategie
  - Risikoanalyse

## Implementierungsplan

### 1. AesEncryptor.cs (neu)
- **Datei**: `src/Infrastructure/AesEncryptor.cs`
- **Status**: ⏳ Nicht begonnen
- **Aufgabe**: Statische Klasse mit Encrypt/Decrypt Methoden für AES-256
- **Abhängigkeiten**: System.Security.Cryptography (im .NET 10.0 enthalten)

### 2. AppConfig.cs anpassen
- **Datei**: `src/Infrastructure/AppConfig.cs`
- **Status**: ⏳ Nicht begonnen
- **Änderungen**:
  - `SambaPassword` → `SambaPasswordRclone` (rclone-obscured)
  - Neuer Property: `SambaPasswordAes` (AES-256 encrypted)
  - Load-Methode: Beide Properties laden
  - Save-Methode: Beide Properties speichern
  - Migration: Altes `SambaPassword` erkennen und aufteilen

### 3. SettingsForm.cs anpassen
- **Datei**: `src/UI/SettingsForm.cs`
- **Status**: ⏳ Nicht begonnen
- **Änderungen**:
  - `BtnSave_Click`: Zwei Passwörter verschlüsseln
  - `ObscurePassword`: Für Rclone beibehalten
  - Neue Methode: `EncryptPasswordAes` für Samba
  - `SaveRcloneConfig`: Nur `SambaPasswordRclone` speichern

### 4. Smb.cs anpassen
- **Datei**: `src/Infrastructure/Smb.cs`
- **Status**: ⏳ Nicht begonnen
- **Änderungen**:
  - `Login`-Methode: AES-Entschlüsselung vor Authentifizierung
  - `SMBConnectionManager.EnsureConnected`: Entschlüsselte Passwörter nutzen

### 5. Migration implementieren
- **Status**: ⏳ Nicht begonnen
- **Aufgabe**: Alte `SambaPassword` (rclone-obscured) in beide neuen Formate umwandeln
- **Ort**: `AppConfig.Load`-Methode oder separate Migration-Methode

## Entscheidungen (nach User-Feedback)

1. **Verschlüsselungsschlüssel**: Statische Passphrase im Code (einfache Implementierung)
2. **UI-Anpassung**: Ein Passwort-Feld mit automatischer Aufteilung
3. **Caching-Strategie**: Kein Caching - bei jedem Login entschlüsseln (sicher)

## Implementierungsstatus (abgeschlossen)

### 1. AesEncryptor.cs ✅
- **Datei**: `src/Infrastructure/AesEncryptor.cs`
- **Status**: ✅ Erstellt
- **Features**:
  - Statische Encrypt/Decrypt Methoden
  - AES-256 mit zufälligem IV pro Verschlüsselung
  - Base64-Kodierung für Konfigurationsdatei
  - Statische Passphrase: "LightroomSync2024SecureKey!"

### 2. AppConfig.cs ✅
- **Datei**: `src/Infrastructure/Config.cs`
- **Status**: ✅ Angepasst
- **Änderungen**:
  - `SambaPasswordRclone` (rclone-obscured)
  - `SambaPasswordAes` (AES-256 encrypted)
  - Migration in Load-Methode
  - Beide Properties in Save-Methode

### 3. SettingsForm.cs ✅
- **Datei**: `src/UI/SettingsForm.cs`
- **Status**: ✅ Angepasst
- **Änderungen**:
  - Ein Passwort-Feld mit automatischer Aufteilung
  - `originalPasswordRclone` und `originalPasswordAes` Felder
  - Beide Passwörter werden bei Speicherung verschlüsselt

### 4. Smb.cs ✅
- **Datei**: `src/Infrastructure/Smb.cs`
- **Status**: ✅ Angepasst
- **Änderungen**:
  - `Login`-Methode entschlüsselt AES-Passwort vor Authentifizierung
  - Verwendet `config.SambaPasswordAes` für SMB-Verbindungen

### 5. Migration ✅
- **Status**: ✅ Implementiert
- **Ort**: `AppConfig.Load`-Methode
- **Funktion**: Alte `SambaPassword` automatisch aufteilen

## Build Status

✅ **Build erfolgreich** (2.6s)
- Ziel: net10.0-windows
- Ausgabe: `bin\Debug\net10.0-windows\LRCatalogSync.dll`
- Keine Kompilierungsfehler

## Test Status

⏳ **Noch nicht getestet**
- Laufzeit-Tests: Passwort-Save/Load-Zyklus
- SMB-Verbindungs-Tests: Authentifizierung mit AES-Passwort
- Migrations-Tests: Alte Konfigurationsdateien

## Risiken (aktualisiert)

| Risiko | Status | Mitigation |
|--------|--------|------------|
| Verlust des Verschlüsselungsschlüssels | ⚠️ Mittel | Schlüssel in Umgebungsvariable auslagern |
| Performance-Overhead | ⚠️ Gering | Caching-Strategie testen |
| Kompatibilität mit bestehenden Konfigurationen | ✅ Niedrig | Migration logic implementiert |

## Risiken

| Risiko | Status | Mitigation |
|--------|--------|------------|
| Verlust des Verschlüsselungsschlüssels | ⚠️ Mittel | Schlüssel in Umgebungsvariable auslagern |
| Performance-Overhead | ⚠️ Gering | Caching-Strategie testen |
| Kompatibilität mit bestehenden Konfigurationen | ⚠️ Mittel | Migration logic sorgfältig testen |

## Zeitplan (aktualisiert)

| Aufgabe | Geschätzte Dauer | Status |
|---------|------------------|--------|
| AesEncryptor.cs | 0.5 Tage | ⏳ |
| AppConfig.cs | 0.5 Tage | ⏳ |
| SettingsForm.cs | 0.5 Tage | ⏳ |
| Smb.cs | 0.5 Tage | ⏳ |
| Migration | 0.5 Tage | ⏳ |
| Tests | 1 Tag | ⏳ |
| **Gesamt** | **3.5 Tage** | |