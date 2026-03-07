using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace LRCatalogSync
{
    public partial class SettingsForm : Form
    {
        private AppConfig config;
        private string baseDir;
        private string configFilePath;
        private string rcloneConfigPath;
        private string originalPassword; // Speichert das ursprüngliche verschlüsseltes Passwort

        public SettingsForm(AppConfig cfg, string basePath)
        {
            InitializeComponent();
            config = cfg;
            baseDir = basePath;
            configFilePath = Path.Combine(baseDir, "data", "config", "LRCatSync.conf");
            rcloneConfigPath = Path.Combine(baseDir, "data", "config", "rclone.conf");
            originalPassword = cfg.SambaPassword; // Speichern des ursprünglichen Passworts

            SetupControls();
            LoadSettings();
        }

        private void SetupControls()
        {
            this.Text = "LRCatalogSync - Fototour-und-Technik.de";
            this.Size = new System.Drawing.Size(510, 550);
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

            int yPos = 10;
            const int labelWidth = 125;
            const int controlWidth = 300;
            const int lineHeightToHeading = 8;
            const int lineHeight = 25;

            AddInfoText(scrollPanel, "LRCatalogSync Einstellungen", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight-5;
            
            AddLabelAndTextBox(scrollPanel, "Rclone Pfad:", ref yPos, "txtRcloneFolder", config.RcloneFolder, labelWidth, controlWidth, true);
            yPos += 22;
            AddInfoRclone(scrollPanel, "Download von rclone (https://rclone.org/downloads)", ref yPos, labelWidth + 17);
            yPos += lineHeight;
            
            AddLabelAndComboBox(scrollPanel, "Log-Level:", ref yPos, "cmbLogLevel", new[] { "Aus", "Debug", "Info", "Warn", "Error" }, config.LogLevel, labelWidth, controlWidth - 200);
            yPos += lineHeight;
            
            AddInfoText(scrollPanel, "Lightroom Katalog", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight - 5;
            AddCheckBox(scrollPanel, "*Previews.lrdata syncen (Nicht sinnvoll)", ref yPos, "chkSyncPreviewData", config.SyncPreviewData, labelWidth);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Lokaler Pfad:", ref yPos, "txtLocalPath", config.LocalPath, labelWidth, controlWidth, true);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Remote Pfad:", ref yPos, "txtRemotePath", config.RemotePath, labelWidth, controlWidth, false);
            yPos += lineHeight;
            
            AddInfoText(scrollPanel, "Lightroom Katalog Sicherungsordner", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "_________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight - 5;            
            AddCheckBox(scrollPanel, "Sicherungsordner aktivieren", ref yPos, "chkEnableBackups", config.EnableBackups, labelWidth);
            yPos += lineHeight;            
            AddLabelAndTextBox(scrollPanel, "Lokaler Backup Pfad:", ref yPos, "txtBackupsLocalPath", config.BackupsLocalPath, labelWidth, controlWidth, true);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Remote Backup Pfad:", ref yPos, "txtBackupsRemotePath", config.BackupsRemotePath, labelWidth, controlWidth, false);
            yPos += lineHeight;
            
            AddInfoText(scrollPanel, "Samba Server Einstellungen", ref yPos, 10);
            yPos += lineHeightToHeading;
            AddInfoText(scrollPanel, "________________________________________________________________________________________________", ref yPos, 10);
            yPos += lineHeight - 5;
            
            AddLabelAndTextBox(scrollPanel, "Remote Server IP/Name:", ref yPos, "txtRemoteIP", config.RemoteIP, labelWidth, controlWidth, false);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Remote Benutzer:", ref yPos, "txtSambaUser", config.SambaUser, labelWidth, controlWidth, false);
            yPos += lineHeight;
            AddLabelAndTextBox(scrollPanel, "Remote Passwort:", ref yPos, "txtSambaPassword", "", labelWidth, controlWidth, false, true);
            yPos += 30;

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
            AddLinkLabel(btnPanel, "Fototour und Technik", "https://Fototour-und-Technik.de", 10, 35);

            // Buttons auf der rechten Seite
            var btnSave = new Button
            {
                Text = "Speichern",
                Width = 100,
                Height = 35,
                Left = 265,
                Top = 12
            };
            btnSave.Click += BtnSave_Click;
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
            if (passwordControl.Length > 0 && !string.IsNullOrEmpty(originalPassword))
            {
                ((TextBox)passwordControl[0]).Text = "****";
            }
        }

        private void AddLabelAndTextBox(Panel panel, string labelText, ref int yPos, string controlName, string value, int labelWidth, int controlWidth, bool isPathField, bool isPassword = false)
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

            var textBox = new TextBox
            {
                Name = controlName,
                Text = value,
                Left = 10 + labelWidth + 10,
                Top = yPos,
                Width = controlWidth,
                Height = 24
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
                    Left = 10 + labelWidth + 10 + controlWidth + 5,
                    Top = yPos,
                    Width = 35,
                    Height = 24
                };
                btnBrowse.Click += (s, e) =>
                {
                    string path = BrowseFolder();
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
                Left = 10 + labelWidth + 10,
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
                    return dialog.SelectedPath;
                }
            }
            return null;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                config.RcloneFolder = GetControlValue("txtRcloneFolder");
                config.LocalPath = GetControlValue("txtLocalPath");
                config.BackupsLocalPath = GetControlValue("txtBackupsLocalPath");
                config.BackupsRemotePath = GetControlValue("txtBackupsRemotePath");
                config.EnableBackups = GetCheckBoxValue("chkEnableBackups");
                config.SyncPreviewData = GetCheckBoxValue("chkSyncPreviewData");
                config.RemoteIP = GetControlValue("txtRemoteIP");
                config.RemotePath = GetControlValue("txtRemotePath");
                config.SambaUser = GetControlValue("txtSambaUser");
                config.LogLevel = GetControlValue("cmbLogLevel");

                // ================= VALIDIERUNG 1: rclone.exe prüfen =================
                string rcloneFolder = config.RcloneFolder;

                // Konvertiere zu absolutem Pfad
                string absoluteRcloneFolder = rcloneFolder;
                if (!Path.IsPathRooted(rcloneFolder))
                {
                    absoluteRcloneFolder = Path.GetFullPath(Path.Combine(baseDir, rcloneFolder));
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

                // ================= VALIDIERUNG 2: Lokaler Pfad und *.lrcat prüfen =================
                if (string.IsNullOrEmpty(config.LocalPath))
                {
                    MessageBox.Show(
                        "Fehler: Der lokale Pfad ist erforderlich!",
                        "Lokaler Pfad fehlt",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (!Directory.Exists(config.LocalPath))
                {
                    MessageBox.Show(
                        $"Fehler: Der lokale Pfad existiert nicht!\n\nPfad: {config.LocalPath}",
                        "Lokaler Pfad existiert nicht",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // Prüfe auf *.lrcat Datei
                string[] lrcatFiles = Directory.GetFiles(config.LocalPath, "*.lrcat", SearchOption.TopDirectoryOnly);
                if (lrcatFiles.Length == 0)
                {
                    MessageBox.Show(
                        $"Fehler: Keine Lightroom Katalog (*.lrcat) in diesem Verzeichnis gefunden!\n\nPfad: {config.LocalPath}\n\nBitte wählen Sie den korrekten Lightroom Katalog Ordner.",
                        "Lightroom Katalog nicht gefunden",
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

                // ================= VALIDIERUNG 4: Passwort verschlüsseln =================
                string passwordInput = GetControlValue("txtSambaPassword");

                // Überprüfe, ob das ursprüngliche Passwort bereits verschlüsselt ist
                if (string.IsNullOrEmpty(passwordInput) || passwordInput == "****")
                {
                    // Wenn kein neues Passwort eingegeben wurde, behalte das alte
                    // Aber stelle sicher, dass es korrekt verarbeitet wird
                    if (!string.IsNullOrEmpty(originalPassword))
                    {
                        // Verwende das ursprüngliche Passwort
                        config.SambaPassword = originalPassword;
                    }
                }
                else
                {
                    // Neues Passwort eingegeben - verschlüssele es mit dem validierten rclone Pfad
                    config.SambaPassword = ObscurePassword(passwordInput, absoluteRclonePath);
                }

                // Stelle sicher, dass data/config Ordner existiert
                string configDir = Path.Combine(baseDir, "data", "config");
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                config.Save(configFilePath);
                SaveRcloneConfig();

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

        private string ObscurePassword(string password, string rcloneExePath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = rcloneExePath,
                    Arguments = $"obscure \"{password}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (Process p = Process.Start(psi))
                {
                    string result = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Fehler beim Verschlüsseln des Passworts: {ex.Message}");
                throw;
            }
        }

        private void SaveRcloneConfig()
        {
            string[] lines = new string[]
            {
                "[synology]",
                "type = smb",
                $"host = {config.RemoteIP}",
                $"user = {config.SambaUser}",
                $"pass = {config.SambaPassword}"
            };

            File.WriteAllLines(rcloneConfigPath, lines);
            Log.Debug("rclone.conf erfolgreich erstellt");
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