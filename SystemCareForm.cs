using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace SystemCare;

internal sealed class SystemCareForm : Form
{
    private readonly SystemCareConfig _config;
    private readonly Label _taskStatus = new();
    private readonly Label _nextRun = new();
    private readonly Label _mode = new();
    private readonly RichTextBox _details = new();
    private readonly Button _dryRunButton = new();
    private readonly Button _installButton = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 5000 };
    private bool _busy;

    public SystemCareForm(SystemCareConfig config)
    {
        _config = config;
        Text = "SystemCare";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 540);
        Size = new Size(900, 610);
        BackColor = Color.FromArgb(11, 16, 32);
        ForeColor = Color.FromArgb(229, 231, 235);
        Font = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.Sizable;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(28, 24, 28, 24),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildStatusCards(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
        root.Controls.Add(BuildDetails(), 0, 3);

        _refreshTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _refreshTimer.Start();
        Shown += async (_, _) => await RefreshStatusAsync();
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            AutoSize = true,
            Text = "SystemCare",
            Font = new Font("Segoe UI Semibold", 25F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 0)
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Text = "Leise tägliche Systempflege für Windows und Gaming",
            Font = new Font("Segoe UI", 10.5F),
            ForeColor = Color.FromArgb(156, 163, 175),
            Location = new Point(3, 45)
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        return panel;
    }

    private Control BuildStatusCards()
    {
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        cards.Controls.Add(BuildCard("AUTOMATIK", _taskStatus, Color.FromArgb(52, 211, 153)), 0, 0);
        cards.Controls.Add(BuildCard("NÄCHSTER LAUF", _nextRun, Color.FromArgb(96, 165, 250)), 1, 0);
        cards.Controls.Add(BuildCard("SCHUTZPROFIL", _mode, Color.FromArgb(251, 191, 36)), 2, 0);
        return cards;
    }

    private static Control BuildCard(string caption, Label value, Color accent)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(16, 12, 12, 10),
            BackColor = Color.FromArgb(20, 28, 48)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(accent, 2F);
            e.Graphics.DrawLine(pen, 0, 0, 0, card.Height);
        };
        var label = new Label
        {
            AutoSize = true,
            Text = caption,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(16, 12)
        };
        value.AutoSize = true;
        value.Text = "Wird geprüft …";
        value.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
        value.ForeColor = Color.White;
        value.Location = new Point(16, 43);
        card.Controls.Add(label);
        card.Controls.Add(value);
        return card;
    }

    private Control BuildActions()
    {
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = Color.Transparent
        };
        ConfigureButton(_dryRunButton, "Jetzt prüfen (Dry-Run)", Color.FromArgb(37, 99, 235));
        _dryRunButton.Click += async (_, _) => await RunDryRunAsync();
        ConfigureButton(_installButton, "Tagesaufgabe aktualisieren", Color.FromArgb(5, 150, 105));
        _installButton.Click += async (_, _) => await InstallTaskAsync();
        var configButton = new Button();
        ConfigureButton(configButton, "Konfiguration öffnen", Color.FromArgb(55, 65, 81));
        configButton.Click += (_, _) => OpenPath(SystemCareConfig.ConfigPath);
        var logsButton = new Button();
        ConfigureButton(logsButton, "Logs öffnen", Color.FromArgb(55, 65, 81));
        logsButton.Click += (_, _) => OpenPath(SystemCareConfig.DataDirectory);
        actions.Controls.Add(_dryRunButton);
        actions.Controls.Add(_installButton);
        actions.Controls.Add(configButton);
        actions.Controls.Add(logsButton);
        return actions;
    }

    private Control BuildDetails()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42), Padding = new Padding(16) };
        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Aktueller Status",
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Color.White
        };
        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.BorderStyle = BorderStyle.None;
        _details.BackColor = panel.BackColor;
        _details.ForeColor = Color.FromArgb(203, 213, 225);
        _details.Font = new Font("Consolas", 9.5F);
        _details.DetectUrls = true;
        panel.Controls.Add(_details);
        panel.Controls.Add(header);
        return panel;
    }

    private static void ConfigureButton(Button button, string text, Color color)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 36;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        button.Margin = new Padding(0, 0, 10, 0);
        button.Padding = new Padding(12, 0, 12, 0);
        button.Cursor = Cursors.Hand;
    }

    private async Task RefreshStatusAsync()
    {
        if (_busy || IsDisposed) return;
        var result = await TaskSchedulerManager.QueryAsync();
        if (IsDisposed) return;
        bool installed = result.ExitCode == 0;
        _taskStatus.Text = installed ? "Aktiv" : "Nicht installiert";
        _taskStatus.ForeColor = installed ? Color.FromArgb(110, 231, 183) : Color.FromArgb(252, 165, 165);
        _mode.Text = "Schonend & geschützt";
        _mode.ForeColor = Color.FromArgb(253, 224, 71);
        _nextRun.Text = installed ? ExtractLine(result.Output, "Nächste Laufzeit") : $"Täglich {_config.DailyTime}";
        _details.Text = installed ? result.Output : "Noch keine tägliche Aufgabe gefunden.\n\nMit ‘Tagesaufgabe aktualisieren’ wird sie mit dem aktuellen Programm registriert.";
    }

    private async Task RunDryRunAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var dryRunConfig = SystemCareConfig.Load();
            dryRunConfig.DryRun = true;
            _details.Text = "Dry-Run läuft …\n\nEs werden keine Änderungen vorgenommen.";
            int exitCode = await SystemCareRunner.RunAsync(dryRunConfig);
            _details.Text = $"Dry-Run abgeschlossen (Exit {exitCode}).\n\nDer vollständige Prüfbericht liegt unter:\n{SystemCareConfig.DataDirectory}";
        }
        catch (Exception ex)
        {
            _details.Text = $"Dry-Run fehlgeschlagen:\n{ex.Message}";
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }

    private async Task InstallTaskAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            int exitCode = await TaskSchedulerManager.InstallAsync(_config);
            if (exitCode != 0)
            {
                MessageBox.Show(this, "Die Aufgabe konnte nicht aktualisiert werden. Bitte SystemCare einmal als Administrator starten.", "SystemCare", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            await RefreshStatusAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _dryRunButton.Enabled = !busy;
        _installButton.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private static string ExtractLine(string text, string label)
    {
        string? line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => candidate.TrimStart().StartsWith(label, StringComparison.OrdinalIgnoreCase));
        if (line is null) return "Täglich";
        int separator = line.IndexOf(':');
        return separator >= 0 ? line[(separator + 1)..].Trim() : line.Trim();
    }

    private static void OpenPath(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return;
        }
        SystemCareConfig.EnsureConfigFile();
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
