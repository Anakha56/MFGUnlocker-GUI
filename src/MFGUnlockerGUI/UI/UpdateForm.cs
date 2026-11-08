using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MFGUnlockerGUI.Core;

namespace MFGUnlockerGUI.UI
{
    /// <summary>Shows a newer MFG Unlocker release. Nothing is downloaded unless the user clicks OK.</summary>
    public class UpdateForm : Form
    {
        public UpdateForm(ReleaseInfo r, string currentTag)
        {
            Text = "New MFG Unlocker version";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 360);
            Ui.Dpi(this);
            Font = SystemFonts.MessageBoxFont;

            var head = new Label
            {
                Text = "Version " + r.Tag + " is available (published " + r.PublishedUtc.ToLocalTime().ToString("d MMM yyyy") + ")."
                    + (string.IsNullOrEmpty(currentTag) ? "" : " You have " + currentTag + "."),
                AutoSize = true, MaximumSize = new Size(488, 0), Margin = new Padding(0, 0, 0, 8)
            };
            var notes = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                TabStop = false,
                Text = string.IsNullOrWhiteSpace(r.Notes) ? "(No release notes.)" : ReleaseNotesText.ToPlain(r.Notes).Replace("\n", "\r\n")
            };
            var hint = new Label { Text = "OK downloads it and checks it against the published checksums.", AutoSize = true, MaximumSize = new Size(488, 0), Margin = new Padding(0, 8, 0, 0) };
            var ok = Ui.Btn("OK", DialogResult.OK);
            var no = Ui.Btn("Not now", DialogResult.Cancel);
            AcceptButton = ok; CancelButton = no;
            // nothing selected in the notes, and OK has the focus when the window opens
            Shown += (s, e) => { notes.SelectionStart = 0; notes.SelectionLength = 0; ok.Focus(); };
            var table = Ui.Column();
            Ui.AddAuto(table, head);
            Ui.AddFill(table, notes);
            Ui.AddAuto(table, hint);
            Ui.AddAuto(table, Ui.ButtonRow(ok, no));
            Controls.Add(table);
        }
    }

    /// <summary>A checklist of recorded games to upgrade after a new version is installed.</summary>
    public class UpgradeAllForm : Form
    {
        private readonly CheckedListBox _list = new CheckedListBox { CheckOnClick = true };
        private readonly List<GameRecord> _games;

        public UpgradeAllForm(IEnumerable<GameRecord> games, string tag)
        {
            _games = games.ToList();
            Text = "Upgrade your games?";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 300);
            Ui.Dpi(this);
            Font = SystemFonts.MessageBoxFont;

            var head = new Label
            {
                Text = "MFG Unlocker " + tag + " is installed. Tick the games to upgrade now (close each game first).",
                AutoSize = true, MaximumSize = new Size(488, 0), Margin = new Padding(0, 0, 0, 8)
            };
            _list.Dock = DockStyle.Fill;
            _list.HorizontalScrollbar = true;
            foreach (var g in _games) _list.Items.Add(g.GameRoot + (g.Version != null ? "  (now " + g.Version + ")" : ""), true);
            var ok = Ui.Btn("Upgrade ticked", DialogResult.OK, 100);
            var no = Ui.Btn("Not now", DialogResult.Cancel, 60);
            AcceptButton = ok; CancelButton = no;
            var table = Ui.Column();
            Ui.AddAuto(table, head);
            Ui.AddFill(table, _list);
            Ui.AddAuto(table, Ui.ButtonRow(ok, no));
            Controls.Add(table);
        }

        public IReadOnlyList<GameRecord> Selected
        {
            get { return _games.Where((g, i) => _list.GetItemChecked(i)).ToList(); }
        }
    }
}
