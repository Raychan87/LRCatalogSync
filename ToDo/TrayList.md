






🟢 🔴 🟡 ⚪ 🔵 🟠


⚪ NoCfg = LRCatSync: Konfigurationsdateien fehlen! 

🟢 Standby = LRCatSync: wartet auf Änderungen...

🟠 BSyncing = LRCatSync: synchronisiere Lightroom Sicherungsordner.

🟡 LSyncing = LRCatSync: synchronisiere Lightroom Katalog.

🔴 RcloneCfg = LRCatSync: rclone Konfigurationsdatei fehlt!

🔴 RcloneExe = LRCatSync: rclone.exe fehlt!

🔴 Error = LRCatSync: Interner Programm fehler, bitte Log überprüfen!

🔵 Lockfile = LRCatSync: Lightroom Classic ist aktiv.

🔴 NoSamba = LRCatSync: Keine Verbindung zum Samba Server!


026-08-16 11:46:51 [DEBUG] Coordinator: Starte BackupManager
2026-08-16 11:46:51 [DEBUG] BackupManager: gestartet E:\Lightroom_SyDrive\Backups -> synology:/Lightroom/Backups/
2026-08-16 11:46:51 [DEBUG] BackupManager: Bisync-Fehler erkannt, starte mit --resync neu
2026-08-16 11:46:56 [ERROR] BackupManager: Bisync mit --resync fehlgeschlagen (ExitCode: 7)
2026-08-16 11:46:56 [DEBUG] Coordinator: BackupManager abgeschlossen
2026-08-16 11:46:56 [DEBUG] Coordinator: Starte Katalogsync
2026-08-16 11:46:56 [DEBUG] CatalogManager: Starte Versionsvergleich (lokal vs remote)
2026-08-16 11:47:00 [DEBUG] CatalogManager: Remote-Katalog nicht vorhanden → UPLOAD
2026-08-16 11:47:00 [DEBUG] CatalogManager: Sync-Richtung erkannt: Upload
2026-08-16 11:47:00 [DEBUG] CatalogManager: setze Lockfiles
2026-08-16 11:47:00 [DEBUG] SMB: Keine gültige Verbindung erkannt, starte Re-/connect.
2026-08-16 11:47:00 [DEBUG] SMB: Verbindungsversuch 1/3
2026-08-16 11:47:00 [DEBUG] SMB: Verbinden mit Server=192.168.178.5, Share=Lightroom
2026-08-16 11:47:00 [DEBUG] SMB: TCP-Verbindung zu 192.168.178.5 fehlgeschlagen.
2026-08-16 11:47:00 [DEBUG] SMB: Verbindung fehlgeschlagen, warte 1000ms vor erneutem Versuch.
2026-08-16 11:47:01 [DEBUG] SMB: Verbindungsversuch 2/3
2026-08-16 11:47:01 [DEBUG] SMB: Verbinden mit Server=192.168.178.5, Share=Lightroom
2026-08-16 11:47:01 [DEBUG] SMB: TCP-Verbindung zu 192.168.178.5 fehlgeschlagen.
2026-08-16 11:47:01 [DEBUG] SMB: Verbindung fehlgeschlagen, warte 2000ms vor erneutem Versuch.
2026-08-16 11:47:03 [DEBUG] SMB: Verbindungsversuch 3/3
2026-08-16 11:47:03 [DEBUG] SMB: Verbinden mit Server=192.168.178.5, Share=Lightroom
2026-08-16 11:47:03 [DEBUG] SMB: TCP-Verbindung zu 192.168.178.5 fehlgeschlagen.
2026-08-16 11:47:03 [ERROR] SMB: Verbindung nach 3 Versuchen fehlgeschlagen.
2026-08-16 11:47:03 [ERROR] LockManager: SMB-Verbindung fehlgeschlagen, kein Remote-Lock möglich
2026-08-16 11:47:03 [ERROR] CatalogManager: Konnte Locks nicht setzen, breche Sync ab
2026-08-16 11:47:03 [DEBUG] LockManager: Heartbeat gestoppt
2026-08-16 11:47:03 [DEBUG] SMB: Keine gültige Verbindung erkannt, starte Re-/connect.
2026-08-16 11:47:03 [DEBUG] SMB: Verbindungsversuch 1/3
2026-08-16 11:47:03 [DEBUG] SMB: Verbinden mit Server=192.168.178.5, Share=Lightroom
2026-08-16 11:47:03 [DEBUG] SMB: TCP-Verbindung zu 192.168.178.5 fehlgeschlagen.
2026-08-16 11:47:03 [DEBUG] SMB: Verbindung fehlgeschlagen, warte 1000ms vor erneutem Versuch.
2026-08-16 11:47:04 [DEBUG] SMB: Verbindungsversuch 2/3
2026-08-16 11:47:04 [DEBUG] SMB: Verbinden mit Server=192.168.178.5, Share=Lightroom
2026-08-16 11:47:04 [DEBUG] SMB: TCP-Verbindung zu 192.168.178.5 fehlgeschlagen.
2026-08-16 11:47:04 [DEBUG] SMB: Verbindung fehlgeschlagen, warte 2000ms vor erneutem Versuch.
2026-08-16 11:47:06 [DEBUG] SMB: Verbindungsversuch 3/3
2026-08-16 11:47:06 [DEBUG] SMB: Verbinden mit Server=192.168.178.5, Share=Lightroom
2026-08-16 11:47:06 [DEBUG] SMB: TCP-Verbindung zu 192.168.178.5 fehlgeschlagen.
2026-08-16 11:47:06 [ERROR] SMB: Verbindung nach 3 Versuchen fehlgeschlagen.
2026-08-16 11:47:06 [NOTICE] LockManager: Alle Locks freigegeben (SyncGuid: 9d01de70-c3fc-4fc1-a530-ab92c6b8287a)
2026-08-16 11:47:06 [DEBUG] CatalogManager: Sync erfolgreich abgeschlossen
2026-08-16 11:47:06 [DEBUG] Coordinator: CatalogManager abgeschlossen
2026-08-16 11:47:06 [DEBUG] Coordinator: Sync-Zyklus komplett abgeschlossen









