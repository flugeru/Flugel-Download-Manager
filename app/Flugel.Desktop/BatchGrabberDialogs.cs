using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Flugel.Core;

namespace Flugel.Desktop;

// Batch entry: paste a URL list or expand a * pattern, then queue them all.
internal sealed class BatchDialog : FormDialog
{
    internal int AddedCount { get; private set; }

    internal BatchDialog(DownloadManager manager) : base("Add a batch of downloads", "Paste one URL per line (# starts a comment), or expand a numeric/letter pattern that contains *.", "_Add batch", 700, 720)
    {
        var settings = manager.Settings;
        var urls = new TextBox
        {
            Name = "BatchUrls",
            AcceptsReturn = true,
            AcceptsTab = true,
            Height = 220,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };
        AutomationProperties.SetName(urls, "Batch URLs, one per line");
        Fields.Children.Add(new Label { Content = "_URLs", Target = urls });
        Fields.Children.Add(urls);

        var patternGroup = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 14) };
        var pattern = new TextBox { Width = 250, Name = "BatchPattern", VerticalContentAlignment = VerticalAlignment.Center };
        var from = new TextBox { Width = 64, Margin = new Thickness(10, 0, 0, 0), Name = "BatchFrom", VerticalContentAlignment = VerticalAlignment.Center };
        var to = new TextBox { Width = 64, Margin = new Thickness(6, 0, 0, 0), Name = "BatchTo", VerticalContentAlignment = VerticalAlignment.Center };
        var width = new TextBox { Width = 48, Margin = new Thickness(6, 0, 0, 0), Name = "BatchWidth", Text = "1", VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(pattern, "Pattern URL containing an asterisk");
        AutomationProperties.SetName(from, "Range start");
        AutomationProperties.SetName(to, "Range end");
        AutomationProperties.SetName(width, "Zero padding width");
        var expandButton = new Button { Content = "_Expand pattern", Margin = new Thickness(10, 0, 0, 0) };
        patternGroup.Children.Add(new Label { Content = "Pa_ttern", Target = pattern });
        patternGroup.Children.Add(pattern);
        patternGroup.Children.Add(new Label { Content = "_from" });
        patternGroup.Children.Add(from);
        patternGroup.Children.Add(new Label { Content = "_to" });
        patternGroup.Children.Add(to);
        patternGroup.Children.Add(new Label { Content = "_width" });
        patternGroup.Children.Add(width);
        patternGroup.Children.Add(expandButton);
        Fields.Children.Add(patternGroup);

        var countText = new TextBlock { Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 12) };
        Fields.Children.Add(countText);

        var directory = DirectoryInput(manager.Settings.DownloadDirectory, "_Destination folder for all");
        var queue = Choice("_Queue", settings.Queues.Select(q => q.Name), "Main", "BatchQueue");
        var startNow = Check("Start downloads immediately", true, "BatchStartNow");

        void RefreshCount()
        {
            try
            {
                int count = BatchUrlExpander.ParseUrlList(urls.Text).Count;
                countText.Text = count == 0 ? "No valid URLs yet." : $"{count} URL(s) will be added (limit {BatchUrlExpander.MaxBatchCount}).";
            }
            catch (ArgumentException ex) { countText.Text = ex.Message; }
        }
        urls.TextChanged += (_, _) => RefreshCount();
        RefreshCount();

        expandButton.Click += (_, _) =>
        {
            try
            {
                BatchExpansionPreview preview;
                string patternValue = pattern.Text.Trim();
                if (from.Text.Trim().Length == 0 && to.Text.Trim().Length == 0)
                    throw new FormException("Enter a range, for example from 1 to 10. Letters a–z also work.");
                if (int.TryParse(from.Text.Trim(), out int fromNumber) && int.TryParse(to.Text.Trim(), out int toNumber))
                    preview = BatchUrlExpander.ExpandNumeric(patternValue, fromNumber, toNumber, Number(width, 1, 10, "Zero padding width"));
                else if (from.Text.Trim().Length == 1 && to.Text.Trim().Length == 1)
                    preview = BatchUrlExpander.ExpandAlphabetic(patternValue, from.Text.Trim()[0], to.Text.Trim()[0]);
                else
                    throw new FormException("Use a whole-number range or single ASCII letters (a–z / A–Z).");
                urls.Text = string.Join(Environment.NewLine, preview.Urls);
                RefreshCount();
            }
            catch (Exception ex) { Problem(ex); }
        };

        Fields.Children.Add(Error);
        Accept.Click += (_, _) =>
        {
            try
            {
                var parsed = BatchUrlExpander.ParseUrlList(urls.Text);
                if (parsed.Count == 0)
                {
                    urls.Focus();
                    throw new FormException("Enter at least one valid HTTP or HTTPS URL, or expand a pattern.");
                }
                string dest = DirectoryValue(directory);
                string targetQueue = queue.SelectedItem as string ?? "Main";
                bool start = startNow.IsChecked == true;
                foreach (string url in parsed)
                {
                    try
                    {
                        manager.AddWithOptions(new AddDownloadOptions
                        {
                            Url = url,
                            DestinationDirectory = dest,
                            QueueName = targetQueue,
                            StartImmediately = start
                        });
                        AddedCount++;
                    }
                    catch (ArgumentException ex)
                    {
                        Error.Text = $"Stopped at {url}: {ex.Message} {AddedCount} earlier URL(s) were added.";
                        break;
                    }
                }
                if (AddedCount == 0) throw new FormException("No URL from the list could be added.");
                CloseOk();
            }
            catch (Exception ex) { Problem(ex); }
        };
    }
}

