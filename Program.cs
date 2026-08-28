using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace SystemCare;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            SelfTest.Run();
            return 0;
        }

        var config = SystemCareConfig.Load();
        if (args.Any(arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))) config.DryRun = true;
        int timeIndex = Array.FindIndex(args, arg => string.Equals(arg, "--time", StringComparison.OrdinalIgnoreCase));
        if (timeIndex >= 0)
        {
            if (timeIndex + 1 >= args.Length || !TimeSpan.TryParse(args[timeIndex + 1], out var requestedTime))
            {
                Console.Error.WriteLine("Ungültige Zeit. Erwartet wird HH:mm.");
                return 2;
            }
            config.DailyTime = requestedTime.ToString(@"hh\:mm");
        }

        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) || arg == "-h"))
        {
            Console.WriteLine(Usage.Text);
            return 0;
        }

        if (args.Any(arg => string.Equals(arg, "--install-task", StringComparison.OrdinalIgnoreCase)))
        {
            return await TaskSchedulerManager.InstallAsync(config);
        }

        if (args.Any(arg => string.Equals(arg, "--uninstall-task", StringComparison.OrdinalIgnoreCase)))
        {
            return await TaskSchedulerManager.UninstallAsync();
        }

        if (args.Any(arg => string.Equals(arg, "--status", StringComparison.OrdinalIgnoreCase)))
        {
            return await TaskSchedulerManager.StatusAsync();
        }

        if (!args.Any(arg => string.Equals(arg, "--run-once", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(Usage.Text);
            return 0;
        }

        return await SystemCareRunner.RunAsync(config);
    }
}

internal sealed class SystemCareConfig
{
    private static readonly string[] SafeDebloatAllowlist =
    {
        "Microsoft.3DBuilder",
        "Microsoft.BingNews",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",
        "Microsoft.MicrosoftOfficeHub",
        "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.People",
        "Microsoft.SkypeApp",
        "Microsoft.WindowsMaps",
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.YourPhone",
        "Microsoft.ZuneMusic",
        "Microsoft.ZuneVideo"
    };

