using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Flugel.Core;

namespace Flugel.Desktop;

internal static class DesktopSmoke
{
    internal static async Task RunAsync(MainWindow window, DownloadManager manager, string dataDirectory)
    {
        var checks = new List<object>();
        int exitCode = 1;
        string evidenceDirectory = Path.Combine(dataDirectory, "ui-evidence");
        Directory.CreateDirectory(evidenceDirectory);
        await using var server = new LocalServer();
        try
        {
            void Check(string name, bool passed)
            {
                checks.Add(new { name, passed });
                if (!passed) throw new InvalidOperationException("UI check failed: " + name);
            }

            Check("fresh isolated manager", manager.GetSnapshots().Count == 0);
            Check("filters distinguish active scheduled paused completed failed",
                MainWindow.Matches(DownloadState.Downloading, "Active") && !MainWindow.Matches(DownloadState.Paused, "Active") &&
                MainWindow.Matches(DownloadState.Scheduled, "Queued") && !MainWindow.Matches(DownloadState.Completed, "Queued") &&
                MainWindow.Matches(DownloadState.Completed, "Completed") && MainWindow.Matches(DownloadState.Failed, "Failed"));
            Check("source display hides query credentials fragment", MainWindow.DisplaySource("https://user:secret@example.test/file?token=private#secret") == "https://example.test/file");
            bool rejected = false;
            try { App.ValidateUrl("https://user:secret@example.test/file"); } catch (ArgumentException) { rejected = true; }
            Check("URL input rejects credentials", rejected);

            string downloads = Path.Combine(dataDirectory, "downloads");
            manager.Settings.DownloadDirectory = downloads;
            manager.Settings.MaxRetries = 0;
            manager.SaveSettings();
            var add = new AddDialog(manager, server.Url + "sample.bin") { Owner = window };
            bool addRejected17 = false;
            add.Loaded += (_, _) => add.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                Capture(add, Path.Combine(evidenceDirectory, "add-dialog.png"));
                var segments = FindTextBox(add, "Segments");
                segments.Text = "17";
                add.ConfirmForSmoke();
                addRejected17 = add.AddedId is null && manager.GetSnapshots().Count == 0;
                segments.Text = "16";
                add.ConfirmForSmoke();
                // Validation failure must not leave a modal test window waiting forever.
                if (add.AddedId is null) add.Close();
            }));
            add.ShowDialog();
            Check("Add rejects 17 segments and accepts 16", addRejected17 && manager.GetSnapshots().Any(item => item.Id == add.AddedId && item.Segments == 16));
            Check("prefilled Add dialog confirms production manager Add", add.AddedId is not null);
            string id = add.AddedId!;
            await WaitForAsync(() => manager.GetSnapshots().Any(item => item.Id == id && item.State is DownloadState.Completed or DownloadState.Failed));
            var completed = manager.GetSnapshots().Single(item => item.Id == id);
            Check("controlled local transfer completes", completed.State == DownloadState.Completed);
            string file = Path.Combine(downloads, completed.FileName);
            Check("completed local file exact SHA-256", File.Exists(file) && SHA256.HashData(await File.ReadAllBytesAsync(file)).SequenceEqual(SHA256.HashData(LocalServer.Payload)));

            string scheduledId = manager.Add(server.Url + "scheduled.bin", downloads, scheduledAt: DateTimeOffset.Now.AddHours(2));
            string pausedId = manager.Add(server.Url + "paused.bin", downloads, scheduledAt: DateTimeOffset.Now.AddHours(3));
            manager.Pause(pausedId);
            string failedId = manager.Add(server.Url + "missing.bin", downloads);
            await WaitForAsync(() => manager.GetSnapshots().Any(item => item.Id == failedId && item.State == DownloadState.Failed));
            window.RefreshSnapshots();
            Check("table displays controlled states", window.DownloadTable.Items.Count == 4);
            window.DownloadTable.SelectedItem = window.DownloadTable.Items.Cast<DownloadRow>().Single(row => row.Value.Id == failedId);
            window.DownloadTable.Focus();
            window.UpdateLayout();
            Check("native download table accepts keyboard focus", window.DownloadTable.IsKeyboardFocusWithin);
            Check("all requested table columns present", window.DownloadTable.Columns.Count == 9);
            Capture(window, Path.Combine(evidenceDirectory, "desktop-wide.png"));

            window.Width = 1000;
            window.Height = 650;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            Check("compact layout retains usable table viewport", window.DownloadTable.ActualWidth >= 650 && window.DownloadTable.ActualHeight >= 110);
            Capture(window, Path.Combine(evidenceDirectory, "desktop-compact.png"));

            var settings = new SettingsDialog(manager) { Owner = window };
            bool settingsRejected17 = false, settingsAccepted16 = false;
            settings.Loaded += (_, _) => settings.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                Capture(settings, Path.Combine(evidenceDirectory, "settings-dialog.png"));
                var segments = FindTextBox(settings, "DefaultSegments");
                int previousSegments = manager.Settings.Segments;
                segments.Text = "17";
                settings.ConfirmForSmoke();
                settingsRejected17 = settings.IsVisible && manager.Settings.Segments == previousSegments;
                segments.Text = "16";
                settings.ConfirmForSmoke();
                settingsAccepted16 = !settings.IsVisible && manager.Settings.Segments == 16;
                if (settings.IsVisible) settings.Close();
            }));
            settings.ShowDialog();
            Check("Settings rejects 17 segments and accepts 16", settingsRejected17 && settingsAccepted16);
            Check("settings native dialog opens and closes", !settings.IsVisible);
            Check("scheduled transfer remains scheduled", manager.GetSnapshots().Single(item => item.Id == scheduledId).State == DownloadState.Scheduled);
            var browserReply = SingleInstanceIpc.TrySendAsync(dataDirectory, new IpcRequestMessage
            {
                Action = "enqueue-media", Url = server.Url + "browser.mp4", FileName = "browser.mp4", MediaQuality = "1080p"
            }, 4000);
            // The dialog deliberately remains open until the pipe acknowledgment arrives.
            bool browserDialogClosed = false, acknowledgedWhileOpen = false, busyRejected = false;
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
            {
                while (true)
                {
                    var browserDialog = Application.Current.Windows.OfType<AddDialog>().FirstOrDefault();
                    if (browserDialog is not null)
                    {
                        try
                        {
                            var reply = await browserReply.WaitAsync(TimeSpan.FromSeconds(5));
                            acknowledgedWhileOpen = reply?.Status == "accepted-for-confirmation" && browserDialog.IsVisible;
                            var busy = await SingleInstanceIpc.TrySendAsync(dataDirectory, new IpcRequestMessage
                            {
                                Action = "enqueue-media", Url = server.Url + "second.mp4"
                            }, 4000);
                            busyRejected = busy?.Status == "desktop-busy";
                        }
                        catch (TimeoutException) { }
                        finally { browserDialog.Close(); browserDialogClosed = true; }
                        break;
                    }
                    await Task.Delay(50);
                }
            }));
            Check("browser IPC request receives acknowledgment", (await browserReply)?.Status == "accepted-for-confirmation");
            await WaitForAsync(() => browserDialogClosed);
            Check("browser IPC acknowledges before confirmation closes", acknowledgedWhileOpen);
            Check("busy desktop rejects instead of silently dropping capture", busyRejected);
            Check("orderly asynchronous close", await window.StopAsync());
            exitCode = 0;
        }
        catch (Exception ex)
        {
            checks.Add(new { name = "smoke failure", passed = false, errorType = ex.GetType().Name, controlledTestError = ex.Message, stackTrace = ex.StackTrace });
            await window.StopAsync();
        }
        finally
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var evidence = new
            {
                passed = exitCode == 0,
                capturedAtUtc = DateTimeOffset.UtcNow,
                scope = "Real WPF dispatcher/layout, native controls, isolated production manager, loopback-only HTTP. No simulated DPI coverage or external/user content.",
                dpi = new { x = dpi.PixelsPerInchX, y = dpi.PixelsPerInchY },
                checks,
                screenshots = Directory.GetFiles(evidenceDirectory, "*.png").Select(Path.GetFileName).ToArray()
            };
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(exitCode);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) await Task.Delay(100, timeout.Token);
    }

    private static TextBox FindTextBox(DependencyObject root, string name)
    {
        if (root is TextBox input && input.Name == name) return input;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject node)
            {
                try { return FindTextBox(node, name); }
                catch (KeyNotFoundException) { }
            }
        throw new KeyNotFoundException(name);
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        // Capture at the real display DPI so 125%/150% scaling evidence is not downscaled (UI-01).
        var dpi = VisualTreeHelper.GetDpi(window);
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        png.Save(output);
    }

    // ponytail: controlled smoke server is sequential HTTP only; transport conformance belongs in Flugel.Checks.
    private sealed class LocalServer : IAsyncDisposable
    {
        internal static readonly byte[] Payload = Enumerable.Range(0, 512 * 1024).Select(i => (byte)(i % 251)).ToArray();
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        internal string Url { get; }

        internal LocalServer()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _serve = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    string request = await reader.ReadLineAsync(_stop.Token) ?? "";
                    string? header;
                    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(_stop.Token))) { }
                    bool missing = request.Contains("/missing.bin", StringComparison.Ordinal);
                    byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {(missing ? "404 Not Found" : "200 OK")}\r\nContent-Length: {(missing ? 0 : Payload.Length)}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, _stop.Token);
                    if (!missing && !request.StartsWith("HEAD ", StringComparison.Ordinal))
                        await stream.WriteAsync(Payload, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _serve;
            _stop.Dispose();
        }
    }
}
