using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MFGUnlockerGUI.Core;

namespace MFGUnlockerGUI.UI
{
    /// <summary>"It didn't work": pick what happened, get plain advice, and optionally try the next file name.</summary>
    public class TroubleshootForm : Form
    {
        private readonly Func<GameRecord> _getRecord;
        private readonly GameAnalysis _analysis;
        private readonly Installer _installer;
        private readonly Troubleshooter _ts;
        private readonly AppPaths _paths;
        private readonly BackupManager _backup;
        private readonly string _gpu, _driver, _modVersion;
        private readonly bool _showChoppy, _showUnlockHint;

        private readonly RadioButton _menu = new RadioButton { Text = "The menu didn't appear when I pressed Backspace", Checked = true };
        private readonly RadioButton _crash = new RadioButton { Text = "The game crashes or shows a black screen" };
        private readonly RadioButton _freeze = new RadioButton { Text = "The picture froze" };
        private readonly RadioButton _choppy = new RadioButton { Text = "It works but feels choppy (RTX 30)" };
        private readonly RadioButton _fgMissing = new RadioButton { Text = "The game's own frame generation option is missing" };
        private readonly RadioButton _report = new RadioButton { Text = "None of these: help me report it" };
        private readonly Button _go = Ui.Btn("Check", DialogResult.None, 90);
        private readonly Label _result = new Label { AutoSize = true, MaximumSize = new Size(528, 0), Margin = new Padding(0, 8, 0, 8) };
        private readonly FlowLayoutPanel _actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, MaximumSize = new Size(528, 0), Margin = new Padding(0) };
        private readonly FlowLayoutPanel _links = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        private readonly Button _close = Ui.Btn("Close", DialogResult.Cancel, 90);

        /// <summary>True when something in the game folder changed, so the main window should look again.</summary>
        public bool Changed { get; private set; }
        public bool UninstallRequested { get; private set; }
        /// <summary>True when a rename-to-next-name succeeded in this dialog.</summary>
        public bool Renamed { get; private set; }

