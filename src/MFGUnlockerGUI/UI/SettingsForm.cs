using System;
using System.Drawing;
using System.Windows.Forms;
using MFGUnlockerGUI.Core;

namespace MFGUnlockerGUI.UI
{
    public class SettingsForm : Form
    {
        private readonly StateStore _state;
        private readonly NumericUpDown _keep = new NumericUpDown();

        public SettingsForm(StateStore state)
        {
            _state = state;
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            Ui.Dpi(this);
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var label = new Label { Text = "Backups to keep for each game:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 8, 0) };
            _keep.Minimum = 1; _keep.Maximum = 50;
            _keep.Value = Math.Max(1, Math.Min(50, state.Settings.BackupsToKeep));
            _keep.Width = 60;
            var keepRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
            keepRow.Controls.AddRange(new Control[] { label, _keep });
            var note = new Label
            {
                Text = "Backups hold files this tool moved out of the way. Older ones are cleaned up past this number.",
                AutoSize = true, MaximumSize = new Size(390, 0), Margin = new Padding(0, 10, 0, 0)
            };
            var ok = Ui.Btn("Save", DialogResult.OK);
            var cancel = Ui.Btn("Cancel", DialogResult.Cancel);
            AcceptButton = ok; CancelButton = cancel;
            var table = Ui.Column();
            table.AutoSize = true; table.AutoSizeMode = AutoSizeMode.GrowAndShrink; table.Dock = DockStyle.None;
            Ui.AddAuto(table, keepRow);
            Ui.AddAuto(table, note);
            Ui.AddAuto(table, Ui.ButtonRow(ok, cancel));
            Controls.Add(table);
            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.OK) return;
                try
                {
                    _state.Settings.BackupsToKeep = (int)_keep.Value;
                    _state.SaveSettings();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't save the setting: " + ex.Message, "Settings");
                    e.Cancel = true;
                }
            };
        }
    }
}
