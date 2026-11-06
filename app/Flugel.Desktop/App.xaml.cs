using System.IO;
using System.Windows;
using System.Windows.Threading;
using Flugel.Core;

namespace Flugel.Desktop;

public partial class App : Application
{
    internal static App Instance { get; private set; } = null!;
    internal SingleInstanceIpcServer? IpcServer { get; private set; }
    internal MainWindow? MainWin { get; private set; }
    private System.Windows.Forms.NotifyIcon? _tray;
    private DispatcherTimer? _clipboardTimer;
    private string _lastClipboardText = "";
    private bool _clipboardSuppressed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Instance = this;
        string? smokeDirectory = null;
        try
        {
            string? url = null, fileName = null, referrer = null, mediaQuality = null;
            List<string>? segmentUrls = null;
            string? cookieHeader = null, pendingPath = null;
            string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flugel Download Manager");
            bool explicitDirectory = false, smoke = false;
            for (int i = 0; i < e.Args.Length; i++)
            {
                switch (e.Args[i])
                {
                    case "--pending-request" when i + 1 < e.Args.Length:
                        pendingPath = Path.GetFullPath(e.Args[++i]);
                        break;
                    case "--url" when i + 1 < e.Args.Length:
                        url = e.Args[++i];
                        ValidateUrl(url);
                        break;
                    case "--filename" when i + 1 < e.Args.Length:
                        fileName = e.Args[++i];
                        break;
                    case "--referrer" when i + 1 < e.Args.Length:
                        referrer = e.Args[++i];
                        break;
                    case "--media-quality" when i + 1 < e.Args.Length:
                        mediaQuality = e.Args[++i];
                        break;
                    case "--segments-file" when i + 1 < e.Args.Length:
                        string segPath = e.Args[++i];
                        try { segmentUrls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(segPath)); }
                        catch { segmentUrls = null; }
                        try { File.Delete(segPath); } catch { }
                        break;
                    case "--data-dir" when i + 1 < e.Args.Length:
                        dataDirectory = Path.GetFullPath(e.Args[++i]);
                        explicitDirectory = true;
                        break;
                    case "--smoke-test": smoke = true; break;
                    default: throw new ArgumentException("Usage: Flugel Download Manager [--url <http(s) URL>] [--filename <name>] [--referrer <url>] [--media-quality <label>] [--segments-file <file>] [--data-dir <directory>] [--smoke-test]");
                }
            }
            if (smoke && (!explicitDirectory || url is not null))
                throw new ArgumentException("--smoke-test requires a fresh --data-dir and cannot be combined with --url. Only controlled local data is captured.");
            if (smoke && Directory.Exists(dataDirectory) && Directory.EnumerateFileSystemEntries(dataDirectory).Any())
                throw new ArgumentException("Smoke-test directory must be empty. Choose a new --data-dir; existing files are never removed.");
            if (smoke) smokeDirectory = dataDirectory;