        public TroubleshootForm(Func<GameRecord> getRecord, GameAnalysis analysis, Installer installer, Troubleshooter ts,
            AppPaths paths, BackupManager backup, string gpu, string driver, string modVersion, bool showChoppy = false, bool showUnlockHint = false)
        {
            _showChoppy = showChoppy; _showUnlockHint = showUnlockHint;
            _getRecord = getRecord; _analysis = analysis; _installer = installer; _ts = ts;
            _paths = paths; _backup = backup; _gpu = gpu; _driver = driver; _modVersion = modVersion;

            Text = "It didn't work";
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Ui.Dpi(this);
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            CancelButton = _close;

            var head = new Label { Text = "What happened?", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
            var radios = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
            foreach (var r in new[] { _menu, _crash, _freeze, _choppy, _fgMissing, _report })
            {
                r.AutoSize = true;
                radios.Controls.Add(r);
            }
            _choppy.Visible = _showChoppy;
            _go.Click += async (s, e) => await Safe(OnGo);
            _go.Margin = new Padding(8, 8, 0, 0);

            var table = Ui.Column();
            table.AutoSize = true; table.AutoSizeMode = AutoSizeMode.GrowAndShrink; table.Dock = DockStyle.None;
            Ui.AddAuto(table, head);
            Ui.AddAuto(table, radios);
            Ui.AddAuto(table, _go);
            Ui.AddAuto(table, _result);
            Ui.AddAuto(table, _actions);
            Ui.AddAuto(table, _links);
            Ui.AddAuto(table, Ui.ButtonRow(_close));
            Controls.Add(table);
        }

        private async Task Safe(Func<Task> f)
        {
            try { await f(); }
            catch (Exception ex) { Say("Something went wrong: " + ex.Message); }
            finally { _go.Enabled = true; }
        }

        private void Say(string text)
        {
            _result.Text = text;
            _actions.Controls.Clear();
            _links.Controls.Clear();
        }

        private void AddAction(string text, Func<Task> click)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 0) };
            b.Click += async (s, e) => { b.Enabled = false; await Safe(click); };
            _actions.Controls.Add(b);
        }

        private string NextName()
        {
            var rec = _getRecord();
            if (rec == null) return null;
            var adv = FilenameAdvisor.Advise(rec.TargetDir, _analysis.Engine, _analysis.CompatExact ? _analysis.Compat : null, rec.FailedNames, rec.WorkingName);
            return adv.Options.Where(o => o.Allowed && !ProxyNames.BinkNames.Contains(o.Name, StringComparer.OrdinalIgnoreCase))
                .Select(o => o.Name).FirstOrDefault();
        }

        private async Task OnGo()
        {
            var rec = _getRecord();
            if (rec == null) { Say("MFG Unlocker isn't installed by this tool in this game."); return; }
            _go.Enabled = false;
            Say("Checking...");

            if (_menu.Checked)
            {
                var verdict = await Task.Run(() => _ts.CheckMenuMissing(rec));
                if (verdict == LogVerdict.Blocked)
                {
                    Say(Troubleshooter.SacBlockedAdvice);
                    AddAction("Help me report it", ReportAsync);
                }
                else if (verdict == LogVerdict.NotLoaded)
                {
                    var next = NextName();
                    if (next == null)
                    {
                        Say("That name wasn't loaded, and there is no other name left to try.");
                        AddAction("Uninstall...", () => { UninstallRequested = true; DialogResult = DialogResult.OK; return Task.FromResult(0); });
                        AddAction("Help me report it", ReportAsync);
                    }
                    else
                    {
                        Say("That name wasn't loaded. Try the next one: " + next + "?\r\n(Close the game first.)");
                        AddAction("Yes, try " + next, () => Rename(next));
                    }
                }
                else
                {
                    var problem = await Task.Run(() => _ts.FindLeftoverOrDxgiProblem(rec));
                    Say("MFG Unlocker did load (a log file was written). "
                        + (problem ?? "Nothing obvious is in the way. Try pressing Backspace again once the game has reached the main menu, or check the in-game graphics settings."));
                    var next = NextName();
                    if (problem != null && next != null) AddAction("Try " + next, () => Rename(next));
                }
            }
            else if (_crash.Checked)
            {
                var next = NextName();
                Say("Some games crash when a file name is picked up too early. " + (next != null ? "You can try the next file name (" + next + "), or remove the mod." : "You can remove the mod again."));
                if (next != null) AddAction("Try " + next, () => Rename(next));
                AddAction("Uninstall...", () => { UninstallRequested = true; DialogResult = DialogResult.OK; return Task.FromResult(0); });
            }
            else if (_freeze.Checked)
            {
                Say(Troubleshooter.FrozenPictureAdvice);
            }
            else if (_choppy.Checked)
            {
                Say("On RTX 30 cards, frame generation can feel uneven. This tool can switch off one setting (flip metering) in the mod's settings file in the game folder. The old file is backed up first.");
                AddAction("Apply the RTX 30 flip metering fix", ApplyFlip);
            }
            else if (_fgMissing.Checked)
            {
                Say(Troubleshooter.FgOptionMissingAdvice);
            }
            else
            {
                await ReportAsync();
            }
            _go.Enabled = true;
        }

        private async Task Rename(string next)
        {
            var res = await Task.Run(() => _installer.MarkFailedAndRename(_analysis.GameRoot, next));
            Changed = Changed || res.Ok;
            Renamed = Renamed || res.Ok;
            Say(res.Ok ? "Renamed to " + next + ". Start the game and press Backspace again, then come back and tell me if it worked."
                         + (_showUnlockHint ? "\r\n\r\n" + SystemInfo.Rtx30UnlockHint : "")
                       : res.Message);
        }

        private async Task ApplyFlip()
        {
            if (MessageBox.Show(this, "This sets a switch in the mod's settings file in the game folder (the old file is backed up first). Go ahead?",
                    "RTX 30 flip metering", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                foreach (Control c in _actions.Controls) c.Enabled = true;
                return;
            }
            var rec = _getRecord();
            var msg = await Task.Run(() => _ts.SetRtx30FlipMetering(rec, _backup));
            Changed = true;
            Say(msg);
        }

        private async Task ReportAsync()
        {
            var rec = _getRecord();
            var folder = await Task.Run(() => _ts.BuildReport(rec, _paths, _gpu, _driver, _modVersion));
            Say("A report folder was made on your PC. Nothing has been sent anywhere. Attach the files in it to a new report on GitHub if you want to share it:\r\n" + folder);
            try { Process.Start("explorer.exe", "\"" + folder + "\""); } catch { }
            AddLink("Report a problem (opens in your browser): " + Troubleshooter.ReportUrl, Troubleshooter.ReportUrl);
            AddLink("Compatibility report (opens in your browser): " + Troubleshooter.CompatReportUrl, Troubleshooter.CompatReportUrl);
        }

        private void AddLink(string text, string url)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, MaximumSize = new Size(528, 0) };
            l.LinkClicked += (s, e) => { try { Process.Start(url); } catch { } };
            _links.Controls.Add(l);
        }
    }
}
