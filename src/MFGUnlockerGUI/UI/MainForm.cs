using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MFGUnlockerGUI.Core;

namespace MFGUnlockerGUI.UI
{
    public class MainForm : Form
    {
        private const string OptiScalerUrl = "https://github.com/dashdogy/MFG-Unlocker/blob/main/docs/optiscaler.md";

        private readonly AppPaths _paths;
        private StateStore _state;
        private BackupManager _backup;
        private GitHubClient _gh;
        private TemplateStore _templates;
        private Installer _installer;
        private Troubleshooter _troubleshooter;
        private GameAnalyzer _analyzer;
        private CompatibilityTable _table;

        private GpuClass _gpuClass = GpuClass.NvidiaRtx20to40;
        private bool _gpuKnown;
        private string _gpuText = "checking...";
        private string _gpuName = "";
        private bool _hasRtx30;
        private bool _hasRtx20or30;
        private bool? _sac;
        private string _driver = "";
        private string _updateStatus = "";

        private GameAnalysis _a;
        private int _busyCount;
        private bool _busy { get { return _busyCount > 0; } }
        private void EndBusy() { if (_busyCount > 0) _busyCount--; }
        private bool _updatingUi;

        // controls
        private readonly Label _statusGpu = new Label { AutoSize = true };
        private readonly Label _statusSac = new Label { AutoSize = true };
        private readonly Label _statusTemplate = new Label { AutoSize = true };
        private readonly Button _checkUpdate = new Button { Text = "Check for update", AutoSize = true };
        private readonly Label _statusAdmin = new Label { AutoSize = true };
        private readonly ComboBox _applied = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        // read-only, but selectable so the path can be copied
        private readonly TextBox _path = new TextBox { ReadOnly = true, BorderStyle = BorderStyle.None, TabStop = false };
        private readonly Button _browse = new Button { Text = "Add a game…", AutoSize = true };
        private bool _hasEntries;
        private bool _folderGone;   // the open game is a saved record whose folder no longer exists
        private readonly ListView _findings = new ListView { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None, MultiSelect = false };
        private readonly ComboBox _exeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
        private readonly Label _exeLabel = new Label { Text = "Game program:", AutoSize = true, Visible = false };
        private readonly Button _adopt = new Button { Text = "Adopt", AutoSize = true, Visible = false };
        private readonly ComboBox _names = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed };
        private readonly CheckBox _bink = new CheckBox { Text = "Use Bink route", AutoSize = true, Visible = false };
        private readonly Label _message = new Label();
        private readonly LinkLabel _link = new LinkLabel { AutoSize = true, Visible = false };
        private readonly Button _install = new Button { Text = "Install", AutoSize = true };
        private readonly Button _worked = new Button { Text = "It worked", AutoSize = true };
        private readonly Button _didnt = new Button { Text = "It didn't work...", AutoSize = true };
        private readonly Button _upgrade = new Button { Text = "Upgrade", AutoSize = true };
        private readonly Button _uninstall = new Button { Text = "Uninstall...", AutoSize = true };
        private readonly Button _forget = new Button { Text = "Forget this game", AutoSize = true };
        private readonly Button _backups = new Button { Text = "Open backups", AutoSize = true };
        private readonly Button _settings = new Button { Text = "Settings", AutoSize = true };
        private int _lastNameIndex = -1;
        private string _linkUrl;

