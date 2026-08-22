using System.Diagnostics;
using System.Reflection;

using LRCatalogSync.Infrastructure;

namespace LRCatalogSync.UI
{
    public partial class SettingsForm : Form
    {
        private readonly string appVersion = GetApplicationVersion();
        private AppConfig config;
        private string originalPasswordRclone; // Speichert das ursprüngliche rclone-verschlüsselte Passwort
        private string originalPasswordAes; // Speichert das ursprüngliche AES-verschlüsselte Passwort
        private readonly ToolTip settingsToolTip = new ToolTip();

        public SettingsForm(AppConfig cfg)
        {
            InitializeComponent();
            config = cfg;
            originalPasswordRclone = cfg.SambaPasswordRclone; // Speichern des ursprünglichen Rclone-Passworts
            originalPasswordAes = cfg.SambaPasswordAes; // Speichern des ursprünglichen AES-Passworts

            SetupControls();
            LoadSettings();
        }

        // ==================== EINRICHTUNG DER FORMULAR-CONTROLS ====================
        private void SetupControls()
        {
            this.Text = $"LRCatalogSync v{appVersion} - Fototour-und-Technik.de";
            this.Icon = LoadIcon("LRCatalogSync.Resources.app_icon.ico");
            this.Size = new System.Drawing.Size(510, 650); // Setze die Größe des Formulars (Breite, Höhe)
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = true;

            // Panel für Scrolling
            var scrollPanel = new Panel
            {
                AutoScroll = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            this.Controls.Add(scrollPanel);

            int yPos = 15;
            const int labelWidth = 140;
            const int controlWidth = 300;
            const int lineHeightToHeading = 8;
            const int lineHeight = 25;

            AddInfoText(scrollPanel, "LRCatalogSync - Einstellungen", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight-20;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight-5;
            AddCheckBox(scrollPanel, "Automatisch beim Systemstart ausführen", ref yPos, "chkAutoRun", config.AutoRun, labelWidth);
            yPos += lineHeight;            
            AddLabelAndTextBox(scrollPanel, "Rclone Verzeichnispfad:", ref yPos, "txtRcloneFolder", config.RcloneFolder, labelWidth, controlWidth, true);
            yPos += 22;
            AddInfoRclone(scrollPanel, "Download von rclone (https://rclone.org/downloads)", ref yPos, labelWidth +10);
            yPos += lineHeight;
            AddLabelAndComboBox(scrollPanel, "Log-Level:", ref yPos, "cmbLogLevel", new[] { "DEBUG", "INFO", "NOTICE", "ERROR" }, config.LogLevel, labelWidth, controlWidth - 200);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Aktuallisierungszeit:", ref yPos, "txtGlobalCycleInterval", config.GlobalCycleInterval.ToString(), labelWidth, 35, false, false, "Sekunden", HorizontalAlignment.Right);
            yPos += lineHeight;            
            AddInfoText(scrollPanel, "Lightroom Katalog", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight - 5;
            AddCheckBox(scrollPanel, "*Previews.lrdata synchronisieren?", ref yPos, "chkSyncPreviewData", config.SyncPreviewData, labelWidth);
            yPos += lineHeight + 2;
            AddLabelAndTextBox(scrollPanel, "lokale Katalog Datei:", ref yPos, "txtCatalogLocalFile", config.CatalogLocalFile, labelWidth, controlWidth, true);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Remote Katalog Pfad:", ref yPos, "txtCatalogRemotePath", config.CatalogRemotePath, labelWidth, controlWidth, false);
            yPos += lineHeight;
            AddCheckBox(scrollPanel, "letzten Katalog behalten?", ref yPos, "chkEnableRcloneCopy", config.EnableRcloneCopy, labelWidth);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Ordnername:", ref yPos, "txtRcloneCopyFolderName", config.RcloneCopyFolderName, labelWidth, controlWidth, false);
            yPos += lineHeight;

            AddInfoText(scrollPanel, "Lightroom Katalog Sicherungsordner", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight - 5;            
            AddCheckBox(scrollPanel, "Sicherungsordner aktivieren", ref yPos, "chkEnableBackups", config.EnableBackups, labelWidth);
            yPos += lineHeight + 2;            
            AddLabelAndTextBox(scrollPanel, "Lokaler Backup Pfad:", ref yPos, "txtBackupsLocalPath", config.BackupsLocalPath, labelWidth, controlWidth, true);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Remote Backup Pfad:", ref yPos, "txtBackupsRemotePath", config.BackupsRemotePath, labelWidth, controlWidth, false);
            yPos += lineHeight;
            
            AddInfoText(scrollPanel, "Samba Server Einstellungen", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "________________________________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight - 5;
            AddLabelAndTextBox(scrollPanel, "Server IP/Name:", ref yPos, "txtRemoteIP", config.RemoteIP, labelWidth, controlWidth, false);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Benutzername:", ref yPos, "txtSambaUser", config.SambaUser, labelWidth, controlWidth, false);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Passwort:", ref yPos, "txtSambaPassword", "", labelWidth, controlWidth, false, true);
            yPos += lineHeight;
            
            // Button Panel mit Links
            var btnPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = System.Drawing.SystemColors.Control
            };
            this.Controls.Add(btnPanel);

            // Links auf der linken Seite
            AddLinkLabel(btnPanel, "GitHub Project", "https://github.com/Raychan87/LRCatalogSync", 10, 15);
            AddLinkLabel(btnPanel, "© Fototour und Technik", "https://Fototour-und-Technik.de", 10, 35);

            // Buttons auf der rechten Seite
            var btnSave = new Button
            {
                Text = "Speichern",
                Width = 100,
                Height = 35,
                Left = 265,
                Top = 12
            };
            btnSave.Click += (sender, e) => BtnSave_Click(sender, e);
            btnPanel.Controls.Add(btnSave);

            var btnCancel = new Button
            {
                Text = "Abbrechen",
                DialogResult = DialogResult.Cancel,
                Width = 100,
                Height = 35,
                Left = 380,
                Top = 12
            };
            btnPanel.Controls.Add(btnCancel);

            this.CancelButton = btnCancel;
        }

        // ==================== HILFSMETHODEN FÜR FORMULAR-CONTROLS ====================
        private void AddLinkLabel(Panel panel, string text, string url, int left, int top)
        {
            var linkLabel = new LinkLabel
            {
                Text = text,
                Left = left,
                Top = top,
                Width = 230,
                Height = 20,
                AutoSize = false,
                LinkColor = System.Drawing.Color.FromArgb(0, 120, 215),
                VisitedLinkColor = System.Drawing.Color.FromArgb(0, 120, 215),
                LinkBehavior = LinkBehavior.NeverUnderline //Kein Unterstrich
            };
            linkLabel.LinkClicked += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    MessageBox.Show("Link konnte nicht geöffnet werden.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            panel.Controls.Add(linkLabel);
        }

        /// Fügt ein Label und ein TextBox-Control zum Panel hinzu.
        /// Optional können ein Suffix-Label (z.B. "Sekunden") und ein Browse-Button für Datei-/Ordnerauswahl hinzugefügt werden.
        private void AddLabelAndTextBox(Panel panel, string labelText, ref int yPos, string controlName, string value, int labelWidth, int controlWidth, bool isPathField, bool isPassword = false, string suffixText = "", HorizontalAlignment textAlignment = HorizontalAlignment.Left)
        {
            // Label erstellen und hinzufügen
            var label = new Label
            {
                Text = labelText,                                   // Anzeigetext des Labels
                Left = 10,                                          // Horizontale Position (10px vom linken Rand)
                Top = yPos,                                         // Vertikale Position
                Width = labelWidth,                                 // Breite des Labels
                Height = 20,                                        // Höhe des Labels
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft, // Textausrichtung linksbündig
                AutoSize = false                                    // Größe nicht automatisch anpassen
            };
            panel.Controls.Add(label);

            // TextBox erstellen und konfigurieren
            var textBox = new TextBox
            {
                Name = controlName,               // Name des Controls für spätere Wertabfrage
                Text = value,                     // Anfangswert aus der Konfiguration
                Left = labelWidth + 10,           // Position direkt nach dem Label (10px Abstand)
                Top = yPos,                       // Vertikale Position
                Width = controlWidth,             // Breite des Eingabefelds
                Height = 24,                      // Höhe des Eingabefelds
                TextAlign = textAlignment         // Textausrichtung (links, rechts, zentriert)
            };

            // Wenn isPassword true ist, Eingabe maskieren (Passwort-Feld)
            if (isPassword)
            {
                textBox.UseSystemPasswordChar = true;
            }

            settingsToolTip.SetToolTip(textBox, GetToolTipText(controlName));

            panel.Controls.Add(textBox);

            // Wenn suffixText angegeben ist, Label rechts neben der TextBox hinzufügen
            // Dies wird verwendet, um Einheiten wie z.B."Sekunden" anzuzeigen
            if (!string.IsNullOrEmpty(suffixText))
            {
                var suffixLabel = new Label
                {
                    Text = suffixText,                                // Anzeigetext (z.B. "Sekunden")
                    Left = textBox.Right,                         // Position direkt nach der TextBox (8px Abstand)
                    Top = yPos,                                       // Vertikale Position (gleich wie TextBox)
                    Width = 70,                                       // Breite des Suffix-Labels
                    Height = 20,                                      // Höhe des Labels
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft, // Textausrichtung linksbündig
                    AutoSize = false                                  // Größe nicht automatisch anpassen
                };
                panel.Controls.Add(suffixLabel);
            }

            // Wenn isPathField true ist, Browse-Button hinzufügen für Datei-/Ordnerauswahl
            if (isPathField)
            {
                var btnBrowse = new Button
                {
                    Text = "...",                                       // Button-Text mit drei Punkten
                    Left = labelWidth + 10 + controlWidth,            // Position direkt nach der TextBox
                    Top = yPos,                                         // Vertikale Position (gleich wie TextBox)
                    Width = 35,                                         // Breite des Buttons
                    Height = 24                                         // Höhe des Buttons
                };
                btnBrowse.Click += (s, e) =>
                {
                    string path = "";
                    
                    // Unterscheidung zwischen Katalog-Datei (File-Dialog) und anderen Pfeldern (Folder-Dialog)
                    if (controlName == "txtCatalogLocalFile")
                    {
                        path = BrowseFile("Lightroom Katalog-Datei (*.lrcat)|*.lrcat|Alle Dateien (*.*)|*.*");
                    }
                    else
                    {
                        path = BrowseFolder();
                    }
                    
                    if (!string.IsNullOrEmpty(path))
                    {
                        textBox.Text = path;
                    }
                };
                panel.Controls.Add(btnBrowse);
            }
        }

        private CheckBox AddCheckBox(Panel panel, string labelText, ref int yPos, string controlName, bool isChecked, int labelWidth)
        {
            var checkBox = new CheckBox
            {
                Name = controlName,                                 // Name des Controls für spätere Wertabfrage
                Text = labelText,                                   // Anzeigetext des Checkboxes
                Checked = isChecked,                                // Aktueller Status (angehakt oder nicht)
                Left = 10 + labelWidth + 10,                      // Position nach dem Label (10px + Labelbreite + 10px)
                Top = yPos,                                         // Vertikale Position
                Width = 300,                                        // Breite des Checkboxes
                Height = 20,                                        // Höhe des Checkboxes
                AutoSize = false                                    // Größe nicht automatisch anpassen
            };
            settingsToolTip.SetToolTip(checkBox, GetToolTipText(controlName));
            panel.Controls.Add(checkBox);

            return checkBox;
        }

        private string GetToolTipText(string controlName)
        {
            return controlName switch
            {
                "chkAutoRun" => "Startet LRCatalogSync automatisch beim Windows-Systemstart.",
                "txtRcloneFolder" => "Pfad zur rclone-Installation oder zum rclone-Verzeichnis.",
                "txtGlobalCycleInterval" => "Zeit in Sekunden zwischen den automatischen Synchronisationszyklen (1 bis 999).",
                "chkSyncPreviewData" => "Wenn aktiv, wird zusätzlich der Ordner *Previews.lrdata des Lightroom-Katalogs synchronisiert.",
                "txtCatalogLocalFile" => "Lokaler Pfad zur Lightroom-Katalogdatei (.lrcat).",
                "txtCatalogRemotePath" => "Zielpfad auf dem Samba-Server z.B. //192.168.1.100/SambaOrdner/ -> /SambaOrdner/",
                "chkEnableRcloneCopy" => "Behält nach der Synchronisation eine Kopie des letzten Katalogs.",
                "txtRcloneCopyFolderName" => "Name des Ordners für die Kopie des letzten Lightroom-Katalogs.",
                "chkEnableBackups" => "Aktiviert die Sicherung der Sicherungsordner die Lightroom Classic ablegt.",
                "txtBackupsLocalPath" => "Lokaler Pfad zum Sicherungsordner von Lightroom Classic.",
                "txtBackupsRemotePath" => "Zielpfad auf dem entfernten Speicher für die Sicherungsordner von Lightroom Classic.",
                "txtRemoteIP" => "IP-Adresse oder Hostname des Samba-Servers.",
                "txtSambaUser" => "Benutzername für die Verbindung zum Samba-Server.",
                "txtSambaPassword" => "Passwort für die Verbindung zum Samba-Server.",
                _ => string.Empty
            };
        }

        private void AddInfoRclone(Panel panel, string infoText, ref int yPos, int leftPosition)
        {
            var infoLabel = new Label
            {
                Text = infoText,                                    // Anzeigetext (Info-Text mit Link)
                Left = leftPosition,                                // Horizontale Position
                Top = yPos,                                         // Vertikale Position
                Width = 300,                                        // Breite des Labels
                Height = 20,                                        // Höhe des Labels
                ForeColor = System.Drawing.Color.FromArgb(0, 120, 215), // Textfarbe (Blau: RGB 0,120,215)
                AutoSize = false                                    // Größe nicht automatisch anpassen
            };

            // Macht den Text klickbar als Link
            infoLabel.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://rclone.org/downloads/",
                        UseShellExecute = true
                    });
                }
                catch
                {
                    MessageBox.Show("Link konnte nicht geöffnet werden.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            infoLabel.Cursor = System.Windows.Forms.Cursors.Hand;
            panel.Controls.Add(infoLabel);
        }

        private void AddInfoText(Panel panel, string infoText, ref int yPos, int leftPosition)
        {
            var infoLabel = new Label
            {
                Text = infoText,                                    // Anzeigetext (Info-Text)
                Left = leftPosition,                                // Horizontale Position
                Top = yPos,                                         // Vertikale Position
                Width = 300,                                        // Breite des Labels
                Height = 20,                                        // Höhe des Labels
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft, // Textausrichtung linksbündig
                AutoSize = false,                                   // Größe nicht automatisch anpassen
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)     // Fette Schriftart für Überschriften
            };

            panel.Controls.Add(infoLabel);
        }

        private void AddLabelAndComboBox(Panel panel, string labelText, ref int yPos, string controlName, string[] items, string selectedValue, int labelWidth, int controlWidth)
        {
            var label = new Label
            {
                Text = labelText,                                   // Anzeigetext des Labels
                Left = 10,                                          // Horizontale Position (10px vom linken Rand)
                Top = yPos,                                         // Vertikale Position
                Width = labelWidth,                                 // Breite des Labels
                Height = 20,                                        // Höhe des Labels
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft, // Textausrichtung linksbündig
                AutoSize = false                                    // Größe nicht automatisch anpassen
            };
            panel.Controls.Add(label);

            var comboBox = new ComboBox
            {
                Name = controlName,                                 // Name des Controls für spätere Wertabfrage
                Left = labelWidth + 10,                             // Position direkt nach dem Label (10px Abstand)
                Top = yPos,                                         // Vertikale Position
                Width = controlWidth,                               // Breite des ComboBox
                Height = 24,                                        // Höhe des ComboBox
                DropDownStyle = ComboBoxStyle.DropDownList          // Nur Dropdown-Liste, keine manuelle Eingabe
            };

            foreach (string item in items)
            {
                comboBox.Items.Add(item);
            }

            comboBox.SelectedItem = selectedValue;
            panel.Controls.Add(comboBox);
        }

        // ==================== HILFSMETHODEN FÜR DATEI- UND ORDNERDIALOGE ====================

        private string BrowseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Ordner auswählen";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.SelectedPath ?? string.Empty;
                }
            }
            return string.Empty;
        }