    public bool DryRun { get; set; }
    public bool EnableWindowsUpdate { get; set; } = true;
    public bool IncludeDriverUpdates { get; set; } = true;
    public bool EnableWingetUpdates { get; set; } = true;
    public bool EnableTempCleanup { get; set; } = true;
    public bool EnableComponentCleanup { get; set; } = true;
    public bool EnableDebloat { get; set; } = true;
    public bool RemoveProvisionedPackages { get; set; } = false;
    public bool EnableGamingOptimization { get; set; } = true;
    public int TempFileAgeDays { get; set; } = 7;
    public string DailyTime { get; set; } = "03:15";
    public List<string> DebloatPackages { get; set; } = SafeDebloatAllowlist.ToList();

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamingSystemCare");

    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public static SystemCareConfig Load()
    {
        SystemCareConfig? config = null;
        try
        {
            if (File.Exists(ConfigPath))
            {
                config = JsonSerializer.Deserialize<SystemCareConfig>(File.ReadAllText(ConfigPath));
            }
        }
        catch
        {
            Console.Error.WriteLine("Warnung: config.json konnte nicht gelesen werden; Standardwerte werden verwendet.");
        }

        config ??= new SystemCareConfig();
        config.TempFileAgeDays = Math.Clamp(config.TempFileAgeDays, 1, 90);
        if (!TimeSpan.TryParse(config.DailyTime, out _)) config.DailyTime = "03:15";
        config.DebloatPackages = (config.DebloatPackages ?? new List<string>())
            .Where(candidate => SafeDebloatAllowlist.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return config;
    }

    public static void EnsureConfigFile()
    {
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(ConfigPath))
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new SystemCareConfig(), new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

internal static class SystemCareRunner
{
    private const string WindowsUpdateScript = @"
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$criteria = if ($env:SC_INCLUDE_DRIVERS -eq '1') { ""IsInstalled=0 and IsHidden=0 and (Type='Software' or Type='Driver')"" } else { ""IsInstalled=0 and IsHidden=0 and Type='Software'"" }
$search = $searcher.Search($criteria)
$updates = New-Object -ComObject Microsoft.Update.UpdateColl
for ($i = 0; $i -lt $search.Updates.Count; $i++) {
  $update = $search.Updates.Item($i)
  if (!$update.EulaAccepted) { $update.AcceptEula() }
  [void]$updates.Add($update)
}
$downloaded = 0
$installed = 0
$rebootRequired = $false
if ($updates.Count -gt 0) {
  $downloader = $session.CreateUpdateDownloader()
  $downloader.Updates = $updates
  $downloaded = $downloader.Download().ResultCode
  $installer = $session.CreateUpdateInstaller()
  $installer.Updates = $updates
  $result = $installer.Install()
  $installed = $result.ResultCode
  $rebootRequired = $result.RebootRequired
}
[pscustomobject]@{ Found = $updates.Count; DownloadResult = $downloaded; InstallResult = $installed; RebootRequired = $rebootRequired } | ConvertTo-Json -Compress
";

    public static async Task<int> RunAsync(SystemCareConfig config)
    {
        SystemCareConfig.EnsureConfigFile();
        string runId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        using var log = new AuditLog(runId);
        log.Write($"Start SystemCare dryRun={config.DryRun} admin={IsAdministrator()}");
        Console.WriteLine($"SystemCare {runId} – {(config.DryRun ? "DRY-RUN" : "Ausführung")}");

        bool isAdmin = IsAdministrator();
        if (!config.DryRun && isAdmin)
        {
            await RestorePointAsync(log);
            await BackupSystemStateAsync(runId, log);
        }
        else if (!isAdmin)
        {
            log.Write("Nicht erhöht gestartet; privilegierte Schritte werden übersprungen.");
            Console.WriteLine("Hinweis: Für Windows Update, DISM und App-Bereinigung als Administrator starten.");
        }

        if (config.EnableWindowsUpdate)
        {
            await WindowsUpdateAsync(config, log);
        }

        if (config.EnableWingetUpdates)
        {
            await WingetUpdateAsync(config, log);
        }

        if (config.EnableTempCleanup)
        {
            CleanTemporaryFiles(config, log);
        }

        if (config.EnableComponentCleanup && isAdmin)
        {
            await RunCommandAsync("dism.exe", ["/Online", "/Cleanup-Image", "/StartComponentCleanup", "/NoRestart"], TimeSpan.FromMinutes(30), config.DryRun, log);
        }

        if (config.EnableDebloat && isAdmin)
        {
            await DebloatAsync(config, log);
        }

        if (config.EnableGamingOptimization)
        {
            await OptimizeGamingAsync(config, log);
        }

        log.Write("SystemCare beendet.");
        Console.WriteLine($"Fertig. Protokoll: {log.Path}");
        return 0;
    }

    private static async Task WindowsUpdateAsync(SystemCareConfig config, AuditLog log)
    {
        if (!CommandExists("powershell.exe"))
        {
            log.Write("Windows Update übersprungen: powershell.exe fehlt.");
            return;
        }

        var environment = new Dictionary<string, string?>
        {
            ["SC_INCLUDE_DRIVERS"] = config.IncludeDriverUpdates ? "1" : "0"
        };
        await RunCommandAsync("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", WindowsUpdateScript],
            TimeSpan.FromMinutes(45), config.DryRun, log, environment);
    }

    private static async Task WingetUpdateAsync(SystemCareConfig config, AuditLog log)
    {
        if (!CommandExists("winget.exe"))
        {
            log.Write("WinGet übersprungen: winget.exe fehlt.");
            return;
        }

        await RunCommandAsync("winget.exe", ["upgrade", "--all", "--silent", "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"],
            TimeSpan.FromMinutes(45), config.DryRun, log);
    }

    private static void CleanTemporaryFiles(SystemCareConfig config, AuditLog log)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-config.TempFileAgeDays);
        var roots = new[] { Environment.GetEnvironmentVariable("TEMP"), Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows", "Temp") }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            int deleted = 0;
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(filePath);
                        if (info.LastWriteTimeUtc >= cutoff || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        log.Write($"TEMP delete {(config.DryRun ? "planned " : string.Empty)}{filePath}");
                        if (!config.DryRun) info.Delete();
                        deleted++;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                log.Write($"TEMP scan failed root={root} error={ex.GetType().Name}");
            }
            Console.WriteLine($"Temp {root}: {deleted} Datei(en) {(config.DryRun ? "geplant" : "bearbeitet")}");
        }
    }

    private static async Task DebloatAsync(SystemCareConfig config, AuditLog log)
    {
        if (config.DebloatPackages.Count == 0)
        {
            log.Write("Debloat übersprungen: Allowlist leer.");
            return;
        }

        string packages = string.Join(",", config.DebloatPackages.Select(name => $"'{name.Replace("'", "''")}'"));
        string script = $@"
$allow = @({packages})
$results = @()
Get-AppxPackage | Where-Object {{ $allow -contains $_.Name }} | ForEach-Object {{
  $name = $_.Name
  try {{ Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop; $results += [pscustomobject]@{{ Name=$name; Status='Removed' }} }}
  catch {{ $results += [pscustomobject]@{{ Name=$name; Status='Skipped' }} }}
}}
if ($env:SC_REMOVE_PROVISIONED -eq '1') {{
  Get-AppxProvisionedPackage -Online | Where-Object {{ $allow -contains $_.DisplayName }} | ForEach-Object {{
    try {{ Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop; $results += [pscustomobject]@{{ Name=$_.DisplayName; Status='ProvisionedRemoved' }} }} catch {{}}
  }}
}}
$results | ConvertTo-Json -Compress
";
        var environment = new Dictionary<string, string?> { ["SC_REMOVE_PROVISIONED"] = config.RemoveProvisionedPackages ? "1" : "0" };
        await RunCommandAsync("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            TimeSpan.FromMinutes(10), config.DryRun, log, environment);
    }

    private static async Task OptimizeGamingAsync(SystemCareConfig config, AuditLog log)
    {
        try
        {
            using var gameBar = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
            if (gameBar is null) throw new InvalidOperationException("GameBar-Schlüssel nicht verfügbar.");
            if (config.DryRun)
            {
                log.Write("Gaming optimization planned: Game Mode + High Performance.");
            }
            else
            {
                gameBar.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                gameBar.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                await RunCommandAsync("powercfg.exe", ["/setactive", "SCHEME_MIN"], TimeSpan.FromSeconds(30), false, log);
                log.Write("Gaming optimization applied: Game Mode + High Performance.");
            }
            Console.WriteLine($"Gaming-Optimierung: {(config.DryRun ? "geplant" : "aktiv")}");
        }
        catch (Exception ex)
        {
            log.Write($"Gaming optimization failed: {ex.GetType().Name}");
        }
    }

    private static async Task BackupSystemStateAsync(string runId, AuditLog log)
    {
        string backupDirectory = Path.Combine(SystemCareConfig.DataDirectory, "Backups", runId);
        Directory.CreateDirectory(backupDirectory);
        await RunCommandAsync("reg.exe", ["export", @"HKCU\Software\Microsoft\GameBar", Path.Combine(backupDirectory, "gamebar.reg"), "/y"],
            TimeSpan.FromSeconds(30), false, log);
        await RunCommandAsync("powercfg.exe", ["/getactivescheme"], TimeSpan.FromSeconds(30), false, log);
        log.Write($"Backup directory: {backupDirectory}");
    }

    private static async Task RestorePointAsync(AuditLog log)
    {
        const string script = "try { Checkpoint-Computer -Description 'GamingSystemCare before maintenance' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'created' } catch { 'unavailable' }";
        await RunCommandAsync("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            TimeSpan.FromMinutes(2), false, log);
    }

    private static async Task<CommandResult> RunCommandAsync(string fileName, IEnumerable<string> arguments, TimeSpan timeout,
        bool dryRun, AuditLog log, IReadOnlyDictionary<string, string?>? environment = null)
    {
        string argumentText = string.Join(' ', arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument));
        if (dryRun)
        {
            log.Write($"DRY-RUN: {fileName} {argumentText}");
            return new CommandResult(0, string.Empty, string.Empty);
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null) return new CommandResult(-1, string.Empty, "process start failed");
            using var timeoutCts = new CancellationTokenSource(timeout);
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(timeoutCts.Token));
            string output = outputTask.Result.Trim();
            string error = errorTask.Result.Trim();
            log.Write($"COMMAND {fileName} exit={process.ExitCode} stdout={Trim(output)} stderr={Trim(error)}");
            return new CommandResult(process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
            }
            catch { }
            log.Write($"COMMAND {fileName} timeout after {timeout.TotalMinutes:0}m");
            return new CommandResult(-1, string.Empty, "timeout");
        }
        catch (Exception ex)
        {
            log.Write($"COMMAND {fileName} failed={ex.GetType().Name}");
            return new CommandResult(-1, string.Empty, ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string Trim(string value) => value.Length <= 500 ? value : value[..500] + "…";

    private static bool CommandExists(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command);
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { if (File.Exists(Path.Combine(directory, command))) return true; } catch { }
        }
        return false;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

internal sealed record CommandResult(int ExitCode, string Output, string Error);

internal sealed class AuditLog : IDisposable
{
    private readonly StreamWriter _writer;
    public string Path { get; }

    public AuditLog(string runId)
    {
        string directory = SystemCareConfig.DataDirectory;
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, $"systemcare-{runId}.log");
        _writer = new StreamWriter(Path, append: true) { AutoFlush = true };
    }

    public void Write(string message)
    {
        string line = $"{DateTimeOffset.Now:O} {message}";
        _writer.WriteLine(line);
        Console.WriteLine(line);
    }

    public void Dispose() => _writer.Dispose();
}

internal static class TaskSchedulerManager
{
    private const string TaskName = "GamingSystemCare Daily";

    public static async Task<int> InstallAsync(SystemCareConfig config)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executablepfad fehlt.");
        if (!TimeSpan.TryParse(config.DailyTime, out var time)) time = new TimeSpan(3, 15, 0);
        string taskCommand = $"\"{executable}\" --run-once --scheduled";
        var result = await RunSchtasksAsync(["/Create", "/TN", TaskName, "/TR", taskCommand, "/SC", "DAILY", "/ST", time.ToString(@"hh\:mm"), "/RL", "HIGHEST", "/F"]);
        Console.WriteLine(result.Output.Length > 0 ? result.Output : result.Error);
        return result.ExitCode;
    }

    public static async Task<int> UninstallAsync()
    {
        var result = await RunSchtasksAsync(["/Delete", "/TN", TaskName, "/F"]);
        Console.WriteLine(result.Output.Length > 0 ? result.Output : result.Error);
        return result.ExitCode;
    }

    public static async Task<int> StatusAsync()
    {
        var result = await RunSchtasksAsync(["/Query", "/TN", TaskName, "/FO", "LIST"]);
        Console.WriteLine(result.Output.Length > 0 ? result.Output : result.Error);
        return result.ExitCode;
    }

    private static async Task<CommandResult> RunSchtasksAsync(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info);
            if (process is null) return new CommandResult(-1, string.Empty, "process start failed");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());
            return new CommandResult(process.ExitCode, outputTask.Result.Trim(), errorTask.Result.Trim());
        }
        catch (Exception ex) { return new CommandResult(-1, string.Empty, ex.Message); }
    }
}

internal static class Usage
{
    public const string Text = """
SystemCare – tägliche Systempflege für Windows 11

  SystemCare.exe --install-task       täglichen Lauf um 03:15 registrieren
  SystemCare.exe --run-once           Updates, Pflege und Gaming-Optimierung ausführen
  SystemCare.exe --run-once --dry-run nur anzeigen/protokollieren, nichts ändern
  SystemCare.exe --status              geplante Aufgabe anzeigen
  SystemCare.exe --uninstall-task      geplante Aufgabe entfernen
  SystemCare.exe --self-test           lokale Konfigurations-/Allowlist-Prüfung

Die Konfiguration liegt unter %LOCALAPPDATA%\GamingSystemCare\config.json.
""";
}

internal static class SelfTest
{
    public static void Run()
    {
        var config = new SystemCareConfig();
        if (config.DebloatPackages.Any(name => name.Contains("Xbox", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Xbox must never be in the safe debloat list.");
        if (config.DebloatPackages.Count == 0) throw new InvalidOperationException("Debloat allowlist is empty.");
        Console.WriteLine("SystemCare self-test: PASS");
    }
}