            if (pendingPath is not null)
            {
                // Only a request file inside this user's data directory is accepted; it is deleted after reading.
                if (!string.Equals(Path.GetDirectoryName(pendingPath), dataDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(pendingPath).StartsWith("pending-request-", StringComparison.Ordinal))
                    throw new ArgumentException("Pending request must be a request file in the data directory.");
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(pendingPath, System.Text.Encoding.UTF8));
                    var root = doc.RootElement;
                    string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
                    url = Str("Url") ?? url;
                    if (url is not null) ValidateUrl(url);
                    fileName = Str("FileName") ?? fileName;
                    referrer = Str("Referrer") ?? referrer;
                    mediaQuality = Str("MediaQuality") ?? mediaQuality;
                    cookieHeader = Str("CookieHeader");
                    if (root.TryGetProperty("SegmentUrls", out var seg) && seg.ValueKind == System.Text.Json.JsonValueKind.Array)
                        segmentUrls = seg.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
                }
                finally
                {
                    try { File.Delete(pendingPath); } catch { }
                }
            }

            var manager = new DownloadManager(dataDirectory);
            var window = new MainWindow(manager, dataDirectory, url, fileName, referrer, mediaQuality, segmentUrls, smoke, cookieHeader);
            MainWin = window;
            MainWindow = window;
            window.Show();

            // P0-A: single-instance same-user Named Pipe IPC server
            IpcServer = new SingleInstanceIpcServer(dataDirectory, HandleIpcRequestAsync);
            SetupTray(manager);
            SetupClipboardMonitor(manager);
        }
        catch (Exception ex)
        {
            if (smokeDirectory is not null)
            {
                Directory.CreateDirectory(smokeDirectory);
                File.WriteAllText(Path.Combine(smokeDirectory, "startup-failure.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    passed = false,
                    phase = "startup",
                    errorType = ex.GetType().Name,
                    innerErrorType = ex.InnerException?.GetType().Name,
                    message = SafeError(ex)
                }));
                Shutdown(1);
                return;
            }
            MessageBox.Show($"Flugel Download Manager could not open.\n\n{SafeError(ex)}\n\nIf saved data is damaged, keep a backup; do not delete it to bypass this error.",
                "Flugel Download Manager — startup problem", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private Task<IpcResponseMessage> HandleIpcRequestAsync(IpcRequestMessage req)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            var settings = MainWin?.Manager?.Settings;
            var resp = new IpcResponseMessage
            {
                Status = "accepted-for-confirmation",
                ForwardedToRunningInstance = true,
                BrowserAutoIntercept = settings?.BrowserAutoIntercept ?? true,
                BrowserVideoBalloonEnabled = settings?.BrowserVideoBalloonEnabled ?? true,
                AutoCaptureExtensions = settings?.AutoCaptureExtensions ?? "",
                ExcludedHosts = settings?.ExcludedHosts ?? ""
            };
            if (req.Action == "enqueue" || req.Action == "enqueue-media")
            {
                try
                {
                    ValidateUrl(req.Url ?? "");
                    if (!string.IsNullOrEmpty(req.Referrer)) ValidateUrl(req.Referrer);
                    if (req.SegmentUrls is not null)
                        foreach (var segment in req.SegmentUrls) ValidateUrl(segment);
                }
                catch (ArgumentException) { resp.Status = "invalid-request"; return resp; }
                bool accepted = MainWin?.TryAcceptBrowserDownload(new AddDownloadOptions
                {
                    Url = req.Url ?? "",
                    FileName = req.FileName,
                    Referrer = req.Referrer,
                    Description = req.PageTitle ?? "",
                    MediaQualityLabel = req.MediaQuality,
                    MediaSegmentUrls = req.SegmentUrls,
                    CookieHeader = req.CookieHeader,
                    StartImmediately = false
                }) == true;
                if (!accepted) resp.Status = "desktop-busy";
            }
            return resp;
        }).Task;
    }

    internal void ShowAndActivate()
    {
        if (MainWin is null) return;
        if (MainWin.WindowState == WindowState.Minimized) MainWin.WindowState = WindowState.Normal;
        MainWin.Show();
        MainWin.Activate();
        if (MainWin.Topmost) { MainWin.Topmost = false; MainWin.Topmost = true; }
    }

    internal static Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Enter an absolute HTTP or HTTPS URL without embedded username or password.");
        return uri;
    }

    internal static string SafeError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Access denied. Check destination and data-directory permissions.",
        IOException => "A file or data directory is unavailable, locked, already exists, or contains unreadable state. Check permissions, free space, and other running instances.",
        ArgumentException => "One or more values are invalid. Check the URL, filename, directory, schedule, and numeric limits.",
        _ => $"Operation failed ({ex.GetType().Name}). Check the selected transfer and try again. Saved files have not been intentionally overwritten."
    };

    private void SetupTray(DownloadManager manager)
    {
        try
        {
            var icon = System.Drawing.SystemIcons.Application;
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = "Flugel Download Manager",
                Visible = true
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Show Flugel Download Manager", null, (_, _) => ShowAndActivate());
            menu.Items.Add("Add URL…", null, (_, _) => Dispatcher.Invoke(() => MainWin?.ShowAdd((AddDownloadOptions?)null)));
            menu.Items.Add("Pause all", null, (_, _) => Dispatcher.Invoke(() => { manager.PauseAll(); MainWin?.RefreshSnapshots(); }));
            menu.Items.Add("Resume all", null, (_, _) => Dispatcher.Invoke(() => { manager.ResumeAll(); MainWin?.RefreshSnapshots(); }));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, async (_, _) => await Dispatcher.InvokeAsync(async () =>
            {
                if (MainWin is not null && await MainWin.StopAsync())
                {
                    _tray?.Dispose();
                    Shutdown();
                }
            }));
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowAndActivate();
        }
        catch { /* Tray is optional; failures must not block startup. */ }
    }

    private void SetupClipboardMonitor(DownloadManager manager)
    {
        if (!manager.Settings.MonitorClipboard) return;
        _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _clipboardTimer.Tick += (_, _) =>
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText() && !_clipboardSuppressed)
                {
                    string text = System.Windows.Clipboard.GetText().Trim();
                    if (text.Length > 0 && text != _lastClipboardText && Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                        string.IsNullOrEmpty(uri.UserInfo) && text.Length < 4096)
                    {
                        _lastClipboardText = text;
                        MainWin?.ShowAdd(new AddDownloadOptions { Url = text, StartImmediately = false });
                    }
                    else _lastClipboardText = text;
                }
            }
            catch { }
        };
        _clipboardTimer.Start();
    }

    internal void SuppressClipboardOnce()
    {
        _clipboardSuppressed = true;
        _ = Task.Delay(1500).ContinueWith(_ => _clipboardSuppressed = false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IpcServer?.DisposeAsync().AsTask().Wait(1500);
        _clipboardTimer?.Stop();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}
