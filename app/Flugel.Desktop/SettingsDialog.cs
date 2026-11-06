using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Flugel.Core;
using Microsoft.Win32;

namespace Flugel.Desktop;

// Tabbed options mirroring the observed IDM layout: General, Categories, Save to,
// Downloads (speed/retries), Connection (proxy) and Browser integration.
internal sealed class SettingsDialog : FormDialog
{
    private readonly DownloadManager _manager;
    private readonly TextBox _directory, _concurrent, _segments, _retries;
    private readonly TextBox _globalLimit, _autoCapture, _excludedHosts, _proxyAddress, _proxyUser;
    private readonly CheckBox _speedLimiter, _showCompletion, _startQueue, _minimizeTray, _clipboard, _autoIntercept, _balloon, _categoryFolders;
    private readonly ComboBox _proxyMode;
    private readonly ObservableCollection<CategoryRule> _categories;

    internal SettingsDialog(DownloadManager manager) : base("Download settings", "Defaults apply to new downloads. The engine owns queue scheduling and validates all changes.", "_Save settings", 720, 760)
    {
        _manager = manager;
        var s = manager.Settings;

        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 6) };

        // ---- General (first visible tab hosts the smoke-validated segment field) ----
        var general = new StackPanel { Margin = new Thickness(10, 12, 10, 12) };
        _directory = DirectoryInputIn(general, s.DownloadDirectory);
        _concurrent = LabeledInput(general, "_Concurrent downloads · 1–16", s.MaxConcurrent.ToString(CultureInfo.InvariantCulture), "Concurrency");
        _segments = LabeledInput(general, "_Segments per download · 1–16", s.Segments.ToString(CultureInfo.InvariantCulture), "DefaultSegments");
        _retries = LabeledInput(general, "_Retry limit · 0–10", s.MaxRetries.ToString(CultureInfo.InvariantCulture), "Retries");
        tabs.Items.Add(new TabItem { Header = "General", Content = general });

        // ---- Categories ----
        var categoriesPanel = new DockPanel { Margin = new Thickness(10, 12, 10, 12) };
        var catButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var addCategory = new Button { Content = "_Add category", Margin = new Thickness(0, 0, 8, 0) };
        var removeCategory = new Button { Content = "_Remove selected" };
        catButtons.Children.Add(addCategory);
        catButtons.Children.Add(removeCategory);
        DockPanel.SetDock(catButtons, Dock.Bottom);
        categoriesPanel.Children.Add(catButtons);
        categoriesPanel.Children.Add(new TextBlock
        {
            Text = "Extensions are space-separated file extensions without a dot. The first matching rule assigns category and subfolder.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource("Muted"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        _categories = new ObservableCollection<CategoryRule>(s.Categories.Select(c => new CategoryRule { Name = c.Name, Extensions = c.Extensions, Subfolder = c.Subfolder }));
        var grid = new DataGrid
        {
            ItemsSource = new ListCollectionView(_categories),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            MinHeight = 200
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(CategoryRule.Name)), Width = new DataGridLength(110) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Extensions", Binding = new Binding(nameof(CategoryRule.Extensions)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Subfolder", Binding = new Binding(nameof(CategoryRule.Subfolder)), Width = new DataGridLength(110) });
        categoriesPanel.Children.Add(grid);
        addCategory.Click += (_, _) => _categories.Add(new CategoryRule { Name = "New category", Extensions = "ext", Subfolder = "New category" });
        removeCategory.Click += (_, _) =>
        {
            if (grid.SelectedItem is CategoryRule rule && !string.Equals(rule.Name, "General", StringComparison.OrdinalIgnoreCase))
                _categories.Remove(rule);
            else
                Error.Text = "The General category cannot be removed.";
        };
        tabs.Items.Add(new TabItem { Header = "Categories", Content = categoriesPanel });

        // ---- Save to ----
        var saveTo = new StackPanel { Margin = new Thickness(10, 12, 10, 12) };
        _categoryFolders = AddCheckbox(saveTo, "Store files in a category subfolder under the download directory", s.UseCategorySubfolders, "CategorySubfolders");
        saveTo.Children.Add(Note("Use Categories → Subfolder to control the per-category folder name. Existing files are never moved."));
        tabs.Items.Add(new TabItem { Header = "Save to", Content = saveTo });

        // ---- Downloads ----
        var downloads = new StackPanel { Margin = new Thickness(10, 12, 10, 12) };
        _speedLimiter = AddCheckbox(downloads, "Limit total download speed", s.SpeedLimiterEnabled, "SpeedLimiter");
        var limitLabel = new Label { Content = "_Global speed limit · KiB/s (16–10 000 000)" };
        _globalLimit = new TextBox
        {
            Name = "GlobalLimit",
            Text = Math.Max(16, s.MaxGlobalBytesPerSecond / 1024).ToString(CultureInfo.InvariantCulture),
            MinHeight = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14)
        };
        AutomationProperties.SetName(_globalLimit, "Global speed limit in KiB per second");
        limitLabel.Target = _globalLimit;
        downloads.Children.Add(limitLabel);
        downloads.Children.Add(_globalLimit);
        _showCompletion = AddCheckbox(downloads, "Show a dialog when a download finishes", s.ShowCompletionDialog, "ShowCompletion");
        _startQueue = AddCheckbox(downloads, "Start the Main queue automatically", s.StartQueueImmediately, "StartQueueImmediately");
        _minimizeTray = AddCheckbox(downloads, "Minimize to the notification area instead of closing", s.MinimizeToTray, "MinimizeToTray");
        _clipboard = AddCheckbox(downloads, "Watch the clipboard for copied URLs (asks before adding)", s.MonitorClipboard, "MonitorClipboard");
        tabs.Items.Add(new TabItem { Header = "Downloads", Content = downloads });

        // ---- Connection ----
        var connection = new StackPanel { Margin = new Thickness(10, 12, 10, 12) };
        _proxyMode = new ComboBox { Margin = new Thickness(0, 0, 0, 14), MinHeight = 30 };
        foreach (string mode in new[] { "None", "System", "Manual" }) _proxyMode.Items.Add(mode);
        _proxyMode.SelectedItem = s.ProxyMode is "None" or "Manual" ? s.ProxyMode : "System";
        connection.Children.Add(new Label { Content = "_Proxy" });
        connection.Children.Add(_proxyMode);
        var proxyAddressLabel = new Label { Content = "_Proxy address · host:port (manual mode)" };
        _proxyAddress = new TextBox { Name = "ProxyAddress", Text = s.ProxyAddress ?? "", MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetName(_proxyAddress, "Proxy address");
        proxyAddressLabel.Target = _proxyAddress;
        connection.Children.Add(proxyAddressLabel);
        connection.Children.Add(_proxyAddress);
        var proxyUserLabel = new Label { Content = "_Proxy user name · optional" };
        _proxyUser = new TextBox { Name = "ProxyUser", Text = s.ProxyUsername ?? "", MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetName(_proxyUser, "Proxy user name");
        proxyUserLabel.Target = _proxyUser;
        connection.Children.Add(proxyUserLabel);
        connection.Children.Add(_proxyUser);
        connection.Children.Add(Note("The proxy password is kept in the Windows credential store, not in settings files; leave blank to connect without credentials."));
        tabs.Items.Add(new TabItem { Header = "Connection", Content = connection });

        // ---- Browser ----
        var browser = new StackPanel { Margin = new Thickness(10, 12, 10, 12) };
        _autoIntercept = AddCheckbox(browser, "Automatically offer Flugel Download Manager for matching browser downloads (opt-in)", s.BrowserAutoIntercept, "BrowserAutoIntercept");
        _balloon = AddCheckbox(browser, "Show the “Download this video” balloon on video pages", s.BrowserVideoBalloonEnabled, "VideoBalloon");
        var captureLabel = new Label { Content = "_Auto-capture file extensions · space separated" };
        _autoCapture = new TextBox
        {
            Name = "AutoCaptureExtensions",
            Text = s.AutoCaptureExtensions,
            AcceptsReturn = true,
            Height = 74,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 14)
        };
        AutomationProperties.SetName(_autoCapture, "Auto capture file extensions");
        captureLabel.Target = _autoCapture;
        browser.Children.Add(captureLabel);
        browser.Children.Add(_autoCapture);
        var hostsLabel = new Label { Content = "_Never intercept hosts · space or comma separated" };
        _excludedHosts = new TextBox { Name = "ExcludedHosts", Text = s.ExcludedHosts, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetName(_excludedHosts, "Never intercept hosts");
        hostsLabel.Target = _excludedHosts;
        browser.Children.Add(hostsLabel);
        browser.Children.Add(_excludedHosts);
        browser.Children.Add(Note("When interception fails or is disabled, the browser completes the download itself — nothing is cancelled or discarded."));
        tabs.Items.Add(new TabItem { Header = "Browser", Content = browser });

        Fields.Children.Add(tabs);
        Fields.Children.Add(Error);
        Loaded += (_, _) => _directory.Focus();
        Accept.Click += (_, _) =>
        {
            try
            {
                string newDirectory = DirectoryValue(_directory);
                int newConcurrent = Number(_concurrent, 1, 16, "Concurrent downloads");
                int newSegments = Number(_segments, 1, 16, "Segments");
                int newRetries = Number(_retries, 0, 10, "Retry limit");
                long newLimit = Number(_globalLimit, 16, 10_000_000, "Global speed limit") * 1024L;
                string mode = _proxyMode.SelectedItem as string ?? "System";
                string? proxyAddress = string.IsNullOrWhiteSpace(_proxyAddress.Text) ? null : _proxyAddress.Text.Trim();
                if (mode == "Manual" && (proxyAddress is null || !proxyAddress.Contains(':')))
                    throw new FormException("Manual proxy requires an address like host:port.");
                if (_categories.Count == 0 || _categories.All(c => !string.Equals(c.Name, "General", StringComparison.OrdinalIgnoreCase)))
                    throw new FormException("Keep at least one category named “General”.");
                if (_categories.Select(c => c.Name.Trim().ToLowerInvariant()).Distinct().Count() != _categories.Count)
                    throw new FormException("Category names must be unique.");
                foreach (var rule in _categories)
                {
                    if (string.IsNullOrWhiteSpace(rule.Name)) throw new FormException("Every category needs a name.");
                    foreach (string token in rule.Extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (token.Contains('.') || token.Contains('/') || token.Contains('\\'))
                            throw new FormException($"Extensions must not contain dots or path characters: {token}");
                }

                var settings = _manager.Settings;
                // Capture every value first; restore all of them if persistence fails.
                var backup = (settings.DownloadDirectory, settings.MaxConcurrent, settings.Segments, settings.MaxRetries,
                    settings.UseCategorySubfolders, settings.SpeedLimiterEnabled, settings.MaxGlobalBytesPerSecond,
                    settings.ShowCompletionDialog, settings.StartQueueImmediately, settings.MinimizeToTray, settings.MonitorClipboard,
                    settings.BrowserAutoIntercept, settings.BrowserVideoBalloonEnabled, settings.AutoCaptureExtensions,
                    settings.ExcludedHosts, settings.ProxyMode, settings.ProxyAddress, settings.ProxyUsername, settings.Categories);
                try
                {
                    settings.DownloadDirectory = newDirectory;
                    settings.MaxConcurrent = newConcurrent;
                    settings.Segments = newSegments;
                    settings.MaxRetries = newRetries;
                    settings.UseCategorySubfolders = _categoryFolders.IsChecked == true;
                    settings.SpeedLimiterEnabled = _speedLimiter.IsChecked == true;
                    settings.MaxGlobalBytesPerSecond = newLimit;
                    settings.ShowCompletionDialog = _showCompletion.IsChecked == true;
                    settings.StartQueueImmediately = _startQueue.IsChecked == true;
                    settings.MinimizeToTray = _minimizeTray.IsChecked == true;
                    settings.MonitorClipboard = _clipboard.IsChecked == true;
                    settings.BrowserAutoIntercept = _autoIntercept.IsChecked == true;
                    settings.BrowserVideoBalloonEnabled = _balloon.IsChecked == true;
                    settings.AutoCaptureExtensions = _autoCapture.Text.Trim();
                    settings.ExcludedHosts = _excludedHosts.Text.Trim();
                    settings.ProxyMode = mode;
                    settings.ProxyAddress = proxyAddress;
                    settings.ProxyUsername = string.IsNullOrWhiteSpace(_proxyUser.Text) ? null : _proxyUser.Text.Trim();
                    settings.Categories = [.. _categories.Select(c => new CategoryRule { Name = c.Name.Trim(), Extensions = c.Extensions, Subfolder = c.Subfolder })];
                    _manager.SaveSettings();
                }
                catch
                {
                    (settings.DownloadDirectory, settings.MaxConcurrent, settings.Segments, settings.MaxRetries,
                        settings.UseCategorySubfolders, settings.SpeedLimiterEnabled, settings.MaxGlobalBytesPerSecond,
                        settings.ShowCompletionDialog, settings.StartQueueImmediately, settings.MinimizeToTray, settings.MonitorClipboard,
                        settings.BrowserAutoIntercept, settings.BrowserVideoBalloonEnabled, settings.AutoCaptureExtensions,
                        settings.ExcludedHosts, settings.ProxyMode, settings.ProxyAddress, settings.ProxyUsername, settings.Categories) = backup;
                    throw;
                }
                CloseOk();
            }
            catch (Exception ex) { Problem(ex); }
        };
    }

    private TextBox DirectoryInputIn(Panel parent, string value)
    {
        var input = new TextBox { Text = value, Name = "SettingsDirectory", MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(input, "Default download directory");
        var label = new Label { Content = "_Default download folder", Target = input };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var browse = new Button { Content = "_Browse…", Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) =>
        {
            try
            {
                var dialog = new OpenFolderDialog { Title = "Choose default download directory", Multiselect = false };
                if (Directory.Exists(input.Text)) dialog.InitialDirectory = input.Text;
                if (dialog.ShowDialog(this) == true) input.Text = dialog.FolderName;
            }
            catch (Exception ex) { Error.Text = App.SafeError(ex); }
        };
        row.Children.Add(browse);
        row.Children.Add(input);
        var host = new StackPanel();
        host.Children.Add(label);
        host.Children.Add(row);
        parent.Children.Add(host);
        return input;
    }

    private static TextBox LabeledInput(Panel parent, string labelText, string value, string name)
    {
        var input = new TextBox { Text = value, Name = name, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetName(input, labelText.Replace("_", ""));
        var label = new Label { Content = labelText, Target = input, Margin = new Thickness(0, 0, 0, 2) };
        parent.Children.Add(label);
        parent.Children.Add(input);
        return input;
    }

    private static CheckBox AddCheckbox(Panel parent, string label, bool value, string name)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Name = name, Margin = new Thickness(0, 0, 0, 10), MinHeight = 30 };
        AutomationProperties.SetName(box, label);
        parent.Children.Add(box);
        return box;
    }
}
