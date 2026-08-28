using System.Diagnostics;

namespace SystemCare;

internal sealed class SystemCareForm : Form
{
    private readonly SystemCareConfig _config;
    private readonly Panel _pageHost = new();
    private readonly Label _pageTitle = new();
    private readonly Label _pageSubtitle = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 5000 };
    private Control? _currentPage;
    private string _currentPageName = "Automatik";
    private bool _busy;

    private List<UpdateInfo> _updates = new();
    private DataGridView? _updatesGrid;
    private Label? _updatesStatus;
    private Button? _scanUpdatesButton;
    private Button? _installAllUpdatesButton;

    private List<CleanupItem> _cleanupItems = new();
    private DataGridView? _cleanupGrid;
    private FlowLayoutPanel? _cleanupCategories;
    private Label? _cleanupStatus;
    private Button? _scanCleanupButton;
    private CheckBox? _fullCleanupScan;

    public SystemCareForm(SystemCareConfig config)
    {
        _config = config;
        Text = "SystemCare";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 680);
        Size = new Size(1240, 780);
        BackColor = Color.FromArgb(9, 14, 28);
        ForeColor = Color.FromArgb(229, 231, 235);
        Font = new Font("Segoe UI", 10F);

        BuildShell();
        ShowPage("Automatik", "Automatik-Übersicht", "Zeitplan, Schutzprofil und alle automatisch ausgeführten Aufgaben.", BuildAutomationPage);
        _refreshTimer.Tick += async (_, _) =>
        {
            if (_currentPageName == "Automatik") await RefreshAutomationAsync();
        };
        _refreshTimer.Start();
    }

    private void BuildShell()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 222));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42), Padding = new Padding(18, 22, 14, 18) };
        shell.Controls.Add(sidebar, 0, 0);
        sidebar.Controls.Add(new Label { AutoSize = true, Text = "SystemCare", Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(12, 8) });
        sidebar.Controls.Add(new Label { AutoSize = true, Text = "Windows · Gaming", Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(100, 116, 139), Location = new Point(14, 42) });

        var nav = new FlowLayoutPanel { Location = new Point(10, 92), Width = 184, Height = 410, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        sidebar.Controls.Add(nav);
        AddNavButton(nav, "Automatik-Übersicht", "Automatik", "Zeitplan und Aufgaben");
        AddNavButton(nav, "Updates", "Updates", "Scannen und installieren");
        AddNavButton(nav, "Bereinigung", "Bereinigung", "Dateien und Duplikate");
        AddNavButton(nav, "Empfohlene Verbesserungen", "Empfehlungen", "Offizielle Quellen");
        AddNavButton(nav, "Einstellungen", "Einstellungen", "Zeitplan und Optionen");
        sidebar.Controls.Add(new Label { AutoSize = false, Width = 178, Height = 94, Text = "SCHUTZ\n\n• Sicherheitskomponenten bleiben erhalten\n• Löschungen gehen in den Papierkorb\n• Keine ungeprüften Downloads", Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(14, 560) });

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(9, 14, 28), Padding = new Padding(30, 26, 30, 24) };
        shell.Controls.Add(content, 1, 0);
        _pageTitle.AutoSize = true;
        _pageTitle.Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold);
        _pageTitle.ForeColor = Color.White;
        _pageTitle.Location = new Point(30, 24);
        content.Controls.Add(_pageTitle);
        _pageSubtitle.AutoSize = true;
        _pageSubtitle.Font = new Font("Segoe UI", 10.5F);
        _pageSubtitle.ForeColor = Color.FromArgb(148, 163, 184);
        _pageSubtitle.Location = new Point(33, 66);
        content.Controls.Add(_pageSubtitle);
        _pageHost.Dock = DockStyle.Fill;
        _pageHost.Padding = new Padding(0, 112, 0, 0);
        _pageHost.BackColor = Color.Transparent;
        content.Controls.Add(_pageHost);
    }

    private void AddNavButton(FlowLayoutPanel nav, string text, string pageName, string description)
    {
        var button = new Button { Text = text, Tag = pageName, Width = 184, Height = 44, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = Color.FromArgb(203, 213, 225), Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold), Padding = new Padding(13, 0, 6, 0), Margin = new Padding(0, 0, 0, 6), Cursor = Cursors.Hand, AccessibleName = description };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) =>
        {
            switch (pageName)
            {
                case "Automatik": ShowPage(pageName, "Automatik-Übersicht", "Zeitplan, Schutzprofil und alle automatisch ausgeführten Aufgaben.", BuildAutomationPage); break;
                case "Updates": ShowPage(pageName, "Updates", "Verfügbare Windows- und Treiberupdates einzeln prüfen oder ausführen.", BuildUpdatesPage); break;
                case "Bereinigung": ShowPage(pageName, "Bereinigung", "Scan, Kategorien, Duplikate und kontrollierte Löschaktionen.", BuildCleanupPage); break;
                case "Empfehlungen": ShowPage(pageName, "Empfohlene Verbesserungen", "Offizielle Quellen prüfen und Empfehlungen nachvollziehbar anzeigen.", BuildRecommendationsPage); break;
                case "Einstellungen": ShowPage(pageName, "Einstellungen", "Zeitpunkt, Häufigkeit und automatische Funktionen festlegen.", BuildSettingsPage); break;
            }
        };
        nav.Controls.Add(button);
    }

    private void ShowPage(string pageName, string title, string subtitle, Func<Control> builder)
    {
        _currentPageName = pageName;
        _pageTitle.Text = title;
        _pageSubtitle.Text = subtitle;
        _currentPage?.Dispose();
        _pageHost.Controls.Clear();
        _currentPage = builder();
        _pageHost.Controls.Add(_currentPage);
    }

    private Control BuildAutomationPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildAutomationCards(), 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 7, 0, 0) };
        var dryRun = MakeButton("Jetzt prüfen (Dry-Run)", Color.FromArgb(37, 99, 235));
        dryRun.Click += async (_, _) => await RunDryRunAsync(dryRun);
        var task = MakeButton("Tagesaufgabe speichern", Color.FromArgb(5, 150, 105));
        task.Click += async (_, _) => await SaveConfigAndTaskAsync(task);
        var settings = MakeButton("Einstellungen öffnen", Color.FromArgb(55, 65, 81));
        settings.Click += (_, _) => ShowPage("Einstellungen", "Einstellungen", "Zeitpunkt, Häufigkeit und automatische Funktionen festlegen.", BuildSettingsPage);
        var logs = MakeButton("Logs öffnen", Color.FromArgb(55, 65, 81));
        logs.Click += (_, _) => OpenDirectory(SystemCareConfig.DataDirectory);
        actions.Controls.Add(dryRun);
        actions.Controls.Add(task);
        actions.Controls.Add(settings);
        actions.Controls.Add(logs);
        root.Controls.Add(actions, 0, 1);

        root.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = false, Text = $"Automatisch aktiviert: {EnabledAutomationSummary()}", ForeColor = Color.FromArgb(148, 163, 184), Padding = new Padding(2, 10, 0, 0) }, 0, 2);
        var details = CreateTextPanel("Aktueller Status");
        root.Controls.Add(details, 0, 3);
        _ = RefreshAutomationAsync(details);
        return root;
    }

    private Control BuildAutomationCards()
    {
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        cards.Controls.Add(BuildCard("AUTOMATIK", "Wird geprüft …", Color.FromArgb(52, 211, 153), "automationStatus"), 0, 0);
        cards.Controls.Add(BuildCard("NÄCHSTER LAUF", $"{FrequencyLabel()} {_config.DailyTime}", Color.FromArgb(96, 165, 250)), 1, 0);
        cards.Controls.Add(BuildCard("SCHUTZPROFIL", "Schonend · geschützt", Color.FromArgb(251, 191, 36)), 2, 0);
        return cards;
    }

    private static Control BuildCard(string caption, string value, Color accent, string? valueName = null)
    {
        var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), Padding = new Padding(16, 12, 12, 10), BackColor = Color.FromArgb(20, 28, 48) };
        card.Paint += (_, e) => { using var pen = new Pen(accent, 2F); e.Graphics.DrawLine(pen, 0, 0, 0, card.Height); };
        card.Controls.Add(new Label { AutoSize = true, Text = caption, Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold), ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(16, 12) });
        card.Controls.Add(new Label { Name = valueName ?? string.Empty, AutoSize = true, Text = value, Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(16, 44) });
        return card;
    }

    private Control BuildUpdatesPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 0) };
        _scanUpdatesButton = MakeButton("Updates scannen", Color.FromArgb(37, 99, 235));
        _scanUpdatesButton.Click += async (_, _) => await ScanUpdatesAsync();
        _installAllUpdatesButton = MakeButton("Alle Updates ausführen", Color.FromArgb(5, 150, 105));
        _installAllUpdatesButton.Click += async (_, _) => await InstallUpdatesAsync(_updates);
        actions.Controls.Add(_scanUpdatesButton);
        actions.Controls.Add(_installAllUpdatesButton);
        root.Controls.Add(actions, 0, 0);
        _updatesStatus = new Label { Dock = DockStyle.Fill, Text = "Noch kein Scan ausgeführt.", ForeColor = Color.FromArgb(148, 163, 184), Padding = new Padding(2, 8, 0, 0) };
        root.Controls.Add(_updatesStatus, 0, 1);
        _updatesGrid = CreateGrid();
        _updatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "KB / ID", Width = 110, Name = "kb" });
        _updatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Update", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, Name = "title" });
        _updatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Kategorie", Width = 170, Name = "category" });
        _updatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Größe", Width = 80, Name = "size" });
        _updatesGrid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "Aktion", Text = "Installieren", UseColumnTextForButtonValue = true, Width = 112, Name = "install" });
        var updatesGrid = _updatesGrid;
        updatesGrid.CellContentClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == updatesGrid.Columns["install"]!.Index && e.RowIndex < _updates.Count)
                await InstallUpdatesAsync(new[] { _updates[e.RowIndex] });
        };
        root.Controls.Add(_updatesGrid, 0, 2);
        return root;
    }

    private Control BuildCleanupPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 3, 0, 0) };
        _scanCleanupButton = MakeButton("Bereinigungsscan", Color.FromArgb(37, 99, 235));
        _scanCleanupButton.Click += async (_, _) => await ScanCleanupAsync();
        var deleteSelected = MakeButton("Markierte löschen", Color.FromArgb(185, 28, 28));
        deleteSelected.Click += async (_, _) => await DeleteMarkedCleanupAsync();
        var markAll = MakeButton("Alle markieren", Color.FromArgb(71, 85, 105));
        markAll.Click += (_, _) => SetCleanupSelection(markForDeletion: true);
        var clearMarks = MakeButton("Markierungen entfernen", Color.FromArgb(71, 85, 105));
        clearMarks.Click += (_, _) => SetCleanupSelection(markForDeletion: false);
        _fullCleanupScan = new CheckBox { Text = "Vollscan auf festen Laufwerken (langsamer)", AutoSize = true, ForeColor = Color.FromArgb(203, 213, 225), Padding = new Padding(8, 9, 0, 0), Checked = false };
        actions.Controls.Add(_scanCleanupButton);
        actions.Controls.Add(deleteSelected);
        actions.Controls.Add(markAll);
        actions.Controls.Add(clearMarks);
        actions.Controls.Add(_fullCleanupScan);
        root.Controls.Add(actions, 0, 0);

        _cleanupCategories = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 4) };
        root.Controls.Add(_cleanupCategories, 0, 1);
        _cleanupStatus = new Label { Dock = DockStyle.Fill, Text = "Noch kein Bereinigungsscan ausgeführt. Dateien werden nur aufgelistet; Löschen geht in den Papierkorb.", ForeColor = Color.FromArgb(148, 163, 184), Padding = new Padding(2, 8, 0, 0) };
        root.Controls.Add(_cleanupStatus, 0, 2);
        _cleanupGrid = CreateGrid();
        _cleanupGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Behalten (aus = löschen)", Width = 142, Name = "keep" });
        _cleanupGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Kategorie", Width = 155, Name = "category" });
        _cleanupGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pfad", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, Name = "path" });
        _cleanupGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Grund", Width = 255, Name = "reason" });
        _cleanupGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Größe", Width = 86, Name = "size" });
        _cleanupGrid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "Einzeln", Text = "Löschen", UseColumnTextForButtonValue = true, Width = 82, Name = "delete" });
        var cleanupGrid = _cleanupGrid;
        cleanupGrid.CurrentCellDirtyStateChanged += (_, _) => { if (cleanupGrid.IsCurrentCellDirty) cleanupGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        cleanupGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == cleanupGrid.Columns["keep"]!.Index && e.RowIndex < _cleanupItems.Count && cleanupGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value is bool keep)
                _cleanupItems[e.RowIndex].Keep = keep;
        };
        cleanupGrid.CellContentClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == cleanupGrid.Columns["delete"]!.Index && e.RowIndex < _cleanupItems.Count)
                await DeleteCleanupItemAsync(_cleanupItems[e.RowIndex]);
        };
        root.Controls.Add(_cleanupGrid, 0, 3);
        return root;
    }

    private Control BuildRecommendationsPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 3, 0, 0), BackColor = Color.Transparent };
        var research = MakeButton("Offizielle Quellen recherchieren", Color.FromArgb(37, 99, 235));
        var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent, Padding = new Padding(0, 3, 0, 0) };
        var status = new Label { Dock = DockStyle.Fill, Text = "Es werden nur offizielle Quellen abgefragt; Empfehlungen ändern nichts automatisch.", ForeColor = Color.FromArgb(148, 163, 184), Padding = new Padding(2, 8, 0, 0) };
        research.Click += async (_, _) =>
        {
            research.Enabled = false;
            status.Text = "Recherche läuft …";
            try
            {
                var recommendations = await RecommendationService.ResearchAsync();
                list.Controls.Clear();
                foreach (var recommendation in recommendations) list.Controls.Add(BuildRecommendationCard(recommendation));
                status.Text = $"{recommendations.Count} Empfehlungen geladen. Quellenstatus: {recommendations.Count(item => item.SourceReachable)}/{recommendations.Count} erreichbar.";
            }
            catch (Exception ex) { status.Text = $"Recherche fehlgeschlagen: {ex.Message}"; }
            finally { research.Enabled = true; }
        };
        actions.Controls.Add(research);
        root.Controls.Add(actions, 0, 0);
        root.Controls.Add(status, 0, 1);
        root.Controls.Add(list, 0, 2);
        return root;
    }

    private static Control BuildRecommendationCard(Recommendation recommendation)
    {
        var card = new Panel { Width = 720, Height = 78, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(14), BackColor = Color.FromArgb(20, 28, 48) };
        card.Controls.Add(new Label { AutoSize = true, Text = recommendation.Title, Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(14, 10) });
        card.Controls.Add(new Label { AutoSize = true, Text = recommendation.Description, ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(14, 35) });
        var link = new LinkLabel { AutoSize = true, Text = recommendation.SourceReachable ? "Quelle öffnen · online" : "Quelle öffnen · nicht geprüft", LinkColor = Color.FromArgb(96, 165, 250), Location = new Point(14, 55) };
        link.Click += (_, _) => Process.Start(new ProcessStartInfo(recommendation.SourceUrl) { UseShellExecute = true });
        card.Controls.Add(link);
        return card;
    }

    private Control BuildSettingsPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 286));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var group = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 28, 48), Padding = new Padding(18) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 0 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var time = new TextBox { Text = _config.DailyTime, Width = 110, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var frequency = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White };
        frequency.Items.AddRange(new object[] { "Täglich", "Wöchentlich" });
        frequency.SelectedIndex = _config.ScheduleFrequency.Equals("WEEKLY", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var day = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White };
        day.Items.AddRange(new object[] { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" });
        day.SelectedIndex = DayIndex(_config.ScheduleDayOfWeek);
        var age = new NumericUpDown { Minimum = 1, Maximum = 90, Value = Math.Clamp(_config.TempFileAgeDays, 1, 90), Width = 110, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White };
        AddSettingsRow(grid, "Uhrzeit (HH:mm)", time);
        AddSettingsRow(grid, "Häufigkeit", frequency);
        AddSettingsRow(grid, "Wochentag", day);
        AddSettingsRow(grid, "Temp-Dateien älter als", age);
        foreach (var check in new[]
        {
            MakeCheck("Windows- und Treiberupdates", _config.EnableWindowsUpdate, value => _config.EnableWindowsUpdate = value),
            MakeCheck("WinGet-Updates", _config.EnableWingetUpdates, value => _config.EnableWingetUpdates = value),
            MakeCheck("Temporäre Dateien bereinigen", _config.EnableTempCleanup, value => _config.EnableTempCleanup = value),
            MakeCheck("Windows-Komponenten bereinigen", _config.EnableComponentCleanup, value => _config.EnableComponentCleanup = value),
            MakeCheck("Sichere Consumer-App-Allowlist", _config.EnableDebloat, value => _config.EnableDebloat = value),
            MakeCheck("Gaming-Optimierung", _config.EnableGamingOptimization, value => _config.EnableGamingOptimization = value)
        })
        {
            grid.Controls.Add(check, 0, grid.RowCount);
            grid.SetColumnSpan(check, 2);
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.RowCount++;
        }
        group.Controls.Add(grid);
        root.Controls.Add(group, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 7, 0, 0), BackColor = Color.Transparent };
        var save = MakeButton("Speichern & Task aktualisieren", Color.FromArgb(5, 150, 105));
        save.Click += async (_, _) =>
        {
            if (!TimeSpan.TryParse(time.Text, out var parsed) || parsed.TotalHours >= 24) { MessageBox.Show(this, "Bitte eine gültige Zeit im Format HH:mm eingeben.", "SystemCare", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _config.DailyTime = parsed.ToString(@"hh\:mm");
            _config.ScheduleFrequency = frequency.SelectedIndex == 1 ? "WEEKLY" : "DAILY";
            _config.ScheduleDayOfWeek = DayCode(day.SelectedIndex);
            _config.TempFileAgeDays = (int)age.Value;
            _config.Save();
            await SaveConfigAndTaskAsync(save);
        };
        actions.Controls.Add(save);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = false, Text = "Die Übersicht trennt Scan und Ausführung. Empfehlungen recherchieren nur offizielle Quellen. Updates und Bereinigung werden nicht stillschweigend aus der Oberfläche gelöscht.", ForeColor = Color.FromArgb(148, 163, 184), Padding = new Padding(2, 10, 0, 0) }, 0, 2);
        return root;
    }

    private async Task RefreshAutomationAsync(Control? details = null)
    {
        var result = await TaskSchedulerManager.QueryAsync();
        if (details?.Controls.OfType<RichTextBox>().FirstOrDefault() is { } box)
            box.Text = result.ExitCode == 0 ? result.Output : "Keine geplante Aufgabe gefunden.\n\nSpeichere die Task-Einstellungen, um sie zu registrieren.";
        if (_currentPageName == "Automatik" && _currentPage is not null)
        {
            var statusLabels = _currentPage.Controls.Find("automationStatus", true);
            if (statusLabels.Length > 0) statusLabels[0].Text = result.ExitCode == 0 ? "Aktiv" : "Nicht installiert";
        }
    }

    private async Task RunDryRunAsync(Button button)
    {
        if (_busy) return;
        _busy = true;
        SetBusy(button, true);
        try
        {
            var dryConfig = SystemCareConfig.Load();
            dryConfig.DryRun = true;
            await SystemCareRunner.RunAsync(dryConfig);
            MessageBox.Show(this, "Dry-Run abgeschlossen. Es wurden keine Systemänderungen vorgenommen. Der Bericht liegt im Log-Ordner.", "SystemCare", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Dry-Run fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(button, false); _busy = false; await RefreshAutomationAsync(); }
    }

    private async Task SaveConfigAndTaskAsync(Button button)
    {
        if (_busy) return;
        _busy = true;
        SetBusy(button, true);
        try
        {
            _config.Save();
            int exitCode = await TaskSchedulerManager.InstallAsync(_config);
            if (exitCode != 0) MessageBox.Show(this, "Die Task-Aktualisierung benötigt Administratorrechte. Starte SystemCare einmal als Administrator.", "SystemCare", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else MessageBox.Show(this, "Konfiguration gespeichert und tägliche Aufgabe aktualisiert.", "SystemCare", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Task-Aktualisierung fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(button, false); _busy = false; }
    }

    private async Task ScanUpdatesAsync()
    {
        if (_scanUpdatesButton is null || _updatesStatus is null || _updatesGrid is null) return;
        SetBusy(_scanUpdatesButton, true);
        try
        {
            _updatesStatus.Text = "Windows Update wird abgefragt …";
            _updates = (await WindowsUpdateService.ScanAsync(_config.IncludeDriverUpdates)).ToList();
            PopulateUpdatesGrid();
            _updatesStatus.Text = _updates.Count == 0 ? "Keine verfügbaren Updates gefunden." : $"{_updates.Count} Updates gefunden. Jedes Update kann einzeln installiert werden.";
        }
        catch (Exception ex) { _updatesStatus.Text = ex.Message; }
        finally { SetBusy(_scanUpdatesButton, false); }
    }

    private async Task InstallUpdatesAsync(IEnumerable<UpdateInfo> updates)
    {
        var selected = updates.ToList();
        if (selected.Count == 0) { MessageBox.Show(this, "Bitte zuerst einen Update-Scan durchführen.", "SystemCare", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (!SystemCareRunner.IsAdministrator()) { MessageBox.Show(this, "Die Windows-Update-Installation benötigt Administratorrechte. Starte SystemCare über 'Als Administrator ausführen'.", "SystemCare", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (MessageBox.Show(this, $"{selected.Count} Update(s) installieren?", "Windows Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(_installAllUpdatesButton, true);
        try
        {
            foreach (var update in selected)
            {
                _updatesStatus!.Text = $"Installiere: {update.Title}";
                var result = await WindowsUpdateService.InstallAsync(update.UpdateId, _config.IncludeDriverUpdates);
                if (!result.Success) MessageBox.Show(this, result.Message, "Update nicht installiert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            await ScanUpdatesAsync();
        }
        catch (Exception ex) { _updatesStatus!.Text = ex.Message; }
        finally { SetBusy(_installAllUpdatesButton, false); }
    }

    private async Task ScanCleanupAsync()
    {
        if (_scanCleanupButton is null || _cleanupStatus is null) return;
        SetBusy(_scanCleanupButton, true);
        try
        {
            _cleanupStatus.Text = "Bereinigungsscan läuft …";
            var progress = new Progress<string>(message => { if (!IsDisposed) _cleanupStatus.Text = message; });
            var result = await CleanupScanner.ScanAsync(_fullCleanupScan?.Checked == true, _config.TempFileAgeDays, progress);
            _cleanupItems = result.Items.ToList();
            PopulateCleanupGrid();
            PopulateCleanupCategories(result.Categories);
            _cleanupStatus.Text = $"{_cleanupItems.Count:N0} Elemente in {result.Categories.Count} Kategorien gefunden; Scanzeit {result.Duration.TotalSeconds:0.0}s. Alle stehen zunächst auf Behalten." + (string.IsNullOrEmpty(result.Warning) ? string.Empty : $" {result.Warning}");
        }
        catch (OperationCanceledException) { _cleanupStatus.Text = "Scan abgebrochen."; }
        catch (Exception ex) { _cleanupStatus.Text = $"Scan fehlgeschlagen: {ex.Message}"; }
        finally { SetBusy(_scanCleanupButton, false); }
    }

    private async Task DeleteMarkedCleanupAsync()
    {
        await DeleteCleanupItemsAsync(_cleanupItems.Where(item => !item.Keep).ToList(), "markierten");
    }

    private void SetCleanupSelection(bool markForDeletion)
    {
        foreach (var item in _cleanupItems) item.Keep = !markForDeletion;
        PopulateCleanupGrid();
        if (_cleanupStatus is not null)
        {
            _cleanupStatus.Text = markForDeletion
                ? $"{_cleanupItems.Count:N0} Elemente für 'Markierte löschen' vorgemerkt."
                : "Alle Markierungen entfernt; alle Elemente stehen wieder auf Behalten.";
        }
    }

    private async Task DeleteCleanupItemAsync(CleanupItem item)
    {
        if (MessageBox.Show(this, $"In den Papierkorb verschieben?\n\n{item.Path}", "Datei löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await DeleteCleanupItemsAsync(new[] { item }, "einzelnen");
    }

    private async Task DeleteCleanupItemsAsync(IReadOnlyList<CleanupItem> items, string description)
    {
        if (items.Count == 0) { MessageBox.Show(this, "Keine Elemente zum Löschen markiert. Entferne zuerst den Haken bei 'Behalten'.", "Bereinigung", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (items.Count > 1 && MessageBox.Show(this, $"{items.Count} {description} Elemente in den Papierkorb verschieben?", "Bereinigung", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        int deleted = 0;
        foreach (var item in items)
        {
            try { await CleanupScanner.SendToRecycleBinAsync(item); _cleanupItems.Remove(item); deleted++; }
            catch (Exception ex) { _cleanupStatus!.Text = $"Übersprungen: {item.Path} – {ex.Message}"; }
        }
        PopulateCleanupGrid();
        if (_cleanupStatus is not null) _cleanupStatus.Text = $"{deleted} Element(e) in den Papierkorb verschoben. Scanliste aktualisiert.";
    }

    private void PopulateUpdatesGrid()
    {
        if (_updatesGrid is null) return;
        _updatesGrid.Rows.Clear();
        foreach (var update in _updates) _updatesGrid.Rows.Add(update.KbArticles, update.Title, update.Categories, $"{update.SizeMb:0.0} MB");
    }

    private void PopulateCleanupGrid()
    {
        if (_cleanupGrid is null) return;
        _cleanupGrid.Rows.Clear();
        foreach (var item in _cleanupItems) _cleanupGrid.Rows.Add(item.Keep, item.Category, item.Path, item.Reason, FormatBytes(item.SizeBytes));
    }

    private void PopulateCleanupCategories(IReadOnlyList<CleanupCategorySummary> summaries)
    {
        if (_cleanupCategories is null) return;
        _cleanupCategories.Controls.Clear();
        foreach (var summary in summaries)
        {
            var card = new Panel { Width = 218, Height = 68, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(10), BackColor = Color.FromArgb(20, 28, 48) };
            card.Controls.Add(new Label { AutoSize = true, Text = summary.Category, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold), Location = new Point(10, 8) });
            card.Controls.Add(new Label { AutoSize = true, Text = $"{summary.Count} · {FormatBytes(summary.SizeBytes)}", ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(10, 31) });
            var button = new Button { Text = "Kategorie bereinigen", AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(127, 29, 29), ForeColor = Color.White, Location = new Point(112, 28), Padding = new Padding(4, 0, 4, 0) };
            // A category action is explicit and independent of row checkboxes:
            // it always operates on every item currently in that category.
            button.Click += async (_, _) => await DeleteCleanupItemsAsync(_cleanupItems.Where(item => item.Category.Equals(summary.Category, StringComparison.OrdinalIgnoreCase)).ToList(), $"der Kategorie '{summary.Category}'");
            card.Controls.Add(button);
            _cleanupCategories.Controls.Add(card);
        }
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false, BackgroundColor = Color.FromArgb(15, 23, 42), BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(30, 41, 59), RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, ReadOnly = false, EnableHeadersVisualStyles = false };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.FromArgb(203, 213, 225), Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold), SelectionBackColor = Color.FromArgb(30, 41, 59) };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(15, 23, 42), ForeColor = Color.FromArgb(226, 232, 240), SelectionBackColor = Color.FromArgb(30, 64, 115), SelectionForeColor = Color.White, Padding = new Padding(5, 0, 5, 0) };
        return grid;
    }

    private static Control CreateTextPanel(string title)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42), Padding = new Padding(16) };
        panel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 30, Text = title, Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold), ForeColor = Color.White });
        panel.Controls.Add(new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = panel.BackColor, ForeColor = Color.FromArgb(203, 213, 225), Font = new Font("Consolas", 9.5F) });
        return panel;
    }

    private static Button MakeButton(string text, Color color)
    {
        return new Button { Text = text, AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = color, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold), Padding = new Padding(12, 0, 12, 0), Margin = new Padding(0, 0, 10, 0), Cursor = Cursors.Hand };
    }

    private static CheckBox MakeCheck(string text, bool value, Action<bool> changed)
    {
        var check = new CheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = Color.FromArgb(203, 213, 225), FlatStyle = FlatStyle.Flat };
        check.CheckedChanged += (_, _) => changed(check.Checked);
        return check;
    }

    private static void AddSettingsRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        grid.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Color.FromArgb(156, 163, 175), Padding = new Padding(0, 7, 0, 0) }, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private static void SetBusy(Button? button, bool busy)
    {
        if (button is not null) button.Enabled = !busy;
    }

    private string EnabledAutomationSummary()
    {
        var items = new List<string>();
        if (_config.EnableWindowsUpdate) items.Add("Windows Update");
        if (_config.EnableWingetUpdates) items.Add("WinGet");
        if (_config.EnableTempCleanup) items.Add("Temp");
        if (_config.EnableDebloat) items.Add("Allowlist");
        if (_config.EnableGamingOptimization) items.Add("Gaming");
        return items.Count == 0 ? "keine" : string.Join(", ", items);
    }

    private string FrequencyLabel() => _config.ScheduleFrequency.Equals("WEEKLY", StringComparison.OrdinalIgnoreCase) ? "Wöchentlich" : "Täglich";

    private static int DayIndex(string code)
    {
        int index = Array.IndexOf(new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" }, code.ToUpperInvariant());
        return index >= 0 ? index : 0;
    }

    private static string DayCode(int index) => new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" }[Math.Clamp(index, 0, 6)];

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024d / 1024d:0.0} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / 1024d / 1024d:0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:0.0} KB";
        return $"{bytes} B";
    }

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        base.OnFormClosed(e);
    }
}
