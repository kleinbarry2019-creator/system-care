using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SystemCare;

internal sealed record UpdateInfo(string UpdateId, string Title, string KbArticles, string Categories, double SizeMb);

internal sealed record UpdateInstallResult(bool Success, string Title, string Message, bool RebootRequired);

internal static class WindowsUpdateService
{
    private const string ScanScript = @"
$ErrorActionPreference = 'Stop'
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$criteria = if ($env:SC_INCLUDE_DRIVERS -eq '1') { 'IsInstalled=0 and IsHidden=0 and (Type=''Software'' or Type=''Driver'')' } else { 'IsInstalled=0 and IsHidden=0 and Type=''Software''' }
$search = $searcher.Search($criteria)
$items = @()
for ($i = 0; $i -lt $search.Updates.Count; $i++) {
  $update = $search.Updates.Item($i)
  $items += [pscustomobject]@{
    UpdateId = [string]$update.Identity.UpdateID
    Title = [string]$update.Title
    KbArticles = (@($update.KBArticleIDs) -join ', ')
    Categories = (@($update.Categories | ForEach-Object { $_.Name }) -join ', ')
    SizeMb = [math]::Round(([double]$update.MaxDownloadSize / 1MB), 1)
  }
}
@($items) | ConvertTo-Json -Compress -Depth 4
";

    private const string InstallScriptTemplate = @"
$ErrorActionPreference = 'Stop'
$wantedId = '__UPDATE_ID__'
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$criteria = if ($env:SC_INCLUDE_DRIVERS -eq '1') { 'IsInstalled=0 and IsHidden=0 and (Type=''Software'' or Type=''Driver'')' } else { 'IsInstalled=0 and IsHidden=0 and Type=''Software''' }
$search = $searcher.Search($criteria)
$target = $null
for ($i = 0; $i -lt $search.Updates.Count; $i++) {
  $candidate = $search.Updates.Item($i)
  if ([string]$candidate.Identity.UpdateID -eq $wantedId) { $target = $candidate; break }
}
if ($null -eq $target) {
  [pscustomobject]@{ Success = $false; Title = ''; Message = 'Update ist nicht mehr verfügbar oder bereits installiert.'; RebootRequired = $false } | ConvertTo-Json -Compress
  exit 0
}
if (!$target.EulaAccepted) { $target.AcceptEula() }
$updates = New-Object -ComObject Microsoft.Update.UpdateColl
[void]$updates.Add($target)
$downloader = $session.CreateUpdateDownloader()
$downloader.Updates = $updates
$downloadResult = $downloader.Download().ResultCode
$installer = $session.CreateUpdateInstaller()
$installer.Updates = $updates
$result = $installer.Install()
[pscustomobject]@{
  Success = ($result.ResultCode -eq 2)
  Title = [string]$target.Title
  Message = ('DownloadResult=' + $downloadResult + ' InstallResult=' + $result.ResultCode)
  RebootRequired = [bool]$result.RebootRequired
} | ConvertTo-Json -Compress
";

