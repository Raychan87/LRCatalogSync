using System.Diagnostics;

using LRCatalogSync.Infrastructure;    // ← für AppConfig

namespace LRCatalogSync.UI
{
    // Benutzerdefinierte TextBox mit Watermark-Unterstützung
    public class WatermarkTextBox : TextBox
    {
        private string _watermark = string.Empty;
        private Color _watermarkColor = Color.Gray;
        private Label? _watermarkLabel;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Watermark
        {
            get => _watermark;
            set
            {
                _watermark = value;
                UpdateWatermarkLabel();
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color WatermarkColor
        {
            get => _watermarkColor;
            set
            {
                _watermarkColor = value;
                UpdateWatermarkLabel();
            }
        }

        public WatermarkTextBox()
        {
            // Erstelle das Watermark-Label
            _watermarkLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = _watermarkColor,
                BackColor = Color.Transparent,
                Visible = false,
                Cursor = Cursors.IBeam // Zeiger auf Text-Eingabe setzen
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_watermarkLabel != null)
            {
                // Füge das Label als Child hinzu
                Controls.Add(_watermarkLabel);
                _watermarkLabel.BringToFront();
                // Verhindere, dass das Label Klicks abfängt
                _watermarkLabel.Click += (s, e) => this.Focus();
                _watermarkLabel.MouseDown += (s, e) => this.Focus();
                UpdateWatermarkLabel();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateWatermarkLabel();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            UpdateWatermarkLabel();
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            UpdateWatermarkLabel();
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            UpdateWatermarkLabel();
        }

        private void UpdateWatermarkLabel()
        {
            if (_watermarkLabel == null || string.IsNullOrEmpty(_watermark))
                return;

            // Watermark anzeigen, wenn Text leer ist
            _watermarkLabel.Visible = string.IsNullOrEmpty(Text);
            
            if (_watermarkLabel.Visible)
            {
                _watermarkLabel.Text = _watermark;
                _watermarkLabel.Font = this.Font;
                _watermarkLabel.Size = this.Size;
                // Positionierung mit Padding
                _watermarkLabel.Location = new Point(this.Padding.Left, this.Padding.Top);
                _watermarkLabel.Width = this.Width - this.Padding.Horizontal;
            }
        }
    }

    public partial class SettingsForm : Form
    {
        private AppConfig config;
        private string originalPasswordRclone; // Speichert das ursprüngliche rclone-verschlüsselte Passwort
        private string originalPasswordAes; // Speichert das ursprüngliche AES-verschlüsselte Passwort

        public SettingsForm(AppConfig cfg)
        {
            InitializeComponent();
            config = cfg;
            originalPasswordRclone = cfg.SambaPasswordRclone; // Speichern des ursprünglichen Rclone-Passworts
            originalPasswordAes = cfg.SambaPasswordAes; // Speichern des ursprünglichen AES-Passworts

            SetupControls();
            LoadSettings();
        }

        private void SetupControls()
        {
            this.Text = "LRCatalogSync - Fototour-und-Technik.de";
            this.Size = new System.Drawing.Size(510, 620);
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

            AddInfoText(scrollPanel, "LRCatalogSync Einstellungen", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight-20;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight-5;
            AddCheckBox(scrollPanel, "Automatisch beim Systemstart ausführen", ref yPos, "chkAutoRun", config.AutoRun, labelWidth);
            yPos += lineHeight;            
            AddLabelAndTextBox(scrollPanel, "Rclone Verzeichnispfad:", ref yPos, "txtRcloneFolder", config.RcloneFolder, labelWidth, controlWidth, true, false, "z.B. C:\\Program Files\\rclone");
            yPos += 22;
            AddInfoRclone(scrollPanel, "Download von rclone (https://rclone.org/downloads)", ref yPos, labelWidth +10);
            yPos += lineHeight;
            AddLabelAndComboBox(scrollPanel, "Log-Level:", ref yPos, "cmbLogLevel", new[] { "DEBUG", "INFO", "NOTICE", "ERROR" }, config.LogLevel, labelWidth, controlWidth - 200);
            yPos += lineHeight;
            
            AddInfoText(scrollPanel, "Lightroom Katalog", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight - 5;
            AddCheckBox(scrollPanel, "*Previews.lrdata synchronisieren?", ref yPos, "chkSyncPreviewData", config.SyncPreviewData, labelWidth);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "lokale Katalog Datei:", ref yPos, "txtCatalogLocalFile", config.CatalogLocalFile, labelWidth, controlWidth, true, false, "Pfad zur .lrcat Datei auswählen");
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Remote Katalog Pfad:", ref yPos, "txtCatalogRemotePath", config.CatalogRemotePath, labelWidth, controlWidth, false, false, "z.B. remote:catalog/");
            yPos += lineHeight;
            AddCheckBox(scrollPanel, "letzten Katalog behalten?", ref yPos, "chkEnableRcloneCopy", config.EnableRcloneCopy, labelWidth);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Ordnername:", ref yPos, "txtRcloneCopyFolderName", config.RcloneCopyFolderName, labelWidth, controlWidth, false, false, "z.B. backup_v1");
            yPos += lineHeight;

            AddInfoText(scrollPanel, "Lightroom Katalog Sicherungsordner", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight - 5;            
            AddCheckBox(scrollPanel, "Sicherungsordner aktivieren", ref yPos, "chkEnableBackups", config.EnableBackups, labelWidth);
            yPos += lineHeight;            
            AddLabelAndTextBox(scrollPanel, "Lokaler Backup Pfad:", ref yPos, "txtBackupsLocalPath", config.BackupsLocalPath, labelWidth, controlWidth, true, false, "Lokaler Ordner auswählen");
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Remote Backup Pfad:", ref yPos, "txtBackupsRemotePath", config.BackupsRemotePath, labelWidth, controlWidth, false, false, "z.B. remote:backups/");
            yPos += lineHeight;
            
            AddInfoText(scrollPanel, "Samba Server Einstellungen", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "________________________________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight - 5;
            AddLabelAndTextBox(scrollPanel, "Server IP/Name:", ref yPos, "txtRemoteIP", config.RemoteIP, labelWidth, controlWidth, false, false, "z.B. 192.168.1.100");
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Benutzername:", ref yPos, "txtSambaUser", config.SambaUser, labelWidth, controlWidth, false, false, "Benutzername für Samba");
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Passwort:", ref yPos, "txtSambaPassword", "", labelWidth, controlWidth, false, true, "Passwort eingeben");
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

        private void AddLabelAndTextBox(Panel panel, string labelText, ref int yPos, string controlName, string value, int labelWidth, int controlWidth, bool isPathField, bool isPassword = false, string watermark = "")
        {
            var label = new Label
            {
                Text = labelText,
                Left = 10,
                Top = yPos,
                Width = labelWidth,
                Height = 20,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                AutoSize = false
            };
            panel.Controls.Add(label);

            var textBox = new WatermarkTextBox
            {
                Name = controlName,
                Text = value,
                Left = labelWidth + 10,
                Top = yPos,
                Width = controlWidth,
                Height = 24,
                Watermark = watermark
            };

            if (isPassword)
            {
                textBox.UseSystemPasswordChar = true;
            }

            panel.Controls.Add(textBox);

            if (isPathField)
            {
                var btnBrowse = new Button
                {
                    Text = "...",
                    Left = labelWidth + 10 + controlWidth,
                    Top = yPos,
                    Width = 35,
                    Height = 24
                };
                btnBrowse.Click += (s, e) =>
                {
                    string path = "";
                    
                    // Für Katalog-Datei: File-Dialog verwenden
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
                Name = controlName,
                Text = labelText,
                Checked = isChecked,
                Left = 10 + labelWidth + 10,
                Top = yPos,
                Width = 300,
                Height = 20,
                AutoSize = false
            };
            panel.Controls.Add(checkBox);

            return checkBox;
        }

        private void AddInfoRclone(Panel panel, string infoText, ref int yPos, int leftPosition)
        {
            var infoLabel = new Label
            {
                Text = infoText,
                Left = leftPosition,
                Top = yPos,
                Width = 300,
                Height = 20,
                ForeColor = System.Drawing.Color.FromArgb(0, 120, 215),
                AutoSize = false
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
                Text = infoText,
                Left = leftPosition,
                Top = yPos,
                Width = 300,
                Height = 20,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            panel.Controls.Add(infoLabel);
        }

        private void AddLabelAndComboBox(Panel panel, string labelText, ref int yPos, string controlName, string[] items, string selectedValue, int labelWidth, int controlWidth)
        {
            var label = new Label
            {
                Text = labelText,
                Left = 10,
                Top = yPos,
                Width = labelWidth,
                Height = 20,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                AutoSize = false
            };
            panel.Controls.Add(label);

            var comboBox = new ComboBox
            {
                Name = controlName,
                Left = labelWidth + 10,
                Top = yPos,
                Width = controlWidth,
                Height = 24,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            foreach (string item in items)
            {
                comboBox.Items.Add(item);
            }

            comboBox.SelectedItem = selectedValue;
            panel.Controls.Add(comboBox);
        }

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

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ResumeLayout(false);
        }
    }
}