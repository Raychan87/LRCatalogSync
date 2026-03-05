using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace LightroomSync
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
            configFilePath = Path.Combine(baseDir, "LRSync.conf");
            rcloneConfigPath = Path.Combine(baseDir, "rclone.conf");
            originalPassword = cfg.SambaPassword; // Speichern des ursprünglichen Passworts

            SetupControls();
            LoadSettings();
        }

        private void SetupControls()
        {
            this.Text = "Lightroom Sync - Einstellungen";
            this.Size = new System.Drawing.Size(600, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Panel für Scrolling
            var scrollPanel = new Panel
            {
                AutoScroll = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            this.Controls.Add(scrollPanel);

            int yPos = 10;
            const int labelWidth = 150;
            const int controlWidth = 380;
            const int lineHeight = 35;

            // 1. Rclone Pfad
            AddLabelAndTextBox(scrollPanel, "Rclone Pfad:", ref yPos, "txtRclonePath", config.RcloneRelativePath, lineHeight, labelWidth, controlWidth, true);

            // 2. LocalPath
            AddLabelAndTextBox(scrollPanel, "Lokaler Pfad:", ref yPos, "txtLocalPath", config.LocalPath, lineHeight, labelWidth, controlWidth, true);

            // 3. BackupsRelativePath
            AddLabelAndTextBox(scrollPanel, "Backups Pfad:", ref yPos, "txtBackupsPath", config.BackupsRelativePath, lineHeight, labelWidth, controlWidth, false);

            // 4. RemoteIP (Samba)
            AddLabelAndTextBox(scrollPanel, "Samba Server IP:", ref yPos, "txtRemoteIP", config.RemoteIP, lineHeight, labelWidth, controlWidth, false);

            // 5. RemotePath
            AddLabelAndTextBox(scrollPanel, "Remote Pfad:", ref yPos, "txtRemotePath", config.RemotePath, lineHeight, labelWidth, controlWidth, false);

            // 6. SambaUser
            AddLabelAndTextBox(scrollPanel, "Samba Benutzer:", ref yPos, "txtSambaUser", config.SambaUser, lineHeight, labelWidth, controlWidth, false);

            // 7. SambaPassword (mit Sternen, aber nicht laden)
            AddLabelAndTextBox(scrollPanel, "Samba Passwort:", ref yPos, "txtSambaPassword", "", lineHeight, labelWidth, controlWidth, false, true);

            // 8. LogLevel (Dropdown)
            AddLabelAndComboBox(scrollPanel, "Log-Level:", ref yPos, "cmbLogLevel", new[] { "Debug", "Info", "Warn", "Error" }, config.LogLevel, lineHeight, labelWidth, controlWidth);

            // Abstand vor Buttons
            yPos += 30;

            // Buttons
            var btnPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = System.Drawing.SystemColors.Control
            };
            this.Controls.Add(btnPanel);

            var btnSave = new Button
            {
                Text = "Speichern",
                Width = 100,
                Height = 35,
                Left = 150,
                Top = 7
            };
            btnSave.Click += BtnSave_Click;
            btnPanel.Controls.Add(btnSave);

            var btnCancel = new Button
            {
                Text = "Abbrechen",
                DialogResult = DialogResult.Cancel,
                Width = 100,
                Height = 35,
                Left = 350,
                Top = 7
            };
            btnPanel.Controls.Add(btnCancel);

            this.CancelButton = btnCancel;
        }

        private void LoadSettings()
        {
            // Passwort-Feld: Wenn vorhanden, zeige "****"
            var passwordControl = this.Controls.Find("txtSambaPassword", true);
            if (passwordControl.Length > 0 && !string.IsNullOrEmpty(originalPassword))
            {
                ((TextBox)passwordControl[0]).Text = "****";
            }
        }

        private void AddLabelAndTextBox(Panel panel, string labelText, ref int yPos, string controlName, string value, int lineHeight, int labelWidth, int controlWidth, bool isPathField, bool isPassword = false)
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

            yPos += lineHeight;
        }

        private void AddLabelAndComboBox(Panel panel, string labelText, ref int yPos, string controlName, string[] items, string selectedValue, int lineHeight, int labelWidth, int controlWidth)
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

            yPos += lineHeight;
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
                // Werte auslesen
                config.RcloneRelativePath = GetControlValue("txtRclonePath");
                config.LocalPath = GetControlValue("txtLocalPath");
                config.BackupsRelativePath = GetControlValue("txtBackupsPath");
                config.RemoteIP = GetControlValue("txtRemoteIP");
                config.RemotePath = GetControlValue("txtRemotePath");
                config.SambaUser = GetControlValue("txtSambaUser");
                config.LogLevel = GetControlValue("cmbLogLevel");

                // ================= VALIDIERUNG 1: rclone.exe prüfen =================
                string rclonePath = config.RcloneRelativePath;
                
                // Füge "rclone.exe" hinzu, falls nicht vorhanden
                if (!rclonePath.EndsWith("rclone.exe", StringComparison.OrdinalIgnoreCase))
                {
                    rclonePath = Path.Combine(rclonePath, "rclone.exe");
                }

                // Konvertiere zu absolutem Pfad
                string absoluteRclonePath = rclonePath;
                if (!Path.IsPathRooted(rclonePath))
                {
                    absoluteRclonePath = Path.GetFullPath(Path.Combine(baseDir, rclonePath));
                }

                // Überprüfe ob rclone.exe existiert
                if (!File.Exists(absoluteRclonePath))
                {
                    MessageBox.Show(
                        $"Fehler: rclone.exe nicht gefunden!\n\nPfad: {absoluteRclonePath}\n\nBitte überprüfen Sie den Pfad.",
                        "rclone.exe nicht gefunden",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return; // Fenster bleibt offen
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

                // ================= VALIDIERUNG 3: Passwort verschlüsseln =================
                string passwordInput = GetControlValue("txtSambaPassword");

                if (string.IsNullOrEmpty(passwordInput) || passwordInput == "****")
                {
                    // Verwende das ursprüngliche Passwort
                    config.SambaPassword = originalPassword;
                }
                else
                {
                    // Neues Passwort eingegeben - verschlüssele es mit dem validierten rclone Pfad
                    config.SambaPassword = ObscurePassword(passwordInput, absoluteRclonePath);
                }

                // Config speichern
                config.Save(configFilePath);

                // rclone.conf erzeugen
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
            Log.Info("rclone.conf erfolgreich erstellt");
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