// Site / media grabber: fetch a page, list links and media, add selected items.
internal sealed class GrabberDialog : FormDialog
{
    internal int AddedCount { get; private set; }

    internal GrabberDialog(DownloadManager manager) : base("Site and media grabber", "Enter a page URL. Flugel Download Manager lists links, direct media, and unprotected HLS/DASH variants found in that page.", "_Add selected", 760, 740)
    {
        var settings = manager.Settings;
        var pageUrl = new TextBox { Name = "GrabberUrl", VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(pageUrl, "Page URL to inspect");
        var inspectRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var inspect = new Button { Content = "_Inspect page", Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(inspect, Dock.Right);
        inspectRow.Children.Add(inspect);
        inspectRow.Children.Add(pageUrl);
        Fields.Children.Add(new Label { Content = "_Page URL", Target = pageUrl });
        Fields.Children.Add(inspectRow);

        var items = new ObservableCollection<GrabItem>();
        var grid = new DataGrid
        {
            ItemsSource = new ListCollectionView(items),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Extended,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            Height = 330
        };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Get", Binding = new Binding(nameof(GrabItem.Selected)) { Mode = BindingMode.TwoWay }, Width = 48 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Kind", Binding = new Binding(nameof(GrabItem.Kind)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Binding(nameof(GrabItem.FileName)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Label", Binding = new Binding(nameof(GrabItem.Label)), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding(nameof(GrabItem.Status)), Width = 110 });
        Fields.Children.Add(grid);

        var statusText = new TextBlock { Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };
        Fields.Children.Add(statusText);

        var directory = DirectoryInput(manager.Settings.DownloadDirectory, "_Destination folder");
        var queue = Choice("_Queue", settings.Queues.Select(q => q.Name), "Main", "GrabberQueue");
        var startNow = Check("Start downloads immediately", true, "GrabberStartNow");

        inspect.Click += async (_, _) =>
        {
            inspect.IsEnabled = false;
            statusText.Text = "Fetching page…";
            items.Clear();
            try
            {
                string url = pageUrl.Text.Trim();
                try { App.ValidateUrl(url); }
                catch (ArgumentException) { throw new FormException("Enter an absolute HTTP or HTTPS page URL without credentials."); }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var found = await manager.InspectSiteAsync(url, timeout.Token);
                if (found.Count == 0)
                {
                    statusText.Text = "No links or media found on that page. Direct media in scripts or DRM-protected streams are not listed.";
                }
                else
                {
                    foreach (var item in found)
                        items.Add(new GrabItem
                        {
                            Item = item,
                            Selected = !item.IsProtected,
                            Kind = item.IsProtected ? item.Kind + " 🔒" : item.Kind,
                            FileName = item.FileName,
                            Label = item.Label,
                            Status = item.IsProtected ? "Protected — unavailable" : (item.SegmentUrls is { Count: > 0 } ? $"{item.SegmentUrls.Count} segments" : "Direct")
                        });
                    statusText.Text = $"{items.Count} item(s) found. Protected streams stay unselected: Flugel Download Manager does not bypass DRM.";
                    foreach (var row in items) if (row.Item.IsProtected) row.Status = "Protected — unavailable";
                }
            }
            catch (OperationCanceledException) { statusText.Text = "The page did not finish loading within 30 s."; }
            catch (Exception ex) { statusText.Text = ex is FormException ? ex.Message : App.SafeError(ex); }
            finally { inspect.IsEnabled = true; }
        };

        Fields.Children.Add(Error);
        Accept.Click += (_, _) =>
        {
            try
            {
                var selected = items.Where(i => i.Selected && !i.Item.IsProtected).ToList();
                if (selected.Count == 0)
                {
                    grid.Focus();
                    throw new FormException("Select at least one unprotected item to add.");
                }
                string dest = DirectoryValue(directory);
                string targetQueue = queue.SelectedItem as string ?? "Main";
                bool start = startNow.IsChecked == true;
                AddedCount = 0;
                foreach (var row in selected)
                {
                    try
                    {
                        manager.AddWithOptions(new AddDownloadOptions
                        {
                            Url = row.Item.Url,
                            DestinationDirectory = dest,
                            FileName = row.Item.FileName,
                            Category = row.Item.Category,
                            QueueName = targetQueue,
                            StartImmediately = start,
                            MediaSegmentUrls = row.Item.SegmentUrls is { Count: > 0 } ? [.. row.Item.SegmentUrls] : null,
                            MediaQualityLabel = row.Item.SegmentUrls is { Count: > 0 } ? row.Item.Label : null
                        });
                        AddedCount++;
                    }
                    catch (ArgumentException ex)
                    {
                        Error.Text = $"Skipped {row.Item.FileName}: {ex.Message}";
                    }
                }
                if (AddedCount == 0) throw new FormException(Error.Text.Length > 0 ? Error.Text : "No selected item could be added.");
                CloseOk();
            }
            catch (Exception ex) { Problem(ex); }
        };
    }

    private sealed class GrabItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _selected;
        public required GrabbedSiteItem Item { get; init; }
        public required string Kind { get; init; }
        public required string FileName { get; init; }
        public required string Label { get; init; }
        public string Status { get; set; } = "";
        public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Selected))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
