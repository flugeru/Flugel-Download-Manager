using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Flugel.Core;

namespace Flugel.Desktop;

// Add flow: source, destination, classification, scheduling and optional request context.
internal sealed class AddDialog : FormDialog
{
    private readonly DownloadManager _manager;
    private readonly TextBox _url, _directory, _fileName, _segments, _description, _referrer, _authUser, _authPassword, _time;
    private readonly ComboBox _category, _queue;
    private readonly CheckBox _schedule, _useCategoryFolder, _resolveName;
    private readonly RadioButton _startNow, _startLater;
    private readonly DatePicker _date;
    private readonly TextBlock _probe;
    private readonly TextBlock? _mediaNote;

    internal string? AddedId { get; private set; }
    internal bool StartedNow { get; private set; }

    internal AddDialog(DownloadManager manager, string? url) : this(manager, url is null ? null : new AddDownloadOptions { Url = url }) { }

    internal AddDialog(DownloadManager manager, AddDownloadOptions? options) : base("Add a download", "Review the source and destination. Nothing starts until you choose Add download.", "_Add download", 680, 860)
    {
        _manager = manager;
        options ??= new AddDownloadOptions();
        var settings = manager.Settings;

        _url = Input("_URL · HTTP or HTTPS", options.Url, "SourceUrl");
        _directory = DirectoryInput(string.IsNullOrWhiteSpace(options.DestinationDirectory) ? settings.DownloadDirectory : options.DestinationDirectory);
        _fileName = Input("File _name · optional (server name is used when empty)", options.FileName ?? "", "FileName");

        _category = Choice("_Category", settings.Categories.Select(c => c.Name), options.Category ?? DownloadManager.ClassifyFileCategory(options.FileName ?? "", settings.Categories), "CategoryChoice");
        _queue = Choice("_Queue", settings.Queues.Select(q => q.Name), options.QueueName, "QueueChoice");
        _useCategoryFolder = Check("Store in a category subfolder (uses Save To settings)", options.UseCategorySubfolder || settings.UseCategorySubfolders, "UseCategoryFolder");
        _resolveName = Check("Ask the server for the final file name (Content-Disposition)", options.ResolveServerFileName, "ResolveServerName");
        _description = Input("_Description · optional", options.Description, "Description");
        _referrer = Input("_Referrer · optional page URL", options.Referrer ?? "", "Referrer");

        Fields.Children.Add(new Label { Content = "_HTTP authentication · optional" });
        var authRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        _authUser = new TextBox { Name = "AuthUser", MinWidth = 140 };
        _authPassword = new TextBox { Name = "AuthPassword", MinWidth = 140, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(_authUser, "HTTP authentication user name");
        AutomationProperties.SetName(_authPassword, "HTTP authentication password");
        authRow.Children.Add(_authPassword);
        authRow.Children.Add(_authUser);
        Fields.Children.Add(authRow);
        Fields.Children.Add(Note("Sent only to the download server as HTTP Basic authentication over HTTPS. Never stored in history exports."));

        _segments = Input("_Segments · 1–16", (options.Segments ?? settings.Segments).ToString(CultureInfo.InvariantCulture), "Segments");
        _time = new TextBox { Text = DateTime.Now.AddHours(1).ToString("HH:mm", CultureInfo.InvariantCulture), Width = 90, IsEnabled = false, Margin = new Thickness(12, 0, 0, 0), Name = "ScheduleTime" };
        _schedule = new CheckBox { Content = "Schedule for _later (local time)", Margin = new Thickness(0, 0, 0, 10), MinHeight = 30, Name = "ScheduleLater" };
        Fields.Children.Add(_schedule);
        _date = new DatePicker { SelectedDate = DateTime.Today, IsEnabled = false, MinHeight = 34 };
        AutomationProperties.SetName(_date, "Scheduled local date");
        AutomationProperties.SetName(_time, "Scheduled local time, 24-hour HH:mm");
        var scheduleRow = new StackPanel { Orientation = Orientation.Horizontal };
        scheduleRow.Children.Add(_date);
        scheduleRow.Children.Add(_time);
        Fields.Children.Add(scheduleRow);
        Fields.Children.Add(Note("24-hour time · HH:mm. Your computer must remain on with Flugel Download Manager running."));
        _schedule.Checked += (_, _) => _date.IsEnabled = _time.IsEnabled = true;
        _schedule.Unchecked += (_, _) => _date.IsEnabled = _time.IsEnabled = false;

        var startGroup = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
        _startNow = new RadioButton { Content = "Start _now", IsChecked = options.StartImmediately && options.ScheduledAt is null, GroupName = "StartMode", MinHeight = 28, Margin = new Thickness(0, 0, 16, 0) };
        _startLater = new RadioButton { Content = "Download _later (keep queued)", IsChecked = !options.StartImmediately || options.ScheduledAt is not null, GroupName = "StartMode", MinHeight = 28 };
        startGroup.Children.Add(_startNow);
        startGroup.Children.Add(_startLater);
        Fields.Children.Add(new Label { Content = "_When" });
        Fields.Children.Add(startGroup);

        if (options.MediaQualityLabel is { Length: > 0 } || options.MediaSegmentUrls is { Count: > 0 })
        {
            _mediaNote = Note($"Media captured from the browser: {options.MediaQualityLabel ?? "stream"}" +
                (options.MediaSegmentUrls is { Count: > 0 } seg ? $" · {seg.Count} playlist segment(s) will be merged into one file." : ""));
            _mediaNote.FontWeight = FontWeights.SemiBold;
            Fields.Children.Add(_mediaNote);
        }

        if (options.CookieHeader is not null)
            Fields.Children.Add(Note("Browser session: cookies for this site are sent only to this transfer. They are kept in memory, never saved in history or logs."));

        // Probe: separate button, never blocks or auto-submits the dialog.
        var probeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
        var probeButton = new Button { Content = "_Probe URL", Margin = new Thickness(0, 0, 12, 0) };
        _probe = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(probeButton, "Probe URL for size and file name");
        probeRow.Children.Add(probeButton);
        probeRow.Children.Add(_probe);
        Fields.Children.Add(probeRow);
        probeButton.Click += async (_, _) =>
        {
            probeButton.IsEnabled = false;
            _probe.Text = "Probing…";
            try
            {
                var url = _url.Text.Trim();
                try { App.ValidateUrl(url); }
                catch (ArgumentException) { throw new FormException("Enter an absolute HTTP or HTTPS URL without username or password before probing."); }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var result = await _manager.ProbeUrlAsync(url, timeout.Token);
                if (!result.Success) throw new FormException($"Probe failed: {result.ErrorMessage}");
                _probe.Text = $"{result.SuggestedFileName} · {DownloadSnapshot.FormatSize(result.ContentLength ?? 0)} · {result.ContentType ?? "unknown type"} · " +
                    $"{(result.AcceptsRanges ? "resumable" : "single connection")}" +
                    (result.IsHlsManifest ? " · HLS playlist" : result.IsDashManifest ? " · DASH manifest" : "") +
                    $" · suggested category: {result.SuggestedCategory}";
                if (string.IsNullOrWhiteSpace(_fileName.Text)) _fileName.Text = result.SuggestedFileName;
                if (_category.Items.Contains(result.SuggestedCategory)) _category.SelectedItem = result.SuggestedCategory;
            }
            catch (OperationCanceledException) { _probe.Text = "Probe timed out. The server did not answer within 15 s."; }
            catch (Exception ex) { _probe.Text = ex is FormException ? ex.Message : App.SafeError(ex); }
            finally { probeButton.IsEnabled = true; }
        };

        Fields.Children.Add(Error);
        Loaded += (_, _) => { _url.Focus(); if (options.Url is not null) _url.SelectAll(); };
        Accept.Click += (_, _) =>
        {
            try
            {
                var source = _url.Text.Trim();
                try { App.ValidateUrl(source); }
                catch (ArgumentException) { _url.Focus(); throw new FormException("Enter an absolute HTTP or HTTPS URL without username or password."); }
                string directory = DirectoryValue(_directory);
                int segments = Number(_segments, 1, 16, "Segments");
                DateTimeOffset? scheduledAt = null;
                if (_schedule.IsChecked == true)
                {
                    if (_date.SelectedDate is not { } day || !TimeOnly.TryParseExact(_time.Text.Trim(), "HH\\:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                        throw new FormException("Choose a date and enter local time as HH:mm (for example, 18:30).");
                    var local = DateTime.SpecifyKind(day.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
                    if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
                        throw new FormException("This local time is skipped or repeated by daylight-saving changes. Choose an unambiguous time.");
                    scheduledAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                    if (scheduledAt <= DateTimeOffset.Now) throw new FormException("Schedule must be in the future.");
                }
                string? user = string.IsNullOrWhiteSpace(_authUser.Text) ? null : _authUser.Text.Trim();
                string? password = string.IsNullOrWhiteSpace(_authPassword.Text) ? null : _authPassword.Text;
                if (password is not null && user is null) throw new FormException("Enter a user name together with the password, or clear both.");
                string? referrer = string.IsNullOrWhiteSpace(_referrer.Text) ? null : _referrer.Text.Trim();
                if (referrer is not null)
                {
                    try { App.ValidateUrl(referrer); }
                    catch (ArgumentException) { _referrer.Focus(); throw new FormException("Referrer must be an absolute HTTP or HTTPS URL without credentials."); }
                }
                if (_queue.SelectedItem is not string queue || queue.Length == 0) throw new FormException("Choose a queue.");
                if (_category.SelectedItem is not string category || category.Length == 0) throw new FormException("Choose a category.");

                AddedId = _manager.AddWithOptions(new AddDownloadOptions
                {
                    Url = source,
                    DestinationDirectory = directory,
                    FileName = string.IsNullOrWhiteSpace(_fileName.Text) ? null : _fileName.Text.Trim(),
                    Category = category,
                    QueueName = queue,
                    Description = _description.Text.Trim(),
                    Referrer = referrer,
                    BasicAuthUser = user,
                    BasicAuthPassword = password,
                    ScheduledAt = scheduledAt,
                    Segments = segments,
                    StartImmediately = scheduledAt is null && _startNow.IsChecked == true,
                    UseCategorySubfolder = _useCategoryFolder.IsChecked == true,
                    ResolveServerFileName = _resolveName.IsChecked == true,
                    MediaQualityLabel = options.MediaQualityLabel,
                    MediaSegmentUrls = options.MediaSegmentUrls,
                    CookieHeader = options.CookieHeader,
                    UserAgent = options.UserAgent
                });
                StartedNow = scheduledAt is null && _startNow.IsChecked == true;
                CloseOk();
            }
            catch (Exception ex) { Problem(ex); }
        };
    }
}