    public static async Task<IReadOnlyList<UpdateInfo>> ScanAsync(bool includeDrivers, CancellationToken cancellationToken = default)
    {
        var environment = new Dictionary<string, string?> { ["SC_INCLUDE_DRIVERS"] = includeDrivers ? "1" : "0" };
        var result = await ProcessRunner.RunPowerShellAsync(ScanScript, TimeSpan.FromMinutes(30), cancellationToken, environment);
        if (result.ExitCode != 0) throw new InvalidOperationException(FormatError("Windows-Update-Scan", result));
        if (string.IsNullOrWhiteSpace(result.Output)) return Array.Empty<UpdateInfo>();
        try
        {
            return JsonSerializer.Deserialize<List<UpdateInfo>>(result.Output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<UpdateInfo>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Die Windows-Update-Antwort konnte nicht gelesen werden.", ex);
        }
    }

    public static async Task<UpdateInstallResult> InstallAsync(string updateId, bool includeDrivers, CancellationToken cancellationToken = default)
    {
        string script = InstallScriptTemplate.Replace("__UPDATE_ID__", EscapePowerShellLiteral(updateId), StringComparison.Ordinal);
        var environment = new Dictionary<string, string?> { ["SC_INCLUDE_DRIVERS"] = includeDrivers ? "1" : "0" };
        var result = await ProcessRunner.RunPowerShellAsync(script, TimeSpan.FromMinutes(90), cancellationToken, environment);
        if (result.ExitCode != 0) throw new InvalidOperationException(FormatError("Windows-Update-Installation", result));
        try
        {
            return JsonSerializer.Deserialize<UpdateInstallResult>(result.Output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new UpdateInstallResult(false, string.Empty, "Keine Antwort vom Windows-Update-Dienst.", false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Die Windows-Update-Antwort konnte nicht gelesen werden.", ex);
        }
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string FormatError(string operation, CommandResult result) =>
        $"{operation} fehlgeschlagen (Exit {result.ExitCode}). {result.Error}".Trim();
}

internal sealed class CleanupItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Category { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool Keep { get; set; } = true;
}

internal sealed record CleanupCategorySummary(string Category, int Count, long SizeBytes);

internal sealed record CleanupScanResult(IReadOnlyList<CleanupItem> Items, IReadOnlyList<CleanupCategorySummary> Categories,
    IReadOnlyList<string> Roots, string Warning, TimeSpan Duration);

internal static class CleanupScanner
{
    private const long DuplicateMinimumSize = 1024;
    private const long LargeFileMinimumSize = 512L * 1024 * 1024;
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".doc", ".docx", ".odt", ".pdf", ".rtf", ".txt", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".csv" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".ts" };
    private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".raw" };
    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".msi", ".msix", ".zip", ".7z", ".rar", ".iso", ".img" };

    public static Task<CleanupScanResult> ScanAsync(bool fullScan, int ageDays, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(fullScan, Math.Clamp(ageDays, 1, 90), progress, cancellationToken), cancellationToken);
    }