        private string BrowseFile(string filter = "Alle Dateien (*.*)|*.*")
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Datei auswählen";
                dialog.Filter = filter;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.FileName ?? string.Empty;
                }
            }
            return string.Empty;
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            try
            {
                config.RcloneFolder = GetControlValue("txtRcloneFolder");
                config.CatalogLocalFile = GetControlValue("txtCatalogLocalFile");
                config.BackupsLocalPath = GetControlValue("txtBackupsLocalPath");
                config.BackupsRemotePath = GetControlValue("txtBackupsRemotePath");
                config.EnableBackups = GetCheckBoxValue("chkEnableBackups");
                config.EnableRcloneCopy = GetCheckBoxValue("chkEnableRcloneCopy");
                config.RcloneCopyFolderName = GetControlValue("txtRcloneCopyFolderName");
                config.SyncPreviewData = GetCheckBoxValue("chkSyncPreviewData");
                config.RemoteIP = GetControlValue("txtRemoteIP");
                config.CatalogRemotePath = GetControlValue("txtCatalogRemotePath");
                config.SambaUser = GetControlValue("txtSambaUser");
                config.LogLevel = GetControlValue("cmbLogLevel");
                config.AutoRun = GetCheckBoxValue("chkAutoRun");

                if (!int.TryParse(GetControlValue("txtGlobalCycleInterval"), out int globalCycleInterval) || globalCycleInterval <= 0)
                {
                    MessageBox.Show(
                        "Fehler: Die Aktualisierungszeit muss eine positive Zahl in Sekunden sein!",
                        "Ungültige Aktualisierungszeit",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // Prüfe ob der Wert im gültigen Bereich liegt (1-999 Sekunden)
                if (globalCycleInterval < 1 || globalCycleInterval > 999)
                {
                    MessageBox.Show(
                        "Fehler: Die Aktualisierungszeit muss zwischen 1 und 999 Sekunden liegen!",
                        "Ungültiger Wertebereich",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                config.GlobalCycleInterval = globalCycleInterval;

                // ================= VALIDIERUNG 1: rclone.exe prüfen =================
                string rcloneFolder = config.RcloneFolder;

                // Konvertiere zu absolutem Pfad
                string absoluteRcloneFolder = rcloneFolder;
                if (!Path.IsPathRooted(rcloneFolder))
                {
                    absoluteRcloneFolder = Path.GetFullPath(Path.Combine(GlobalData.BaseDir, rcloneFolder));
                }

                string absoluteRclonePath = Path.Combine(absoluteRcloneFolder, "rclone.exe");

                // Überprüfe ob rclone.exe existiert
                if (!File.Exists(absoluteRclonePath))
                {
                    MessageBox.Show(
                        $"Fehler: rclone.exe nicht gefunden!\n\nPfad: {absoluteRclonePath}\n\nBitte überprüfen Sie den Pfad.",
                        "rclone.exe nicht gefunden",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // ================= VALIDIERUNG 2: Katalog-Datei prüfen =================
                if (string.IsNullOrEmpty(config.CatalogLocalFile))
                {
                    MessageBox.Show(
                        "Fehler: Die Katalog-Datei ist erforderlich!",
                        "Katalog-Datei fehlt",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (!File.Exists(config.CatalogLocalFile))
                {
                    MessageBox.Show(
                        $"Fehler: Die Katalog-Datei existiert nicht!\n\nPfad: {config.CatalogLocalFile}",
                        "Katalog-Datei existiert nicht",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // Prüfe ob es eine .lrcat Datei ist
                if (!config.CatalogLocalFile.EndsWith(".lrcat", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        $"Fehler: Die ausgewählte Datei ist keine Lightroom Katalog-Datei!\n\nDatei: {config.CatalogLocalFile}\n\nBitte wählen Sie eine *.lrcat Datei.",
                        "Keine .lrcat Datei",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // ================= VALIDIERUNG 3: Backups Pfad prüfen (nur wenn aktiviert) =================
                if (config.EnableBackups)
                {
                    if (string.IsNullOrEmpty(config.BackupsLocalPath))
                    {
                        MessageBox.Show(
                            "Fehler: Der lokale Backup Pfad ist erforderlich wenn Backups aktiviert sind!",
                            "Lokaler Backup Pfad fehlt",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    if (!Directory.Exists(config.BackupsLocalPath))
                    {
                        MessageBox.Show(
                            $"Fehler: Der lokale Backup Pfad existiert nicht!\n\nPfad: {config.BackupsLocalPath}",
                            "Lokaler Backup Pfad existiert nicht",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    if (string.IsNullOrEmpty(config.BackupsRemotePath))
                    {
                        MessageBox.Show(
                            "Fehler: Der Remote Backup Pfad ist erforderlich wenn Backups aktiviert sind!",
                            "Remote Backup Pfad fehlt",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }

                // ================= VALIDIERUNG 3b: rclone copy Ordnername prüfen =================
                if (config.EnableRcloneCopy && string.IsNullOrEmpty(config.RcloneCopyFolderName))
                {
                    MessageBox.Show(
                        "Fehler: Der rclone copy Ordnername darf nicht leer sein!",
                        "Ordnername fehlt",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // ================= VALIDIERUNG 4: Passwort verschlüsseln =================
                string passwordInput = GetControlValue("txtSambaPassword");

                // Überprüfe, ob das ursprüngliche Passwort bereits verschlüsselt ist
                if (string.IsNullOrEmpty(passwordInput) || passwordInput == "****")
                {
                    // Wenn kein neues Passwort eingegeben wurde, behalte die alten Passwörter
                    config.SambaPasswordRclone = originalPasswordRclone;
                    config.SambaPasswordAes = originalPasswordAes;
                }
                else
                {
                    // Neues Passwort eingegeben - verschlüssele es für beide Systeme
                    config.SambaPasswordRclone = ObscurePassword(passwordInput, absoluteRclonePath);
                    config.SambaPasswordAes = Cryptor.Encrypt(passwordInput);
                }

                // Stelle sicher, dass data/config Ordner existiert
                string configDir = Path.Combine(GlobalData.BaseDir, "data", "config");
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                config.Save(GlobalData.LRCatSyncConfigPath);
                SaveRcloneConfig();

                // Autorun aktualisieren
                if (config.AutoRun)
                {
                    string exePath = Application.ExecutablePath;
                    Autorun.Enable(exePath);
                }
                else
                {
                    Autorun.Disable();
                }

                MessageBox.Show("Einstellungen erfolgreich gespeichert!", "Erfolg", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Speichern: {ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetControlValue(string controlName)
        {
            var control = this.Controls.Find(controlName, true);
            if (control.Length > 0)
            {
                if (control[0] is TextBox tb)
                    return tb.Text;
                if (control[0] is ComboBox cb)
                    return cb.SelectedItem?.ToString() ?? "";
            }
            return "";
        }

        private bool GetCheckBoxValue(string controlName)
        {
            var control = this.Controls.Find(controlName, true);
            if (control.Length > 0 && control[0] is CheckBox cb)
                return cb.Checked;
            return false;
        }

        private string ObscurePassword(string? password, string rcloneExePath)
        {
            try
            {
                string passwordArg = password ?? string.Empty;
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = rcloneExePath,
                    Arguments = $"obscure \"{passwordArg}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (Process p = Process.Start(psi)!)
                {
                    string result = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsForm: Verschlüsseln des Passworts: {ex.Message}");
                throw;
            }
        }

        private void SaveRcloneConfig()
        {
            string[] lines = new string[]
            {
                $"[{GlobalConst.REMOTE_NAME}]",
                "type = smb",
                $"host = {config.RemoteIP}",
                $"user = {config.SambaUser}",
                $"pass = {config.SambaPasswordRclone}"
            };

            File.WriteAllLines(GlobalData.RcloneConfigPath, lines);
            Log.Debug("Config: rclone.conf erfolgreich erstellt");
        }

        // ==================== HILFSMETHODEN FÜR VERSION UND EINSTELLUNGEN ====================
        private static string GetApplicationVersion() //Aus Assembly-Informationen auslesen
        {
            // Lese die Version direkt aus der Assembly Information
            var assembly = Assembly.GetExecutingAssembly();
            var versionAttr = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
            if (versionAttr?.InformationalVersion != null)
            {
                // Entferne den Hash-Teil nach dem "+"
                string version = versionAttr.InformationalVersion;
                int plusIndex = version.IndexOf('+');
                if (plusIndex > 0)
                {
                    version = version.Substring(0, plusIndex);
                }
                return version;
            }
            
            // Fallback auf FileVersion
            return FileVersionInfo.GetVersionInfo(AppContext.BaseDirectory + "LRCatalogSync.exe").ProductVersion ?? "0.0.0.0";
        }

        private static Icon LoadIcon(string resourceName)
        {
            using var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Icon-Ressource nicht gefunden: {resourceName}");
            using var icon = new Icon(stream);
            return new Icon(icon, icon.Width, icon.Height);
        }
        
        private void LoadSettings()
        {
            var passwordControl = this.Controls.Find("txtSambaPassword", true);
            if (passwordControl.Length > 0 && (!string.IsNullOrEmpty(originalPasswordRclone) || !string.IsNullOrEmpty(originalPasswordAes)))
            {
                ((TextBox)passwordControl[0]).Text = "****";
            }
            
            // Setze Standardwerte für rclone copy, falls noch nicht gesetzt
            var chkEnableRcloneCopy = this.Controls.Find("chkEnableRcloneCopy", true);
            if (chkEnableRcloneCopy.Length > 0)
            {
                ((CheckBox)chkEnableRcloneCopy[0]).Checked = config.EnableRcloneCopy;
            }
            
            var txtRcloneCopyFolderName = this.Controls.Find("txtRcloneCopyFolderName", true);
            if (txtRcloneCopyFolderName.Length > 0)
            {
                ((TextBox)txtRcloneCopyFolderName[0]).Text = config.RcloneCopyFolderName;
            }

            // Setze Autorun Checkbox
            var chkAutoRun = this.Controls.Find("chkAutoRun", true);
            if (chkAutoRun.Length > 0)
            {
                ((CheckBox)chkAutoRun[0]).Checked = config.AutoRun;
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ResumeLayout(false);
        }
    }
}