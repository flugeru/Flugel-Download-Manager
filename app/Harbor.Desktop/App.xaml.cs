using System.IO;
using System.Windows;
using Harbor.Core;

namespace Harbor.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? smokeDirectory = null;
        try
        {
            string? url = null;
            string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamHarbor");
            bool explicitDirectory = false, smoke = false;
            for (int i = 0; i < e.Args.Length; i++)
            {
                switch (e.Args[i])
                {
                    case "--url" when i + 1 < e.Args.Length:
                        url = e.Args[++i];
                        ValidateUrl(url);
                        break;
                    case "--data-dir" when i + 1 < e.Args.Length:
                        dataDirectory = Path.GetFullPath(e.Args[++i]);
                        explicitDirectory = true;
                        break;
                    case "--smoke-test": smoke = true; break;
                    default: throw new ArgumentException("Usage: StreamHarbor [--url <http(s) URL>] [--data-dir <directory>] [--smoke-test]");
                }
            }
            if (smoke && (!explicitDirectory || url is not null))
                throw new ArgumentException("--smoke-test requires a fresh --data-dir and cannot be combined with --url. Only controlled local data is captured.");
            if (smoke && Directory.Exists(dataDirectory) && Directory.EnumerateFileSystemEntries(dataDirectory).Any())
                throw new ArgumentException("Smoke-test directory must be empty. Choose a new --data-dir; existing files are never removed.");
            if (smoke) smokeDirectory = dataDirectory;

            // Core owns the exclusive data-directory lock; do not create a competing UI lock.
            var manager = new DownloadManager(dataDirectory);
            var window = new MainWindow(manager, dataDirectory, url, smoke);
            MainWindow = window;
            window.Show();
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
            MessageBox.Show($"StreamHarbor could not open.\n\n{SafeError(ex)}\n\nIf another StreamHarbor window uses this data directory, close it first. Browser links are not forwarded to an existing window: copy the link and use Add there, or launch again after closing it. For isolated use, pass --data-dir <new directory>.\n\nIf saved data is damaged, keep a backup; do not delete it to bypass this error.",
                "StreamHarbor — startup problem", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal static Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Enter an absolute HTTP or HTTPS URL without embedded username or password.");
        return uri;
    }

    // Error messages may contain source URLs. Do not echo queries, credentials, or arbitrary engine text.
    internal static string SafeError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Access denied. Check destination and data-directory permissions.",
        IOException => "A file or data directory is unavailable, locked, already exists, or contains unreadable state. Check permissions, free space, and other running instances.",
        ArgumentException => "One or more values are invalid. Check the URL, filename, directory, schedule, and numeric limits.",
        _ => $"Operation failed ({ex.GetType().Name}). Check the selected transfer and try again. Saved files have not been intentionally overwritten."
    };
}
