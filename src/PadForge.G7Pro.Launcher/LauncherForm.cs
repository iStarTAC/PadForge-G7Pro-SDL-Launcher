using System.Drawing;
using System.Windows.Forms;

namespace PadForge.G7Pro.Launcher;

internal sealed class LauncherForm : Form
{
    private const string WindowRole = "window";
    private const string CardRole = "card";
    private const string TitleRole = "title";
    private const string MutedRole = "muted";
    private const string PrimaryButtonRole = "primary-button";
    private const string SecondaryButtonRole = "secondary-button";
    private const string LinkRole = "link";

    private Paths paths;
    private readonly Label serviceStatus = StatusLabel();
    private readonly Label controllerStatus = new() { AutoSize = true, Text = "Controller: checking…", Tag = MutedRole, Margin = new Padding(3, 2, 0, 2) };
    private readonly Label autostartStatus = StatusLabel();
    private readonly Label padForgePath = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, MinimumSize = new Size(0, 22), Tag = MutedRole, Margin = new Padding(3, 4, 0, 2) };
    private readonly Label padForgeFound = StatusLabel();
    private readonly Label message = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = MutedRole };
    private readonly List<Button> actionButtons = [];
    private readonly Button themeButton;
    private Button? startButton;
    private readonly System.Windows.Forms.Timer controllerTimer = new() { Interval = 3000 };
    private ThemePreference themePreference;
    private bool darkMode;
    private bool busy;

    public LauncherForm(Paths paths)
    {
        this.paths = paths;
        themePreference = ThemeSettings.Load();
        Text = "PadForge G7 Pro Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 540);
        ClientSize = new Size(800, 580);
        Font = new Font("Segoe UI", 10F);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Tag = WindowRole;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), RowCount = 5, ColumnCount = 1, Tag = WindowRole };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Tag = WindowRole };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { AutoSize = true, Text = "PadForge G7 Pro", Font = new Font("Segoe UI Semibold", 22F), Tag = TitleRole }, 0, 0);
        themeButton = MakeButton("Theme: System", (_, _) => CycleTheme(), true);
        themeButton.Margin = new Padding(8, 2, 0, 0);
        header.Controls.Add(themeButton, 1, 0);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(new Label { AutoSize = true, Text = "Launch PadForge and manage its Xbox GIP service.", Tag = MutedRole, Margin = new Padding(2, 0, 0, 0) }, 0, 1);
        root.Controls.Add(BuildLaunchCard(), 0, 2);
        root.Controls.Add(BuildServiceCard(), 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Tag = WindowRole };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(message, 0, 0);
        var footerActions = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Tag = WindowRole,
            Margin = Padding.Empty,
        };
        footerActions.Controls.Add(new Label
        {
            AutoSize = true,
            Text = Program.DisplayVersion,
            Tag = MutedRole,
            Margin = new Padding(0, 11, 10, 0),
        });
        footerActions.Controls.Add(MakeLink("GitHub", (_, _) => RunUtilityAction(Program.OpenProjectPage)));
        footerActions.Controls.Add(MakeLink("Open logs", (_, _) => RunUtilityAction(Program.OpenLogs)));
        footerActions.Controls.Add(MakeButton("Refresh", (_, _) => RefreshStatus(), true));
        footer.Controls.Add(footerActions, 1, 0);
        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);

        HandleCreated += (_, _) => ApplyTheme();
        Shown += (_, _) =>
        {
            RefreshStatus();
            UpdatePadForgePath();
            controllerTimer.Start();
        };
        controllerTimer.Tick += (_, _) => RefreshLiveStatus();
        FormClosed += (_, _) => controllerTimer.Dispose();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        const int WmSettingChange = 0x001A;
        if (m.Msg == WmSettingChange && themePreference == ThemePreference.System) ApplyTheme();
    }

    private Control BuildLaunchCard()
    {
        FlowLayoutPanel actions = ActionsPanel();
        startButton = MakeButton("Start PadForge", (_, _) => StartPadForge());
        actions.Controls.Add(startButton);
        actions.Controls.Add(MakeButton("Install auto-start", (_, _) =>
        {
            if (EnsurePadForgeSelected()) RunAction("Auto-start installed.", () => Program.InstallAutostart(paths));
        }, true));
        actions.Controls.Add(MakeButton("Uninstall auto-start", (_, _) => RunAction("Auto-start removed.", Program.UninstallAutostart), true));
        UpdatePadForgePath();
        return ApplicationCard(actions);
    }

    private void StartPadForge()
    {
        if (!EnsurePadForgeSelected()) return;
        if (Program.IsPadForgeRunning(paths))
        {
            message.Text = "PadForge is already running.";
            UpdateStartButton();
            return;
        }

        if (startButton is not null) startButton.Text = "Starting PadForge…";
        message.Text = "Preparing Xbox GIP mode…";
        Application.DoEvents();
        RunAction("PadForge started.", () =>
        {
            if (Program.Launch(paths, Options.ForLaunch(paths.PadForge), showErrors: false) != 0)
                throw new InvalidOperationException(Program.LastLaunchError ??
                    "PadForge could not be started. See the launcher error log for details.");
        });
    }

    private Control BuildServiceCard()
    {
        FlowLayoutPanel actions = ActionsPanel();
        actions.Controls.Add(MakeButton("Install", (_, _) => RunAction("Service installed.", () => Program.InstallService(paths))));
        actions.Controls.Add(MakeButton("Uninstall", (_, _) => RunAction("Service uninstalled.", Program.UninstallService), true));
        actions.Controls.Add(MakeButton("Start", (_, _) => RunAction("Service started.", Program.StartService), true));
        actions.Controls.Add(MakeButton("Stop", (_, _) => RunAction("Service stopped.", Program.StopService), true));
        actions.Controls.Add(MakeButton("Restart", (_, _) => RunAction("Service restarted.", Program.RestartService), true));
        var details = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = CardRole, Margin = Padding.Empty };
        details.Controls.Add(new Label { AutoSize = true, Text = "Verifies GIP mode and supplies paddles, gyro, battery, and profile data.", Tag = MutedRole, Margin = new Padding(3, 4, 0, 2) });
        details.Controls.Add(controllerStatus);
        return Card("Xbox GIP service", details, serviceStatus, actions);
    }

    private static Control Card(string title, Control subtitle, Label status, Control actions)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6), Padding = new Padding(20, 10, 20, 10), RowCount = 3, ColumnCount = 2, Tag = CardRole };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.Controls.Add(new Label { AutoSize = true, Text = title, Font = new Font("Segoe UI Semibold", 13F), Tag = TitleRole }, 0, 0);
        panel.Controls.Add(status, 1, 0);
        panel.SetColumnSpan(subtitle, 2);
        panel.Controls.Add(subtitle, 0, 1);
        panel.SetColumnSpan(actions, 2);
        panel.Controls.Add(actions, 0, 2);
        return panel;
    }

    private Control ApplicationCard(Control actions)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6), Padding = new Padding(20, 10, 20, 10), RowCount = 4, ColumnCount = 2, Tag = CardRole };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.Controls.Add(new Label { AutoSize = true, Text = "Application", Font = new Font("Segoe UI Semibold", 13F), Tag = TitleRole }, 0, 0);
        panel.Controls.Add(autostartStatus, 1, 0);
        var description = new Label { AutoSize = true, Text = "Auto-start runs PadForge silently at sign-in.", Tag = MutedRole, Margin = new Padding(3, 4, 0, 2) };
        panel.SetColumnSpan(description, 2);
        panel.Controls.Add(description, 0, 1);

        var location = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Tag = CardRole, Margin = new Padding(0, 2, 0, 1) };
        location.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        location.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        padForgeFound.Margin = new Padding(3, 8, 10, 0);
        location.Controls.Add(padForgeFound, 0, 0);
        location.Controls.Add(padForgePath, 1, 0);
        Button browse = MakeButton("Browse…", (_, _) => ChoosePadForgeFolder(), true);
        browse.Margin = new Padding(8, 0, 0, 0);
        location.Controls.Add(browse, 2, 0);
        panel.SetColumnSpan(location, 2);
        panel.Controls.Add(location, 0, 2);

        panel.SetColumnSpan(actions, 2);
        panel.Controls.Add(actions, 0, 3);
        return panel;
    }

    private static FlowLayoutPanel ActionsPanel() => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Padding = new Padding(0, 6, 0, 0),
        Tag = CardRole,
    };

    private Button MakeButton(string text, EventHandler click, bool secondary = false)
    {
        var button = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 36),
            Text = text,
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Tag = secondary ? SecondaryButtonRole : PrimaryButtonRole,
        };
        button.Click += click;
        actionButtons.Add(button);
        return button;
    }

    private static LinkLabel MakeLink(string text, EventHandler click)
    {
        var link = new LinkLabel
        {
            AutoSize = true,
            Text = text,
            Tag = LinkRole,
            Margin = new Padding(0, 10, 14, 0),
            TabStop = true,
        };
        link.LinkClicked += (_, _) => click(link, EventArgs.Empty);
        return link;
    }

    private static Label StatusLabel() => new()
    {
        AutoSize = true,
        Text = "Checking…",
        Font = new Font("Segoe UI Semibold", 10F),
        Anchor = AnchorStyles.Right,
        Tag = MutedRole,
    };

    private void CycleTheme()
    {
        themePreference = themePreference switch
        {
            ThemePreference.System => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.Dark,
            _ => ThemePreference.System,
        };
        ThemeSettings.Save(themePreference);
        ApplyTheme();
    }

    private bool EnsurePadForgeSelected()
    {
        if (File.Exists(paths.PadForge)) return true;
        ChoosePadForgeFolder();
        return File.Exists(paths.PadForge);
    }

    private void ChoosePadForgeFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder containing the official PadForge.exe",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(Path.GetDirectoryName(paths.PadForge))
                ? Path.GetDirectoryName(paths.PadForge)!
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            message.Text = "PadForge.exe was not found. Choose its folder before starting PadForge or installing auto-start.";
            return;
        }

        string executable = Path.Combine(dialog.SelectedPath, "PadForge.exe");
        if (!File.Exists(executable))
        {
            MessageBox.Show("That folder does not contain PadForge.exe.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            message.Text = "The selected folder does not contain PadForge.exe.";
            return;
        }

        paths = paths with { PadForge = executable };
        ThemeSettings.SavePadForgePath(executable);
        UpdatePadForgePath();
        message.Text = "PadForge location saved.";
    }

    private void UpdatePadForgePath()
    {
        bool found = File.Exists(paths.PadForge);
        padForgeFound.Text = found ? "Found" : "Not found";
        padForgeFound.ForeColor = found
            ? (darkMode ? Color.FromArgb(91, 206, 145) : Color.FromArgb(24, 135, 84))
            : (darkMode ? Color.FromArgb(244, 184, 96) : Color.FromArgb(160, 100, 20));
        padForgePath.Text = found ? paths.PadForge : "No valid PadForge.exe selected";
        padForgePath.ForeColor = darkMode ? Palette.Dark.Muted : Palette.Light.Muted;
    }

    private void ApplyTheme()
    {
        darkMode = ThemeSettings.IsDark(themePreference);
        Palette palette = darkMode ? Palette.Dark : Palette.Light;
        ApplyThemeToControl(this, palette);
        themeButton.Text = $"Theme: {themePreference}";
        ThemeSettings.ApplyDarkTitleBar(this, darkMode);
        UpdateStatusColors();
        UpdatePadForgePath();
        Invalidate(true);
    }

    private static void ApplyThemeToControl(Control control, Palette palette)
    {
        string? role = control.Tag as string;
        switch (role)
        {
            case CardRole:
                control.BackColor = palette.Card;
                control.ForeColor = palette.Text;
                break;
            case TitleRole:
                control.BackColor = control.Parent?.BackColor ?? palette.Window;
                control.ForeColor = palette.Text;
                break;
            case MutedRole:
                control.BackColor = control.Parent?.BackColor ?? palette.Window;
                control.ForeColor = palette.Muted;
                break;
            case PrimaryButtonRole:
                StyleButton((Button)control, palette.Accent, Color.White, palette.Accent);
                break;
            case SecondaryButtonRole:
                StyleButton((Button)control, palette.Card, palette.Text, palette.Border);
                break;
            case LinkRole:
                var link = (LinkLabel)control;
                link.BackColor = control.Parent?.BackColor ?? palette.Window;
                link.ForeColor = palette.Muted;
                link.LinkColor = palette.Accent;
                link.ActiveLinkColor = ControlPaint.Light(palette.Accent, 0.15F);
                link.VisitedLinkColor = palette.Accent;
                break;
            default:
                control.BackColor = palette.Window;
                control.ForeColor = palette.Text;
                break;
        }
        foreach (Control child in control.Controls) ApplyThemeToControl(child, palette);
    }

    private static void StyleButton(Button button, Color background, Color foreground, Color border)
    {
        button.BackColor = background;
        button.ForeColor = foreground;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(background, 0.08F);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(background, 0.08F);
    }

    private void RefreshStatus()
    {
        try
        {
            serviceStatus.Text = Program.GetServiceStatus();
            bool installed = Program.IsAutostartInstalled();
            autostartStatus.Text = installed ? "Auto-start installed" : "Manual start";
            UpdateStatusColors();
        }
        catch (Exception error) { message.Text = error.GetBaseException().Message; }
        RefreshControllerStatus(showError: true);
        UpdateStartButton();
    }

    private void RefreshLiveStatus()
    {
        if (busy || !Visible || WindowState == FormWindowState.Minimized) return;
        RefreshControllerStatus();
        UpdateStartButton();
    }

    private void RefreshControllerStatus(bool showError = false)
    {
        if (busy || !Visible || WindowState == FormWindowState.Minimized) return;
        try
        {
            controllerStatus.Text = "Controller: " + Program.GetControllerStatus(paths);
            UpdateStatusColors();
        }
        catch (Exception error)
        {
            controllerStatus.Text = "Controller: status unavailable";
            UpdateStatusColors();
            if (showError) message.Text = error.GetBaseException().Message;
        }
    }

    private void UpdateStatusColors()
    {
        Color good = darkMode ? Color.FromArgb(91, 206, 145) : Color.FromArgb(24, 135, 84);
        Color warning = darkMode ? Color.FromArgb(244, 184, 96) : Color.FromArgb(160, 100, 20);
        Color muted = darkMode ? Palette.Dark.Muted : Palette.Light.Muted;
        serviceStatus.ForeColor = serviceStatus.Text == "Running" ? good : warning;
        controllerStatus.ForeColor = controllerStatus.Text.Contains("Connected in", StringComparison.Ordinal) ? good : warning;
        autostartStatus.ForeColor = autostartStatus.Text == "Auto-start installed" ? good : muted;
    }

    private void UpdateStartButton()
    {
        if (startButton is null || busy) return;
        bool running = Program.IsPadForgeRunning(paths);
        startButton.Text = running ? "PadForge running" : "Start PadForge";
        startButton.Enabled = !running;
    }

    private void RunAction(string success, Action action)
    {
        try { SetBusy(true); action(); message.Text = success; }
        catch (Exception error)
        {
            message.Text = error.GetBaseException().Message;
            MessageBox.Show(error.GetBaseException().Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); RefreshStatus(); }
    }

    private void RunUtilityAction(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            message.Text = error.GetBaseException().Message;
            MessageBox.Show(error.GetBaseException().Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetBusy(bool busy)
    {
        this.busy = busy;
        UseWaitCursor = busy;
        foreach (Button button in actionButtons) button.Enabled = !busy;
        if (!busy) UpdateStartButton();
        Application.DoEvents();
    }

    private sealed record Palette(Color Window, Color Card, Color Text, Color Muted, Color Border, Color Accent)
    {
        public static readonly Palette Light = new(Color.FromArgb(245, 247, 250), Color.White, Color.FromArgb(25, 31, 42), Color.FromArgb(90, 100, 115), Color.FromArgb(205, 211, 220), Color.FromArgb(58, 91, 192));
        public static readonly Palette Dark = new(Color.FromArgb(22, 25, 31), Color.FromArgb(31, 35, 43), Color.FromArgb(238, 241, 246), Color.FromArgb(164, 174, 190), Color.FromArgb(70, 77, 90), Color.FromArgb(83, 116, 222));
    }
}
