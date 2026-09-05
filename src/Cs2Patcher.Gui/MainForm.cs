using Cs2Patcher.Core;

namespace Cs2Patcher.Gui;

/// <summary>
/// The whole tool in one window: it finds your game, tells you what it found, and gives you
/// two buttons. The log pane shows every setting that changes, with its before and after
/// value, because a tool that edits your config should never do so invisibly.
/// </summary>
public sealed class MainForm : Form
{
    private readonly TextBox _pathBox = new();
    private readonly Label _versionLabel = new();
    private readonly Label _hardwareLabel = new();
    private readonly Label _statusLabel = new();
    private readonly ComboBox _profileBox = new();
    private readonly Label _profileDescription = new();
    private readonly Button _applyButton = new();
    private readonly Button _revertButton = new();
    private readonly Button _tunePcButton = new();
    private readonly Button _recommendButton = new();
    private readonly Button _holdUpdateButton = new();
    private readonly TextBox _log = new();

    private GameLocator.GameInstall _install = null!;
    private HardwareInfo? _hardware;
    private PatchEngine? _engine;

    public MainForm()
    {
        Text = "CS2 Performance Patcher";
        Size = new Size(760, 660);
        MinimumSize = new Size(680, 560);
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(BuildLayout());
        Detect();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildInstallGroup());
        root.Controls.Add(BuildProfileGroup());
        root.Controls.Add(BuildStatusPanel());
        root.Controls.Add(BuildButtonRow());
        root.Controls.Add(BuildLogGroup());