        public MainForm(AppPaths paths)
        {
            _paths = paths;
            Text = "MFGUnlocker GUI";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, 600);
            MinimumSize = new Size(700, 540);
            Ui.Dpi(this);
            Font = SystemFonts.MessageBoxFont;
            BuildLayout();
            UpdateButtons();   // everything stays off until startup has loaded the state
            Shown += async (s, e) => await SafeAsync(StartupAsync);
        }

        // ------------------------------------------------------------ layout

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // status
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // folder
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // findings
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // exe / adopt
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // filename
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // message
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // buttons

            var status = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var tplRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            tplRow.Controls.AddRange(new Control[] { _statusTemplate, _checkUpdate });
            status.Controls.AddRange(new Control[] { _statusGpu, _statusSac, tplRow, _statusAdmin });
            root.Controls.Add(status, 0, 0);

            var folder = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
            folder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folder.Controls.Add(new Label { Text = "Game:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) }, 0, 0);
            _applied.Margin = new Padding(0, 3, 0, 3);
            folder.Controls.Add(_applied, 1, 0);
            _browse.Anchor = AnchorStyles.Left; _browse.Margin = new Padding(8, 0, 0, 0);
            folder.Controls.Add(_browse, 2, 0);
            _path.Dock = DockStyle.Fill; _path.BackColor = BackColor; _path.Margin = new Padding(0, 0, 0, 0);
            folder.Controls.Add(_path, 1, 1);
            root.Controls.Add(folder, 0, 1);

            _findings.Dock = DockStyle.Fill;
            _findings.Columns.Add("Finding", 480);
            _findings.Columns.Add("Where", 280);
            // column 0 is as wide as the longest finding (but column 1 keeps a quarter); see FitFindingColumns
            _findings.ShowItemToolTips = true;
            _findings.SizeChanged += (s, e) => FitFindingColumns();
            _findings.Margin = new Padding(0, 8, 0, 8);
            root.Controls.Add(_findings, 0, 2);

            // "Change game program" and "File name to use" share one grid so their labels
            // and dropdowns line up: label column, stretching dropdown column, extra column.
            var pick = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2 };
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pick.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pick.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var namesLabel = new Label { Text = "File name to use:", AutoSize = true };
            foreach (var l in new[] { _exeLabel, namesLabel }) { l.Anchor = AnchorStyles.Left; l.Margin = new Padding(0, 0, 6, 0); }
            foreach (var c in new Control[] { _exeBox, _names }) { c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 3, 0, 3); }
            _names.ItemHeight = Font.Height + 4;
            _adopt.Anchor = _bink.Anchor = AnchorStyles.Left;
            _adopt.Margin = _bink.Margin = new Padding(8, 0, 0, 0);
            pick.Controls.Add(_exeLabel, 0, 0); pick.Controls.Add(_exeBox, 1, 0); pick.Controls.Add(_adopt, 2, 0);
            pick.Controls.Add(namesLabel, 0, 1); pick.Controls.Add(_names, 1, 1); pick.Controls.Add(_bink, 2, 1);
            root.Controls.Add(pick, 0, 3);

            var msg = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            _message.AutoSize = true; _message.MaximumSize = new Size(780, 0);
            // keep the message wrapping to the window width at any size or scale
            root.SizeChanged += (s, e) => { var w = root.ClientSize.Width - root.Padding.Horizontal; if (w > 100) _message.MaximumSize = new Size(w, 0); };
            msg.Controls.AddRange(new Control[] { _message, _link });
            msg.Margin = new Padding(0, 8, 0, 8);
            root.Controls.Add(msg, 0, 5);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            buttons.Controls.AddRange(new Control[] { _install, _worked, _didnt, _upgrade, _uninstall, _forget, _backups, _settings });
            root.Controls.Add(buttons, 0, 6);
            Controls.Add(root);

            _applied.SelectionChangeCommitted += async (s, e) => await SafeAsync(AppliedChosenAsync);
            _browse.Click += async (s, e) => await SafeAsync(BrowseAsync);
            _exeBox.SelectionChangeCommitted += async (s, e) => await SafeAsync(ExeChangedAsync);
            _adopt.Click += async (s, e) => await SafeAsync(AdoptAsync);
            _names.DrawItem += DrawName;
            _names.SelectionChangeCommitted += (s, e) => NameChosen();
            _install.Click += async (s, e) => await SafeAsync(InstallAsync);
            _worked.Click += async (s, e) => await SafeAsync(WorkedAsync);
            _didnt.Click += async (s, e) => await SafeAsync(DidntAsync);
            _upgrade.Click += async (s, e) => await SafeAsync(UpgradeAsync);
            _uninstall.Click += async (s, e) => await SafeAsync(UninstallAsync);
            _forget.Click += async (s, e) => await SafeAsync(ForgetAsync);
            _backups.Click += (s, e) => OpenBackups();
            _settings.Click += (s, e) => { if (_state == null) return; using (var f = new SettingsForm(_state)) f.ShowDialog(this); };
            _checkUpdate.Click += async (s, e) => await SafeAsync(() => CheckUpdateAsync(true));
            _link.LinkClicked += (s, e) => OpenUrl(_linkUrl);
            _bink.CheckedChanged += (s, e) => { if (!_updatingUi) UpdateButtons(); };
        }

        // ------------------------------------------------------------ safety

        private async Task SafeAsync(Func<Task> f)
        {
            try { await f(); }
            catch (Exception ex) { ShowError(ex); }
        }

        private void ShowError(Exception ex)
        {
            Program.LogError(ex);
            _busyCount = 0;
            _message.Text = "Something went wrong: " + ex.Message;
            try { RefreshApplied(); } catch { }   // snap the picker back to the game that is really open
            try { UpdateButtons(); } catch { }
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(url); } catch { }
        }

        // ------------------------------------------------------------ startup

        private async Task StartupAsync()
        {
            _state = await Task.Run(() => new StateStore(_paths));
            _backup = new BackupManager(_paths, _state.Settings);
            _gh = new GitHubClient();
            _templates = new TemplateStore(_paths, _gh);
            _installer = new Installer(_state, _backup, _templates);
            _troubleshooter = new Troubleshooter();
            _table = await Task.Run(() => LoadCachedTable());
            _analyzer = new GameAnalyzer(_table, _state, _installer, _templates);

            _statusAdmin.Text = SystemInfo.IsElevated() ? "Running as administrator" : "";
            _statusAdmin.Visible = _statusAdmin.Text.Length > 0;
            ShowTemplateStatus();
            RefreshApplied();
            UpdateButtons();

            // System info, off the UI thread.
            var info = await Task.Run(() => new
            {
                Names = SystemInfo.GpuNames(),
                Sac = SystemInfo.SmartAppControlOn(),
                Driver = SystemInfo.NvidiaDriverVersion()
            });
            _gpuClass = SystemInfo.Classify(info.Names);
            _gpuKnown = true;
            _hasRtx30 = SystemInfo.HasRtx30(info.Names);
            _hasRtx20or30 = SystemInfo.HasRtx20or30(info.Names);
            _gpuName = info.Names.Count > 0 ? string.Join(", ", info.Names) : "unknown";
            _sac = info.Sac;
            _driver = info.Driver ?? "";
            _gpuText = _gpuName + " (" + (_gpuClass == GpuClass.NvidiaRtx50 ? "RTX 50 series"
                : _gpuClass == GpuClass.NvidiaRtx20to40 ? "RTX 20 to 40 series" : "not supported yet") + ")";
            ShowTemplateStatus();
            UpdateButtons();

            var _ = RefreshOnlineAsync();   // background; never blocks the window
            await CheckUpdateAsync(false);
        }

        private CompatibilityTable LoadCachedTable()
        {
            try
            {
                if (File.Exists(_paths.CompatCache))
                {
                    var t = CompatibilityTable.Parse(File.ReadAllText(_paths.CompatCache), "saved copy");
                    if (t.Games.Count > 0) return t;
                }
            }
            catch { }
            return CompatibilityTable.BuiltIn();
        }

        private async Task RefreshOnlineAsync()
        {
            try
            {
                var md = await _gh.GetCompatibilityMarkdownAsync();
                var t = CompatibilityTable.Parse(md, "online " + DateTime.Now.ToString("yyyy-MM-dd"));
                if (t.Games.Count == 0) return;
                await Task.Run(() => File.WriteAllText(_paths.CompatCache, md));
                _table = t;
                _analyzer = new GameAnalyzer(_table, _state, _installer, _templates);
                _updateStatus = "Compatibility list refreshed from GitHub (applies to the next folder you look at)";
                ShowTemplateStatus();
            }
            catch { /* offline: keep the stored list */ }
            try { await _templates.RefreshKnownHashesAsync(); } catch { }
        }

        private async Task CheckUpdateAsync(bool userClicked)
        {
            ReleaseInfo latest = null;
            try { latest = await _gh.GetLatestAsync(); }
            catch
            {
                var cur = _templates.Current;
                _updateStatus = "Couldn't reach GitHub; using stored " + (cur != null ? cur.Tag : "(nothing yet)");
                ShowTemplateStatus();
                if (userClicked) MessageBox.Show(this, _updateStatus + ".", "Check for update");
                return;
            }
            _updateStatus = "";
            if (!_templates.IsNewer(latest))
            {
                ShowTemplateStatus();
                if (userClicked) MessageBox.Show(this, "You already have the newest version (" + latest.Tag + ").", "Check for update");
                return;
            }
            var current = _templates.Current;
            using (var f = new UpdateForm(latest, current != null ? current.Tag : null))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { ShowTemplateStatus(); return; }
            }
            SetBusy("Downloading " + latest.Tag + "...");
            try
            {
                await _templates.InstallReleaseAsync(latest);
            }
            catch (Exception ex)
            {
                EndBusy();
                _message.Text = "Couldn't install the new version: " + ex.Message;
                UpdateButtons();
                return;
            }
            EndBusy();
            _message.Text = "MFG Unlocker " + latest.Tag + " downloaded and checked.";
            ShowTemplateStatus();
            await OfferUpgradeAllAsync(latest.Tag);
            if (_a != null) await AnalyzeAsync(_a.GameRoot, _a.ChosenExe != null ? _a.ChosenExe.Path : null);
            else UpdateButtons();
        }

        private async Task OfferUpgradeAllAsync(string tag)
        {
            var games = _state.All().Where(g => !string.IsNullOrEmpty(g.InstalledName) && g.Version != tag).ToList();
            if (games.Count == 0) return;
            List<GameRecord> picked;
            using (var f = new UpgradeAllForm(games, tag))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                picked = f.Selected.ToList();
            }
            var lines = new List<string>();
            SetBusy("Upgrading...");
            foreach (var g in picked)
            {
                var root = g.GameRoot;
                var res = await Task.Run(() => _installer.Upgrade(root));
                lines.Add(Path.GetFileName(root.TrimEnd('\\', '/')) + ": " + res.Message);
            }
            EndBusy();
            if (lines.Count > 0) MessageBox.Show(this, string.Join("\r\n", lines), "Upgrade");
        }

        private void ShowTemplateStatus()
        {
            var t = _templates == null ? null : _templates.Current;
            _statusGpu.Text = "Graphics card: " + _gpuText;
            _statusSac.Visible = _sac == true;
            _statusSac.Text = "Smart App Control: on – it may block the mod";
            _statusTemplate.Text = "MFG Unlocker template: " + (t != null ? t.Tag + " (" + t.PublishedUtc.ToLocalTime().ToString("d MMM yyyy") + ")" : "none downloaded yet")
                + (string.IsNullOrEmpty(_updateStatus) ? "" : "   –   " + _updateStatus);
            _statusTemplate.Margin = new Padding(3, 7, 3, 0);
        }

        // ------------------------------------------------------------ analysis

        private async Task BrowseAsync()
        {
            string picked;
            using (var d = new FolderBrowserDialog { Description = "Choose the game's folder", ShowNewFolderButton = false })
            {
                if (_a != null && Directory.Exists(_a.GameRoot)) d.SelectedPath = _a.GameRoot;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                picked = d.SelectedPath;
            }
            await AnalyzeAsync(picked, null);
        }

        private sealed class AppliedItem
        {
            public string Root, Text;
            public override string ToString() { return Text; }
        }

        /// <summary>
        /// Fills the Game picker: every game with an installed record, plus the open game
        /// ("... — not installed yet") when it has none; selects the one now shown.
        /// </summary>
        private void RefreshApplied()
        {
            var was = _updatingUi;
            _updatingUi = true;
            try
            {
                _applied.Items.Clear();
                var entries = AppliedGames.BuildEntries(_state == null ? null : _state.All(), _a != null ? _a.GameRoot : null);
                _hasEntries = entries.Count > 0;
                if (!_hasEntries)
                {
                    _applied.Items.Add("No games yet — click Add a game…");
                    _applied.SelectedIndex = 0;
                    _applied.Enabled = false;
                    return;
                }
                _applied.Enabled = !_busy;
                foreach (var e in entries) _applied.Items.Add(new AppliedItem { Root = e.Root, Text = e.Text });
                for (int i = 0; i < _applied.Items.Count; i++)
                    if (_a != null && SameFolder(((AppliedItem)_applied.Items[i]).Root, _a.GameRoot))
                        _applied.SelectedIndex = i;
            }
            finally { _updatingUi = was; }
        }

        private static bool SameFolder(string x, string y)
        {
            return string.Equals((x ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                (y ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        private async Task AppliedChosenAsync()
        {
            var item = _applied.SelectedItem as AppliedItem;
            if (item == null || _busy) return;
            await AnalyzeAsync(item.Root, null);
        }

        private async Task ExeChangedAsync()
        {
            var item = _exeBox.SelectedItem as ExeItem;
            if (item == null || _a == null) return;
            await AnalyzeAsync(_a.GameRoot, item.Path);
        }

        private async Task AnalyzeAsync(string root, string exe)
        {
            if (string.IsNullOrEmpty(root)) return;
            bool gone = !Directory.Exists(root);
            if (gone && (_state == null || _state.Get(root) == null || string.IsNullOrEmpty(_state.Get(root).InstalledName)))
            {
                // nothing to show for it: keep the old game open and put the picker back on it
                RefreshApplied();
                _message.Text = "That folder was not found: " + root;
                return;
            }
            SetBusy("Looking at the game folder...");
            GameAnalysis a;
            try { a = await Task.Run(() => _analyzer.Analyze(root, exe)); }
            finally { EndBusy(); }
            _a = a;
            _folderGone = gone;
            _link.Visible = false;
            _message.Text = "";
            Render();
        }

        private string GoneText()
        {
            return "That game's folder wasn't found: " + (_a != null ? _a.GameRoot : "")
                + ". If you moved or uninstalled it, you can use Forget this game.";
        }

        private void SetBusy(string text)
        {
            _busyCount++;
            _message.Text = text;
            UpdateButtons();
        }

        // ------------------------------------------------------------ rendering

        private sealed class ExeItem
        {
            public string Path, Text;
            public override string ToString() { return Text; }
        }

        private string Rel(string full)
        {
            try
            {
                if (_a != null && full != null && full.StartsWith(_a.GameRoot, StringComparison.OrdinalIgnoreCase))
                    return full.Substring(_a.GameRoot.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
            }
            catch { }
            return full ?? "";
        }

        /// <summary>Makes the finding column wide enough for the longest text; the "where" column gets the rest.</summary>
        private void FitFindingColumns()
        {
            var w = _findings.ClientSize.Width;
            if (w <= 0) return;
            int need = 0;
            foreach (ListViewItem it in _findings.Items)
                need = Math.Max(need, TextRenderer.MeasureText(it.Text, _findings.Font).Width);
            need += LogicalToDeviceUnits(24);   // cell padding
            int cap = w - w * 25 / 100;
            int c0 = Math.Max(LogicalToDeviceUnits(120), Math.Min(need, cap));
            _findings.Columns[0].Width = c0;
            _findings.Columns[1].Width = Math.Max(0, w - c0);
        }

        private void AddFinding(string icon, string text, string where)
        {
            var it = new ListViewItem(icon + "  " + text);
            it.SubItems.Add(where ?? "");
            it.ToolTipText = text + (string.IsNullOrEmpty(where) ? "" : " — " + where);
            it.ForeColor = icon == "✓" ? Color.FromArgb(0, 110, 40) : icon == "✗" ? Color.Firebrick : Color.FromArgb(160, 90, 0);
            _findings.Items.Add(it);
        }

        private void Render()
        {
            _updatingUi = true;
            try
            {
                var a = _a;
                _path.Text = a.GameRoot;
                _findings.Items.Clear();
                const string OK = "✓", WARN = "⚠", BAD = "✗";

                AddFinding(a.Engine == Engine.Unknown ? WARN : OK,
                    a.Engine == Engine.Unknown ? "Game engine not recognised" : "Engine: " + EngineName(a.Engine), "");

                if (a.ChosenExe == null) AddFinding(BAD, "No game program (.exe) found", "");
                else if (!a.ExeConfident) AddFinding(WARN, "Not sure which program is the game. Please pick the right one below.", Rel(a.ChosenExe.Path));
                else AddFinding(OK, "Game program: " + Path.GetFileName(a.ChosenExe.Path), Rel(a.ChosenExe.Path));

                if (a.TargetDir != null) AddFinding(OK, "Install folder (next to the game program)", Rel(a.TargetDir));

                if (a.AntiCheat.Count == 0) AddFinding(OK, "No anti-cheat found (checked file and folder names)", "");
                else foreach (var f in a.AntiCheat) AddFinding(BAD, "Anti-cheat found: " + f.Name, f.Evidence);

                if (a.FrameGenFiles.Count > 0) AddFinding(OK, "DLSS Frame Generation files found", a.FrameGenFiles[0]);
                else AddFinding(WARN, "DLSS Frame Generation files not found in this game", "");

                switch (a.Status)
                {
                    case InstallStatus.Installed:
                        var rec = _state.Get(a.GameRoot);
                        if (rec != null && AppliedGames.NeedsUpgrade(rec, _templates.Current))
                            AddFinding(WARN, "Installed: " + (rec.Version ?? "unknown version") + " — a newer version (" + _templates.Current.Tag + ") is ready. Click Upgrade.",
                                Rel(Path.Combine(a.TargetDir ?? "", a.InstalledFile ?? "")));
                        else
                        AddFinding(OK, "MFG Unlocker installed as " + a.InstalledFile + (rec != null && rec.Version != null ? " (" + rec.Version + ")" : ""), Rel(Path.Combine(a.TargetDir ?? "", a.InstalledFile ?? "")));
                        break;
                    case InstallStatus.InstalledByHand:
                        AddFinding(WARN, "MFG Unlocker is here (" + a.InstalledFile + ") but this tool didn't put it there. Adopt it to manage it.", Rel(Path.Combine(a.TargetDir ?? "", a.InstalledFile ?? "")));
                        break;
                    case InstallStatus.RecordButFileMissing:
                        AddFinding(WARN, "MFG Unlocker no longer found (a game update or repair may have removed it). Reinstall?", a.InstalledFile);
                        break;
                    case InstallStatus.RecordButFileChanged:
                        AddFinding(WARN, "The installed file has changed since this tool put it there, so it is left alone.", a.InstalledFile);
                        break;
                    default:
                        AddFinding(OK, "MFG Unlocker is not installed", "");
                        break;
                }

                foreach (var l in a.Leftovers) AddFinding(WARN, "Leftover file from an older version of the mod", Rel(l));
                if (a.Advice != null) foreach (var w in a.Advice.ModWarnings) AddFinding(WARN, w, "");

                if (a.Compat == null) AddFinding(OK, "Not in upstream list", "");
                else if (a.CompatExact)
                    AddFinding(OK, "Upstream list (from player reports): " + a.Compat.Game
                        + (a.Compat.Use != null && a.Compat.Use.Count > 0 ? " – file name: " + string.Join(", ", a.Compat.Use) : "") + ListDate(), "");
                else
                    AddFinding(WARN, "Possible match in upstream list (from player reports): " + a.Compat.Game + " — check this is your game" + ListDate(), "");

                if (_gpuClass == GpuClass.NvidiaRtx50 && _gpuKnown)
                    AddFinding(OK, "RTX 50 has Multi Frame Generation built in; the mod mainly adds its menu.", "");

                // exe picker
                _exeBox.Items.Clear();
                bool showExe = a.Exes.Count > 1 || !a.ExeConfident;
                foreach (var c in a.Exes)
                    _exeBox.Items.Add(new ExeItem { Path = c.Path, Text = Rel(c.Path) + "   (" + c.Reason + ")" });
                for (int i = 0; i < _exeBox.Items.Count; i++)
                    if (a.ChosenExe != null && string.Equals(((ExeItem)_exeBox.Items[i]).Path, a.ChosenExe.Path, StringComparison.OrdinalIgnoreCase))
                        _exeBox.SelectedIndex = i;
                _exeBox.Visible = _exeLabel.Visible = showExe && a.Exes.Count > 0;
                _exeLabel.Text = "Change game program:";
                _adopt.Visible = a.Status == InstallStatus.InstalledByHand;

                // names
                _names.Items.Clear();
                _lastNameIndex = -1;
                if (a.Advice != null)
                {
                    foreach (var o in a.Advice.Options.Where(o => !ProxyNames.BinkNames.Contains(o.Name, StringComparer.OrdinalIgnoreCase)))
                        _names.Items.Add(o);
                    for (int i = 0; i < _names.Items.Count; i++)
                        if (((NameOption)_names.Items[i]).Name == a.Advice.Recommended) { _names.SelectedIndex = i; _lastNameIndex = i; }
                    _bink.Visible = a.Advice.BinkAvailable;
                }
                else _bink.Visible = false;
                _bink.Checked = false;
                FitFindingColumns();
            }
            finally { _updatingUi = false; }
            RefreshApplied();
            UpdateButtons();
        }

        /// <summary>" (list updated 2026-11-08)" when the compatibility list came from GitHub, otherwise nothing.</summary>
        private string ListDate()
        {
            var src = _table == null ? null : _table.Source;
            if (src != null && src.StartsWith("online ", StringComparison.Ordinal)) return " (list updated " + src.Substring(7) + ")";
            return "";
        }

        private static string EngineName(Engine e)
        {
            switch (e)
            {
                case Engine.Unreal: return "Unreal Engine";
                case Engine.Unity: return "Unity";
                case Engine.REEngine: return "RE Engine";
                case Engine.REDengine: return "REDengine";
                default: return "unknown";
            }
        }

        private void DrawName(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var o = (NameOption)_names.Items[e.Index];
            e.DrawBackground();
            var color = o.Allowed ? e.ForeColor : SystemColors.GrayText;
            if ((e.State & DrawItemState.Selected) != 0 && o.Allowed) color = e.ForeColor;
            var text = o.Name + (string.IsNullOrEmpty(o.Note) ? "" : "   – " + o.Note);
            TextRenderer.DrawText(e.Graphics, text, e.Font, e.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        }

        private void NameChosen()
        {
            var o = _names.SelectedItem as NameOption;
            if (o == null) return;
            if (!o.Allowed)
            {
                bool playerReport = !string.IsNullOrEmpty(o.Note) && o.Note.StartsWith("Player reports", StringComparison.OrdinalIgnoreCase);
                bool ok = playerReport && MessageBox.Show(this,
                    o.Name + " is marked \"don't use\" in the player reports (" + o.Note + "). Use it anyway?",
                    "Use this file name?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                if (!ok) { _names.SelectedIndex = _lastNameIndex; return; }
            }
            _lastNameIndex = _names.SelectedIndex;
        }

        // ------------------------------------------------------------ buttons

        private void UpdateButtons()
        {
            var a = _a;
            var rec = a != null && _state != null ? _state.Get(a.GameRoot) : null;
            bool have = a != null && !_busy && _installer != null;
            bool recorded = have && rec != null && !string.IsNullOrEmpty(rec.InstalledName);
            bool supported = !(_gpuKnown && _gpuClass == GpuClass.Unsupported);

            bool canPlace = have && a.CanInstall && supported
                && (a.Status == InstallStatus.NotInstalled || a.Status == InstallStatus.RecordButFileMissing)
                && _names.SelectedItem != null && _gpuKnown;
            _install.Enabled = canPlace;
            _install.Text = a != null && a.Status == InstallStatus.RecordButFileMissing ? "Reinstall" : "Install";
            bool live = !_folderGone;
            _worked.Enabled = live && recorded && a.Status == InstallStatus.Installed;
            _didnt.Enabled = live && recorded;
            _upgrade.Enabled = live && recorded && a.Status == InstallStatus.Installed;   // also the path for "a newer version is ready"
            _uninstall.Enabled = live && recorded;
            _forget.Enabled = recorded && (_folderGone || a.Status == InstallStatus.RecordButFileChanged || a.Status == InstallStatus.RecordButFileMissing);
            _backups.Enabled = _backup != null;
            _settings.Enabled = _state != null;
            _browse.Enabled = !_busy && _analyzer != null;
            _checkUpdate.Enabled = !_busy && _templates != null;
            _adopt.Enabled = have;
            _applied.Enabled = !_busy && _state != null && _hasEntries;

            if (!_busy && a != null)
            {
                if (_folderGone) { _message.Text = GoneText(); _link.Visible = false; }
                else if (!supported)
                {
                    _message.Text = "Your graphics card needs the OptiScaler/XeMFG route, which this version doesn't support yet.";
                    ShowLink("Read about the OptiScaler route (opens in your browser)", OptiScalerUrl);
                }
                else if (!a.CanInstall && a.BlockReason != null && a.Status == InstallStatus.NotInstalled)
                {
                    _message.Text = a.BlockReason;
                }
            }
        }

        private void ShowLink(string text, string url)
        {
            _link.Text = text; _linkUrl = url; _link.Visible = true;
        }

        private async Task<InstallResult> BgAsync(string busyText, Func<InstallResult> f)
        {
            SetBusy(busyText);
            try { return await Task.Run(f); }
            finally { EndBusy(); }
        }

        private async Task InstallAsync()
        {
            var a = _a;
            var o = _names.SelectedItem as NameOption;
            if (a == null || a.ChosenExe == null || o == null) return;
            if (!a.ExeConfident &&
                MessageBox.Show(this, "I'm not sure " + Path.GetFileName(a.ChosenExe.Path) + " is the game's program. Install next to it anyway?",
                    "Install", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string exe = a.ChosenExe.Path, root = a.GameRoot, dir = a.TargetDir, name = o.Name;
            string bink = _bink.Visible && _bink.Checked
                ? a.Advice.Options.Where(x => x.Allowed && ProxyNames.BinkNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).Select(x => x.Name).FirstOrDefault()
                : null;
            var res = await BgAsync("Installing...", () => bink != null
                ? _installer.InstallBink(root, exe, dir, bink)
                : _installer.Install(root, exe, dir, name));
            await AnalyzeAsync(root, exe);
            _message.Text = res.Ok
                ? res.Message + "\r\nStart the game and press Backspace. The menu should open by itself the first time. Then come back and tell me if it worked."
                    + (_hasRtx20or30 ? "\r\n" + SystemInfo.Rtx30UnlockHint : "")
                : res.Message;
        }

        private async Task AdoptAsync()
        {
            var a = _a;
            if (a == null || a.ChosenExe == null) return;
            string exe = a.ChosenExe.Path, root = a.GameRoot, dir = a.TargetDir, file = a.InstalledFile;
            var res = await BgAsync("Adopting...", () => _installer.Adopt(root, exe, dir, file));
            await AnalyzeAsync(root, exe);
            _message.Text = res.Message;
        }

        private async Task WorkedAsync()
        {
            var a = _a;
            var rec = _state.Get(a.GameRoot);
            if (rec == null) return;
            rec.WorkingName = rec.InstalledName;
            await Task.Run(() => _state.Save(rec));
            await AnalyzeAsync(a.GameRoot, a.ChosenExe != null ? a.ChosenExe.Path : null);
            _message.Text = "Great, " + rec.WorkingName + " is remembered as the name that works for this game.";
            ShowLink("Want to share that it works? Open compatibility report", Troubleshooter.CompatReportUrl);
        }

        private async Task DidntAsync()
        {
            var a = _a;
            if (a == null) return;
            bool uninstall, changed, renamed;
            using (var f = new TroubleshootForm(() => _state.Get(a.GameRoot), a, _installer, _troubleshooter, _paths, _backup,
                _gpuName, _driver, _templates.Current != null ? _templates.Current.Tag : "", _hasRtx30, _hasRtx20or30))
            {
                f.ShowDialog(this);
                uninstall = f.UninstallRequested; changed = f.Changed; renamed = f.Renamed;
            }
            await AnalyzeAsync(a.GameRoot, a.ChosenExe != null ? a.ChosenExe.Path : null);
            if (uninstall) await UninstallAsync();
            else if (changed) _message.Text = "Changed. Start the game and press Backspace again, then tell me if it worked."
                + (renamed && _hasRtx20or30 ? "\r\n" + SystemInfo.Rtx30UnlockHint : "");
        }

        private async Task UpgradeAsync()
        {
            var root = _a.GameRoot;
            var exe = _a.ChosenExe != null ? _a.ChosenExe.Path : null;
            var res = await BgAsync("Upgrading...", () => _installer.Upgrade(root));
            await AnalyzeAsync(root, exe);
            _message.Text = res.Message;
        }

        private async Task UninstallAsync()
        {
            var root = _a.GameRoot;
            var exe = _a.ChosenExe != null ? _a.ChosenExe.Path : null;
            bool settings, logs;
            using (var d = new Form())
            {
                d.Text = "Uninstall MFG Unlocker"; d.StartPosition = FormStartPosition.CenterParent;
                d.FormBorderStyle = FormBorderStyle.FixedDialog; d.MaximizeBox = false; d.MinimizeBox = false;
                Ui.Dpi(d); d.Font = Font;
                d.AutoSize = true; d.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                var l = new Label { Text = "This removes the MFG Unlocker file this tool installed. Nothing else in the game folder is touched.", AutoSize = true, MaximumSize = new Size(428, 0), Margin = new Padding(0, 0, 0, 8) };
                var s = new CheckBox { Text = "Also move the mod's settings file to a backup", AutoSize = true };
                var g = new CheckBox { Text = "Also move the mod's log files to a backup", AutoSize = true };
                var ok = Ui.Btn("Uninstall", DialogResult.OK);
                var no = Ui.Btn("Cancel", DialogResult.Cancel);
                d.AcceptButton = no; d.CancelButton = no;
                var table = Ui.Column();
                table.AutoSize = true; table.AutoSizeMode = AutoSizeMode.GrowAndShrink; table.Dock = DockStyle.None;
                Ui.AddAuto(table, l); Ui.AddAuto(table, s); Ui.AddAuto(table, g); Ui.AddAuto(table, Ui.ButtonRow(ok, no));
                d.Controls.Add(table);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                settings = s.Checked; logs = g.Checked;
            }
            var res = await BgAsync("Uninstalling...", () => _installer.Uninstall(root, settings, logs));
            await AnalyzeAsync(root, exe);
            _message.Text = res.Message;
        }

        private async Task ForgetAsync()
        {
            var root = _a.GameRoot;
            var exe = _a.ChosenExe != null ? _a.ChosenExe.Path : null;
            if (MessageBox.Show(this, "MFGUnlocker GUI will stop tracking this game. No files are changed. Continue?",
                    "Forget this game", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var res = await BgAsync("Forgetting...", () => _installer.Forget(root));
            await AnalyzeAsync(root, exe);
            _message.Text = res.Message;
        }

        private void OpenBackups()
        {
            try
            {
                var dir = _a != null ? _backup.GameDir(_a.GameRoot) : _paths.Backups;
                if (!Directory.Exists(dir)) dir = _paths.Backups;
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { ShowError(ex); }
        }
    }
}