    public static Task SendToRecycleBinAsync(CleanupItem item, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(item.Path)) throw new FileNotFoundException("Datei nicht mehr vorhanden.", item.Path);
            SendToRecycleBinSilently(item.Path);
        }, cancellationToken);
    }

    private static void SendToRecycleBinSilently(string path)
    {
        // SHFileOperation preserves the recycle-bin behavior while suppressing
        // Windows error dialogs. Protected files are reported to the UI and
        // skipped by the caller instead of interrupting the whole cleanup run.
        var operation = new ShFileOperation
        {
            WindowHandle = IntPtr.Zero,
            Function = 3, // FO_DELETE
            From = path + "\0\0",
            To = string.Empty,
            Flags = 0x0004 | 0x0010 | 0x0040 | 0x0200 | 0x0400,
            AnyOperationsAborted = 0,
            NameMappings = IntPtr.Zero,
            ProgressTitle = string.Empty
        };
        int result = SHFileOperation(ref operation);
        if (result != 0)
            throw new IOException($"Datei konnte nicht in den Papierkorb verschoben werden (Windows-Fehler {result}).");
        if (operation.AnyOperationsAborted != 0)
            throw new IOException("Datei wurde von Windows nicht verschoben.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr WindowHandle;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOperation operation);

    private static CleanupScanResult Scan(bool fullScan, int ageDays, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var roots = ResolveRoots(fullScan);
        var files = new List<FileCandidate>();
        var warnings = new List<string>();
        int visited = 0;

        foreach (string root in roots)
        {
            progress?.Report($"Scanne {root}");
            foreach (string path in EnumerateFiles(root, fullScan, cancellationToken, warnings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    files.Add(new FileCandidate(path, info.Length, info.LastWriteTimeUtc));
                    visited++;
                    if (visited % 250 == 0) progress?.Report($"{visited:N0} Dateien geprüft");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                {
                    warnings.Add($"{path}: {ex.GetType().Name}");
                }
            }
        }

        DateTime cutoff = DateTime.UtcNow.AddDays(-ageDays);
        DateTime oldDownloadCutoff = DateTime.UtcNow.AddDays(-30);
        var items = new Dictionary<string, CleanupItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? category = GetMediaCategory(file.Path);
            if (IsTemporaryPath(file.Path) && file.LastWriteUtc < cutoff)
            {
                AddItem(items, new CleanupItem { Category = "Temporäre Dateien", Path = file.Path, SizeBytes = file.Size, Reason = $"älter als {ageDays} Tage" });
            }
            else if (IsInDownloads(file.Path) && file.LastWriteUtc < oldDownloadCutoff && InstallerExtensions.Contains(Path.GetExtension(file.Path)))
            {
                AddItem(items, new CleanupItem { Category = "Alte Downloads", Path = file.Path, SizeBytes = file.Size, Reason = "alter Installer/Archiv-Download" });
            }
            else if (file.Size >= LargeFileMinimumSize)
            {
                AddItem(items, new CleanupItem { Category = "Große Dateien", Path = file.Path, SizeBytes = file.Size, Reason = "größer als 512 MB – manuell prüfen" });
            }
        }

        var duplicateGroups = files
            .Where(file => file.Size >= DuplicateMinimumSize && GetMediaCategory(file.Path) is not null)
            .GroupBy(file => (Category: GetMediaCategory(file.Path)!, file.Size));
        foreach (var group in duplicateGroups)
        {
            var hashGroups = new Dictionary<string, List<FileCandidate>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Prüfe Duplikate: {Path.GetFileName(file.Path)}");
                try
                {
                    string hash = ComputeSha256(file.Path);
                    if (!hashGroups.TryGetValue(hash, out var matching)) hashGroups[hash] = matching = new List<FileCandidate>();
                    matching.Add(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                {
                    warnings.Add($"Hash {file.Path}: {ex.GetType().Name}");
                }
            }

            foreach (var matching in hashGroups.Values.Where(filesWithSameHash => filesWithSameHash.Count > 1))
            {
                var preferred = matching.OrderBy(file => file.Path.Length).ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase).First();
                string duplicateCategory = group.Key.Category switch
                {
                    "Dokumente" => "Doppelte Dokumente",
                    "Audio" => "Doppelte Audio",
                    "Videos" => "Doppelte Videos",
                    "Fotos" => "Doppelte Fotos",
                    _ => "Doppelte Dateien"
                };
                foreach (var file in matching)
                {
                    string reason = file.Path.Equals(preferred.Path, StringComparison.OrdinalIgnoreCase)
                        ? "Duplikatgruppe – Behalten wird empfohlen"
                        : $"identischer Inhalt wie {Path.GetFileName(preferred.Path)}";
                    if (!items.ContainsKey(file.Path))
                    {
                        AddItem(items, new CleanupItem { Category = duplicateCategory, Path = file.Path, SizeBytes = file.Size, Reason = reason });
                    }
                }
            }
        }

        var categories = items.Values
            .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new CleanupCategorySummary(group.Key, group.Count(), group.Sum(item => item.SizeBytes)))
            .ToList();
        string warning = warnings.Count == 0 ? string.Empty : $"{warnings.Count:N0} Dateien konnten wegen Zugriff/Änderung nicht gelesen werden.";
        return new CleanupScanResult(items.Values.OrderBy(item => item.Category).ThenBy(item => item.Path).ToList(), categories, roots, warning, started.Elapsed);
    }

    private static void AddItem(IDictionary<string, CleanupItem> items, CleanupItem item)
    {
        if (!items.ContainsKey(item.Path)) items[item.Path] = item;
    }

    private static IReadOnlyList<string> ResolveRoots(bool fullScan)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Path.Combine(profile, "Downloads"),
            Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(),
            Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows", "Temp")
        };
        if (fullScan)
        {
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)) roots.Add(drive.RootDirectory.FullName);
        }
        return roots.Where(Directory.Exists).Select(Path.GetFullPath).ToList();
    }

    private static IEnumerable<string> EnumerateFiles(string root, bool fullScan, CancellationToken cancellationToken, ICollection<string> warnings)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            if (fullScan && IsProtectedDirectory(directory)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { warnings.Add($"{directory}: {ex.GetType().Name}"); continue; }
            foreach (string file in files) yield return file;
            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(directory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { warnings.Add($"{directory}: {ex.GetType().Name}"); continue; }
            foreach (string child in directories) if (!IsProtectedDirectory(child)) pending.Push(child);
        }
    }

    private static bool IsProtectedDirectory(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string windows = Path.GetFullPath(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string programFilesX86 = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)
            || full.Contains("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase)
            || full.Contains("\\System Volume Information\\", StringComparison.OrdinalIgnoreCase)
            || full.Contains("\\AppData\\Local\\Packages\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTemporaryPath(string path) => path.Contains("\\AppData\\Local\\Temp\\", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\Windows\\Temp\\", StringComparison.OrdinalIgnoreCase);

    private static bool IsInDownloads(string path) => path.Contains("\\Downloads\\", StringComparison.OrdinalIgnoreCase);

    private static string? GetMediaCategory(string path)
    {
        string extension = Path.GetExtension(path);
        if (DocumentExtensions.Contains(extension)) return "Dokumente";
        if (AudioExtensions.Contains(extension)) return "Audio";
        if (VideoExtensions.Contains(extension)) return "Videos";
        if (PhotoExtensions.Contains(extension)) return "Fotos";
        return null;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record FileCandidate(string Path, long Size, DateTime LastWriteUtc);
}

internal sealed record Recommendation(string Title, string Description, string SourceUrl, bool SourceReachable);

internal static class RecommendationService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static readonly (string Title, string Description, string Url)[] Sources =
    {
        ("Windows Update prüfen", "Regelmäßig Windows- und Treiberupdates über den Microsoft-Dienst prüfen.", "https://support.microsoft.com/windows/windows-update-faq"),
        ("Game Mode kontrollieren", "Den Windows-Spielemodus aktiviert lassen und nach großen Windows-Updates erneut prüfen.", "https://support.microsoft.com/windows/get-to-know-game-bar-on-windows"),
        ("Grafiktreiber prüfen", "Für NVIDIA-Grafikkarten den Treiber nur über die offizielle Download-Seite prüfen.", "https://www.nvidia.com/Download/index.aspx"),
        ("Defender und Firewall erhalten", "Sicherheitskomponenten nicht für vermeintliche Gaming-Optimierungen deaktivieren.", "https://learn.microsoft.com/windows/security/operating-system-security/system-security/windows-defender"),
        ("Speicherplatz beobachten", "Große oder doppelte persönliche Dateien zuerst prüfen und nur bewusst in den Papierkorb verschieben.", "https://support.microsoft.com/windows/free-up-drive-space-in-windows")
    };

    public static async Task<IReadOnlyList<Recommendation>> ResearchAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Recommendation>();
        foreach (var source in Sources)
        {
            bool reachable = false;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                reachable = response.IsSuccessStatusCode;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            results.Add(new Recommendation(source.Title, source.Description, source.Url, reachable));
        }
        return results;
    }
}

internal static class ProcessRunner
{
    public static async Task<CommandResult> RunPowerShellAsync(string script, TimeSpan timeout, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }) info.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        }
        return await RunAsync(info, timeout, cancellationToken);
    }

    private static async Task<CommandResult> RunAsync(ProcessStartInfo info, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            process = Process.Start(info);
            if (process is null) return new CommandResult(-1, string.Empty, "process start failed");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(timeoutCts.Token));
            return new CommandResult(process.ExitCode, outputTask.Result.Trim(), errorTask.Result.Trim());
        }
        catch (OperationCanceledException)
        {
            try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch { }
            return new CommandResult(-1, string.Empty, cancellationToken.IsCancellationRequested ? "abgebrochen" : "Zeitüberschreitung");
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, string.Empty, ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }
}