        return root;
    }

    private GroupBox BuildInstallGroup()
    {
        var browse = new Button { Text = "Browse...", Width = 90, Height = 26, Anchor = AnchorStyles.Right };
        browse.Click += (_, _) => BrowseForInstall();

        _pathBox.Width = 560;
        _pathBox.Height = 26;
        _pathBox.ReadOnly = true;
        _pathBox.BackColor = SystemColors.Window;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, AutoSize = true,
            Padding = new Padding(8, 6, 8, 8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(_pathBox, 0, 0);
        layout.Controls.Add(browse, 1, 0);

        _versionLabel.AutoSize = true;
        _versionLabel.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(_versionLabel, 0, 1);
        layout.SetColumnSpan(_versionLabel, 2);

        _hardwareLabel.AutoSize = true;
        _hardwareLabel.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(_hardwareLabel, 0, 2);
        layout.SetColumnSpan(_hardwareLabel, 2);

        return new GroupBox { Text = "Game", Dock = DockStyle.Top, AutoSize = true, Controls = { layout } };
    }

    private GroupBox BuildProfileGroup()
    {
        _profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileBox.Width = 220;
        foreach (var p in Profiles.All) _profileBox.Items.Add(p.Name);
        _profileBox.SelectedIndex = 1; // Traffic Sim
        _profileBox.SelectedIndexChanged += (_, _) => UpdateProfileDescription();

        _profileDescription.AutoSize = false;
        _profileDescription.Height = 52;
        _profileDescription.Dock = DockStyle.Fill;
        _profileDescription.ForeColor = SystemColors.GrayText;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true,
            Padding = new Padding(8, 6, 8, 8),
        };
        layout.Controls.Add(_profileBox);
        layout.Controls.Add(_profileDescription);

        return new GroupBox { Text = "Profile", Dock = DockStyle.Top, AutoSize = true, Controls = { layout } };
    }

    private Panel BuildStatusPanel()
    {
        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font(Font, FontStyle.Bold);
        _statusLabel.Padding = new Padding(2, 8, 2, 6);
        return new Panel { Dock = DockStyle.Top, AutoSize = true, Controls = { _statusLabel } };
    }

    private Panel BuildButtonRow()
    {
        _applyButton.Text = "Apply";
        _applyButton.Width = 130;
        _applyButton.Height = 34;
        _applyButton.Click += (_, _) => DoApply();

        _revertButton.Text = "Revert";
        _revertButton.Width = 130;
        _revertButton.Height = 34;
        _revertButton.Click += (_, _) => DoRevert();

        _tunePcButton.Text = "Check my PC";
        _tunePcButton.Width = 130;
        _tunePcButton.Height = 34;
        _tunePcButton.Click += (_, _) => DoTunePc();

        _recommendButton.Text = "Recommend for me";
        _recommendButton.Width = 150;
        _recommendButton.Height = 34;
        _recommendButton.Click += (_, _) => DoRecommend();

        _holdUpdateButton.Text = "Hold updates";
        _holdUpdateButton.Width = 130;
        _holdUpdateButton.Height = 34;
        _holdUpdateButton.Click += (_, _) => DoHoldUpdate();

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
        flow.Controls.Add(_applyButton);
        flow.Controls.Add(_revertButton);
        flow.Controls.Add(_tunePcButton);
        flow.Controls.Add(_recommendButton);
        flow.Controls.Add(_holdUpdateButton);

        return new Panel { Dock = DockStyle.Top, AutoSize = true, Controls = { flow } };
    }

    /// <summary>
    /// Reports what outside the game is costing frames, and offers to change only the parts
    /// that can be put back. Anything needing a BIOS, a reboot or administrator rights is
    /// explained and left alone.
    /// </summary>
    private void DoTunePc()
    {
        var exe = _install.InstallDir is null ? null : Path.Combine(_install.InstallDir, "Cities2.exe");
        var findings = HostTuning.Inspect(File.Exists(exe) ? exe : null);

        var report = new List<string>();
        foreach (var f in findings)
        {
            var mark = f.Severity switch
            {
                Severity.Ok => "ok",
                Severity.Actionable => "FIX",
                _ => "note",
            };
            report.Add($"[{mark}] {f.Name}: {f.Current}");
            if (f.Severity != Severity.Ok) report.Add($"       wanted: {f.Wanted}");
            report.Add($"       {f.Why}");
            report.Add("");
        }

        WriteLog("Outside the game", report, []);

        var fixable = findings.Where(f => f.Severity == Severity.Actionable).ToList();
        if (fixable.Count == 0)
        {
            SetStatus("Nothing outside the game this tool can change.", Color.ForestGreen);
            return;
        }

        var answer = MessageBox.Show(
            $"{fixable.Count} of these can be changed from here:\r\n\r\n"
            + string.Join("\r\n", fixable.Select(f => $"  - {f.Name}: {f.Current} -> {f.Wanted}"))
            + "\r\n\r\nEach one is recorded so it can be put back. Change them?",
            "Check my PC", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes) return;

        var state = HostTuning.StateFile(_install.UserDataDir!);
        var applied = HostTuning.Apply(findings, state);

        WriteLog("Outside the game", [.. applied.Select(l => $"changed: {l}"), "", "Put these back with:  cs2patch tune-pc --revert"], []);
        SetStatus($"Changed {applied.Count} outside the game.", Color.ForestGreen);
    }

    private GroupBox BuildLogGroup()
    {
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.Font = new Font("Consolas", 8.5F);
        _log.BackColor = SystemColors.Window;

        return new GroupBox { Text = "What changed", Dock = DockStyle.Fill, Controls = { _log } };
    }

    private void BrowseForInstall()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Cities Skylines II folder (the one containing Cities2.exe)",
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) Detect(dialog.SelectedPath);
    }

    private void Detect(string? explicitPath = null)
    {
        _install = GameLocator.Locate(explicitPath);

        if (!_install.Found)
        {
            _pathBox.Text = "";
            _versionLabel.Text = _install.Diagnostic ?? "Cities: Skylines II not found.";
            _hardwareLabel.Text = "";
            SetStatus("Game not found — use Browse to point at it.", Color.Firebrick);
            _applyButton.Enabled = _revertButton.Enabled = false;
            return;
        }

        _hardware = HardwareProbe.FromPlayerLog(_install.UserDataDir);
        _engine = PatchEngine.For(_install);

        _pathBox.Text = _install.InstallDir;
        _versionLabel.Text = $"Version {_install.GameVersion ?? "unknown"}" +
                             (_install.BuildId is not null ? $"   ·   Steam build {_install.BuildId}" : "");

        _hardwareLabel.Text = _hardware is null
            ? "Run the game once so it writes a Player.log, and this will show your hardware."
            : $"{_hardware.Gpu ?? "?"}" +
              (_hardware.HasVram ? $" · {_hardware.VramMegabytes} MB VRAM" : "") +
              $"   ·   {_hardware.Cpu ?? "?"} ({_hardware.CoreCount} threads)" +
              $"   ·   {_hardware.SystemMemoryGb:N1} GB RAM";

        _applyButton.Enabled = _revertButton.Enabled = true;
        UpdateProfileDescription();
        UpdateHoldButtonText();
        RefreshStatus();
    }

    private void UpdateProfileDescription()
    {
        var profile = Profiles.All[_profileBox.SelectedIndex];
        var free = profile.Tweaks.Count(t => t.Cost == Cost.Free);
        var cheap = profile.Tweaks.Count(t => t.Cost == Cost.Cheap);
        var visible = profile.Tweaks.Count(t => t.Cost == Cost.Visible);

        // The measured line goes first. Somebody choosing between eight tiers wants to know what
        // each one bought before they read what it costs.
        var measured = profile.Measured is { } m
            ? $"About {m.Fps:N0} fps at normal play speed — +{m.GainPercent}% over untouched, "
              + $"with {m.ShareAtSixty}% of frames at 60 or better.\r\n"
            : string.Empty;

        _profileDescription.Text = measured + $"{profile.Description}\r\n" +
                                   $"{profile.Tweaks.Count} changes — {free} invisible, {cheap} barely visible, {visible} visible.";
    }

    private void DoRecommend()
    {
        var pick = ProfileAdvisor.Recommend(_hardware);

        // Select it rather than apply it. A recommendation that patches your game without being
        // asked is not a recommendation.
        var index = Profiles.All.ToList().FindIndex(p => p.Id == pick.Profile.Id);
        if (index >= 0) _profileBox.SelectedIndex = index;

        WriteLog($"Recommended: {pick.Profile.Name}",
        [
            pick.Because,
            $"Expect about {pick.ExpectedFps:N0} fps at normal play speed.",
            "Press Apply if it looks right.",
        ],
        [
            "This is an estimate. Every figure in this tool was measured on one machine, and "
            + "yours is placed against it by GPU memory and core count, nothing more.",
        ]);
    }

    private void DoHoldUpdate()
    {
        var holding = UpdateHold.Read(_install.InstallDir) == UpdatePolicy.OnLaunch;
        var result = UpdateHold.Set(_install.InstallDir, hold: !holding);

        var notes = new List<string>();
        if (result.Success && !holding)
        {
            notes.Add("Steam still updates when you press Play; nothing can refuse an update "
                      + "outright. What this stops is it happening while the machine is idle.");
        }

        WriteLog(result.Success ? $"OK — {result.Message}" : $"Failed — {result.Message}",
            notes, []);

        UpdateHoldButtonText();
    }

    private void UpdateHoldButtonText()
    {
        _holdUpdateButton.Text = UpdateHold.Read(_install.InstallDir) == UpdatePolicy.OnLaunch
            ? "Release updates"
            : "Hold updates";
    }

    private void RefreshStatus()
    {
        var manifest = _engine?.ReadManifest();
        if (manifest is null)
        {
            SetStatus("Not patched.", SystemColors.ControlText);
            _revertButton.Enabled = false;
            return;
        }

        _revertButton.Enabled = true;

        if (!string.Equals(manifest.GameVersion, _install.GameVersion, StringComparison.Ordinal))
            SetStatus($"Patched with '{manifest.ProfileName}', but the game updated since. Apply again.", Color.DarkGoldenrod);
        else if (_engine!.HasDriftedSincePatch())
            SetStatus($"Patched with '{manifest.ProfileName}', but settings changed since. Apply again.", Color.DarkGoldenrod);
        else
            SetStatus($"Patched with '{manifest.ProfileName}' on {manifest.PatchedAtUtc.ToLocalTime():d MMM, HH:mm}.", Color.ForestGreen);
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    private void DoApply()
    {
        if (GameIsRunning()) return;

        var profile = Profiles.All[_profileBox.SelectedIndex];
        var extras = new List<Tweak>();
        if (_hardware?.HasVram == true) extras.Add(Profiles.MeshBudgetFor(_hardware.VramMegabytes));

        // The lowest tiers cut the resolution, and what that means depends on this display
        // rather than on the profile.
        if (profile.ScreenScale is { } scale)
        {
            var resolution = Profiles.ResolutionFor(scale, DisplayProbe.Current());
            if (resolution is not null) extras.Add(resolution);
        }

        try
        {
            var result = _engine!.Apply(profile, extras);
            WriteLog(result.Message, result.Applied, result.Skipped);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"{ex.Message}\r\n\r\nYour original settings are backed up. Use Revert to restore them.",
                "Could not apply", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DoRevert()
    {
        if (GameIsRunning()) return;

        var result = _engine!.Revert();
        WriteLog(result.Message, [], []);
        RefreshStatus();
    }

    private bool GameIsRunning()
    {
        if (System.Diagnostics.Process.GetProcessesByName("Cities2").Length == 0) return false;

        MessageBox.Show(this,
            "Close Cities: Skylines II first.\r\n\r\n" +
            "The game rewrites its settings file when it exits, which would undo the patch.",
            "Game is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return true;
    }

    private void WriteLog(string headline, IReadOnlyList<string> applied, IReadOnlyList<string> skipped)
    {
        var lines = new List<string> { headline, "" };
        lines.AddRange(applied.Select(a => "  + " + a));
        lines.AddRange(skipped.Select(s => "  ! " + s));
        _log.Text = string.Join("\r\n", lines);
    }
}
