using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Harbor.Core;
using Microsoft.Win32;

namespace Harbor.Desktop;

// Native controls retain their platform keyboard, automation, focus and contrast behavior.
internal abstract class FormDialog : Window
{
    protected readonly StackPanel Fields = new() { Margin = new Thickness(24) };
    protected readonly TextBlock Error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
    protected readonly Button Accept = new() { IsDefault = true, MinWidth = 100 };

    protected FormDialog(string title, string subtitle, string action)
    {
        Style = (Style)FindResource(typeof(Window));
        Title = title;
        Width = 600;
        Height = 720;
        MinWidth = 440;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var root = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 14, 16, 20) };
        Accept.Content = action;
        Accept.Style = (Style)FindResource("PrimaryButton");
        var cancel = new Button { Content = "_Cancel", IsCancel = true, MinWidth = 90 };
        buttons.Children.Add(Accept);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        Fields.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        Fields.Children.Add(new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 20), Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
        root.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
        AutomationProperties.SetLiveSetting(Error, AutomationLiveSetting.Assertive);
    }

    protected TextBox Input(string label, string value, string name)
    {
        var input = new TextBox { Text = value, Name = name, Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetName(input, label.Replace("_", ""));
        Fields.Children.Add(new Label { Content = label, Target = input });
        Fields.Children.Add(input);
        return input;
    }

    protected TextBox DirectoryInput(string value)
    {
        var input = new TextBox { Text = value, MinWidth = 160 };
        AutomationProperties.SetName(input, "Destination directory");
        Fields.Children.Add(new Label { Content = "_Destination folder", Target = input });
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var browse = new Button { Content = "_Browse…", Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) =>
        {
            try
            {
                var dialog = new OpenFolderDialog { Title = "Choose download directory", Multiselect = false };
                if (Directory.Exists(input.Text)) dialog.InitialDirectory = input.Text;
                if (dialog.ShowDialog(this) == true) input.Text = dialog.FolderName;
            }
            catch (Exception ex) { Error.Text = App.SafeError(ex); }
        };
        row.Children.Add(browse);
        row.Children.Add(input);
        Fields.Children.Add(row);
        return input;
    }

    protected static int Number(TextBox input, int min, int max, string name)
    {
        if (!int.TryParse(input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
        {
            input.Focus();
            input.SelectAll();
            throw new FormException($"{name} must be a whole number from {min} to {max}.");
        }
        return value;
    }

    protected static string DirectoryValue(TextBox input)
    {
        if (string.IsNullOrWhiteSpace(input.Text) || !Path.IsPathFullyQualified(input.Text.Trim()))
        {
            input.Focus();
            throw new FormException("Choose an absolute destination folder path.");
        }
        return Path.GetFullPath(input.Text.Trim());
    }

    protected void Problem(Exception ex) => Error.Text = ex is FormException ? ex.Message : App.SafeError(ex);
    internal void ConfirmForSmoke() => Accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    protected sealed class FormException(string message) : Exception(message);
}

internal sealed class AddDialog : FormDialog
{
    private readonly TextBox _url, _directory, _fileName, _segments, _time;
    private readonly CheckBox _schedule;
    private readonly DatePicker _date;
    internal string? AddedId { get; private set; }

    internal AddDialog(DownloadManager manager, string? url) : base("Add a download", "Review the source and destination. Nothing starts until you choose Add download.", "_Add download")
    {
        _url = Input("_URL · HTTP or HTTPS", url ?? "", "SourceUrl");
        _directory = DirectoryInput(manager.Settings.DownloadDirectory);
        _fileName = Input("File _name · optional", "", "FileName");
        _segments = Input("_Segments · 1–16", manager.Settings.Segments.ToString(CultureInfo.InvariantCulture), "Segments");
        _schedule = new CheckBox { Content = "Schedule for _later (local time)", Margin = new Thickness(0, 0, 0, 10), MinHeight = 30 };
        Fields.Children.Add(_schedule);
        _date = new DatePicker { SelectedDate = DateTime.Today, IsEnabled = false, MinHeight = 34 };
        _time = new TextBox { Text = DateTime.Now.AddHours(1).ToString("HH:mm", CultureInfo.InvariantCulture), Width = 90, IsEnabled = false, Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetName(_date, "Scheduled local date");
        AutomationProperties.SetName(_time, "Scheduled local time, 24-hour HH:mm");
        var scheduleRow = new StackPanel { Orientation = Orientation.Horizontal };
        scheduleRow.Children.Add(_date);
        scheduleRow.Children.Add(_time);
        Fields.Children.Add(scheduleRow);
        Fields.Children.Add(new TextBlock { Text = "24-hour time · HH:mm. Your computer must remain on with StreamHarbor running.", FontSize = 12, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) });
        Fields.Children.Add(Error);
        _schedule.Checked += (_, _) => _date.IsEnabled = _time.IsEnabled = true;
        _schedule.Unchecked += (_, _) => _date.IsEnabled = _time.IsEnabled = false;
        Loaded += (_, _) => { _url.Focus(); if (url is not null) _url.SelectAll(); };
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
                    if (_date.SelectedDate is not { } day || !TimeOnly.TryParseExact(_time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                        throw new FormException("Choose a date and enter local time as HH:mm (for example, 18:30).");
                    var local = DateTime.SpecifyKind(day.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
                    if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
                        throw new FormException("This local time is skipped or repeated by daylight-saving changes. Choose an unambiguous time.");
                    scheduledAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                    if (scheduledAt <= DateTimeOffset.Now) throw new FormException("Schedule must be in the future.");
                }
                AddedId = manager.Add(source, directory, string.IsNullOrWhiteSpace(_fileName.Text) ? null : _fileName.Text.Trim(), scheduledAt, segments);
                DialogResult = true;
            }
            catch (Exception ex) { Problem(ex); }
        };
    }
}

internal sealed class SettingsDialog : FormDialog
{
    internal SettingsDialog(DownloadManager manager) : base("Download settings", "Defaults apply to new downloads. The engine owns queue scheduling and validates all changes.", "_Save settings")
    {
        Height = 600;
        var directory = DirectoryInput(manager.Settings.DownloadDirectory);
        var concurrent = Input("_Concurrent downloads · 1–16", manager.Settings.MaxConcurrent.ToString(CultureInfo.InvariantCulture), "Concurrency");
        var segments = Input("_Segments per download · 1–16", manager.Settings.Segments.ToString(CultureInfo.InvariantCulture), "DefaultSegments");
        var retries = Input("_Retry limit · 0–10", manager.Settings.MaxRetries.ToString(CultureInfo.InvariantCulture), "Retries");
        Fields.Children.Add(Error);
        Loaded += (_, _) => directory.Focus();
        Accept.Click += (_, _) =>
        {
            try
            {
                string newDirectory = DirectoryValue(directory);
                int newConcurrent = Number(concurrent, 1, 16, "Concurrent downloads");
                int newSegments = Number(segments, 1, 16, "Segments");
                int newRetries = Number(retries, 0, 10, "Retry limit");
                var settings = manager.Settings;
                var oldDirectory = settings.DownloadDirectory;
                int oldConcurrent = settings.MaxConcurrent, oldSegments = settings.Segments, oldRetries = settings.MaxRetries;
                try
                {
                    settings.DownloadDirectory = newDirectory;
                    settings.MaxConcurrent = newConcurrent;
                    settings.Segments = newSegments;
                    settings.MaxRetries = newRetries;
                    manager.SaveSettings();
                }
                catch
                {
                    settings.DownloadDirectory = oldDirectory;
                    settings.MaxConcurrent = oldConcurrent;
                    settings.Segments = oldSegments;
                    settings.MaxRetries = oldRetries;
                    throw;
                }
                DialogResult = true;
            }
            catch (Exception ex) { Problem(ex); }
        };
    }
}
