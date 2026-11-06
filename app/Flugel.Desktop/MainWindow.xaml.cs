using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Flugel.Core;

namespace Flugel.Desktop;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand AddCommand = new("Add URL", "Add", typeof(MainWindow), [new KeyGesture(Key.N, ModifierKeys.Control)]);
    public static readonly RoutedUICommand BatchCommand = new("Batch URLs", "Batch", typeof(MainWindow), [new KeyGesture(Key.B, ModifierKeys.Control)]);
    public static readonly RoutedUICommand GrabberCommand = new("Site grabber", "Grabber", typeof(MainWindow), [new KeyGesture(Key.G, ModifierKeys.Control)]);
    public static readonly RoutedUICommand ImportCommand = new("Import", "Import", typeof(MainWindow));
    public static readonly RoutedUICommand ExportCommand = new("Export", "Export", typeof(MainWindow));
    public static readonly RoutedUICommand ResumeCommand = new("Resume", "Resume", typeof(MainWindow), [new KeyGesture(Key.R, ModifierKeys.Control)]);
    public static readonly RoutedUICommand PauseCommand = new("Pause", "Pause", typeof(MainWindow), [new KeyGesture(Key.P, ModifierKeys.Control)]);
    public static readonly RoutedUICommand RemoveCommand = new("Remove", "Remove", typeof(MainWindow));
    public static readonly RoutedUICommand UpCommand = new("Move up", "Up", typeof(MainWindow), [new KeyGesture(Key.Up, ModifierKeys.Alt)]);
    public static readonly RoutedUICommand DownCommand = new("Move down", "Down", typeof(MainWindow), [new KeyGesture(Key.Down, ModifierKeys.Alt)]);
    public static readonly RoutedUICommand SettingsCommand = new("Settings", "Settings", typeof(MainWindow), [new KeyGesture(Key.OemComma, ModifierKeys.Control)]);
    public static readonly RoutedUICommand SearchCommand = new("Find", "Find", typeof(MainWindow), [new KeyGesture(Key.F, ModifierKeys.Control)]);
    public static readonly RoutedUICommand PauseAllCommand = new("Pause all", "PauseAll", typeof(MainWindow));
    public static readonly RoutedUICommand ResumeAllCommand = new("Resume all", "ResumeAll", typeof(MainWindow));
    public static readonly RoutedUICommand QueueDialogCommand = new("Queue manager", "Queues", typeof(MainWindow));
    public static readonly RoutedUICommand ToggleSidebarCommand = new("Toggle categories", "Sidebar", typeof(MainWindow), [new KeyGesture(Key.B, ModifierKeys.Control | ModifierKeys.Shift)]);
    public static readonly RoutedUICommand PropertiesCommand = new("Properties", "Properties", typeof(MainWindow), [new KeyGesture(Key.Enter, ModifierKeys.Control)]);

    private readonly DownloadManager _manager;
    private readonly ObservableCollection<DownloadRow> _rows = [];
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _timer;
    private string _filter = "All";
    private string _search = "";
    private bool _closing, _closed, _dialogOpen, _ready;
    private DownloadRow? Selected => Downloads?.SelectedItem as DownloadRow;

    internal DownloadManager Manager => _manager;

    internal MainWindow(DownloadManager manager, string dataDirectory, string? initialUrl, string? initialFileName,
        string? initialReferrer, string? initialMediaQuality, List<string>? initialSegments, bool smoke, string? initialCookie = null)
    {
        _manager = manager;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = value => value is DownloadRow row && Matches(row, _filter, _search);
        InitializeComponent();
        DataContext = this; // Enables View-menu speed-limiter toggle binding.
        Downloads.ItemsSource = _view;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => RefreshSnapshots();
        _ready = true;
        Loaded += async (_, _) =>
        {
            RefreshSnapshots();
            _timer.Start();
            if (smoke)
                await DesktopSmoke.RunAsync(this, _manager, dataDirectory);
            else if (initialUrl is not null)
                ShowAdd(new AddDownloadOptions
                {
                    Url = initialUrl,
                    FileName = initialFileName,
                    Referrer = initialReferrer,
                    MediaQualityLabel = initialMediaQuality,
                    MediaSegmentUrls = initialSegments,
                    CookieHeader = initialCookie,
                    StartImmediately = false
                });
        };
    }

    // Legacy state filter contract retained for regression checks.
    internal static bool Matches(DownloadState state, string filter) => filter switch
    {
        "Active" => state == DownloadState.Downloading,
        "Queued" => state is DownloadState.Queued or DownloadState.Scheduled,
        "Completed" => state == DownloadState.Completed,
        "Failed" => state == DownloadState.Failed,
        _ => true
    };

    internal static bool Matches(DownloadRow row, string filter, string search)
    {
        var s = row.Value;
        bool stateOk = filter switch
        {
            "All" => true,
            "State:Queued" => s.State is DownloadState.Queued or DownloadState.Scheduled or DownloadState.Paused,
            "State:Completed" => s.State == DownloadState.Completed,
            "State:Failed" => s.State == DownloadState.Failed,
            "State:Active" => s.State == DownloadState.Downloading,
            _ when filter.StartsWith("Category:", StringComparison.Ordinal) => string.Equals(s.Category, filter[9..], StringComparison.OrdinalIgnoreCase),
            _ when filter.StartsWith("Queue:", StringComparison.Ordinal) => string.Equals(s.QueueName, filter[6..], StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        if (!stateOk) return false;
        if (string.IsNullOrWhiteSpace(search)) return true;
        return s.FileName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               s.Url.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    internal void RefreshSnapshots()
    {
        if (!_ready || _closed || _closing) return;
        try
        {
            var snapshots = _manager.GetSnapshots();
            string? selectedId = Selected?.Value.Id;
            var ids = snapshots.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            for (int i = _rows.Count - 1; i >= 0; i--)
                if (!ids.Contains(_rows[i].Value.Id)) _rows.RemoveAt(i);
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                var existing = _rows.FirstOrDefault(row => row.Value.Id == snapshot.Id);
                if (existing is null) _rows.Insert(i, new DownloadRow(snapshot));
                else
                {
                    existing.Update(snapshot);
                    int index = _rows.IndexOf(existing);
                    if (index != i) _rows.Move(index, i);
                }
            }
            _view.Refresh();
            if (selectedId is not null)
                Downloads.SelectedItem = _rows.FirstOrDefault(row => row.Value.Id == selectedId && Matches(row, _filter, _search));
            int active = snapshots.Count(item => item.State == DownloadState.Downloading);
            TransferSpeed.Text = FormatSpeed(snapshots.Where(item => item.State == DownloadState.Downloading).Sum(item => item.BytesPerSecond));
            ActiveSummary.Text = active == 1 ? "1 active transfer" : $"{active} active transfers";
            CountSummary.Text = $"{_view.Cast<object>().Count()} shown · {snapshots.Count} total";
            EmptyState.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            EmptyTitle.Text = snapshots.Count == 0 ? "Room for your next download" : "No downloads in this view";
            UpdateDetails();
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
    }

    private static string FormatSpeed(double speed) => speed >= 1048576 ? $"{speed / 1048576:0.0} MiB/s" : speed >= 1024 ? $"{speed / 1024:0.0} KiB/s" : $"{Math.Max(0, speed):0} B/s";

    internal static string DisplaySource(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return "Source unavailable";
        var safe = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
        return safe.Uri.GetLeftPart(UriPartial.Path);
    }

    private void UpdateDetails()
    {
        if (!_ready) return;
        var item = Selected?.Value;
        DetailTitle.Text = item?.FileName ?? "Transfer details";
        DetailSource.Text = item is null ? "Select a download to see its source and destination." : DisplaySource(item.Url);
        DetailDestination.Text = item?.Destination ?? "";
        DetailMeta.Text = item is null ? "" :
            $"{item.State} · {item.Category} · {item.QueueName} queue · {item.Segments} segment(s) · {item.SizeText}" +
            (item.MediaQualityLabel is { Length: > 0 } q ? $" · {q}" : "") +
            (item.ScheduledAt is { } when ? $" · Scheduled {when.ToLocalTime():g}" : "") +
            (item.CompletedAt is { } done ? $" · Completed {done.ToLocalTime():g}" : "") +
            (item.Description is { Length: > 0 } d ? $" · {d}" : "");
        DetailError.Text = string.IsNullOrEmpty(item?.Error) ? "" : "Transfer reported an error. Check the source link, network, destination permissions, and available disk space; then Resume to retry. Source query and transport details are hidden for privacy.";
    }

    internal void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => ErrorBanner.Visibility = Visibility.Collapsed;
    private void Downloads_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetails();

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: string tag }) _filter = tag;
        else if (e.NewValue is TreeViewItem) _filter = "All";
        if (Downloads is not null) RefreshSnapshots();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _search = SearchBox.Text.Trim();
            RefreshSnapshots();
            e.Handled = true;
        }
    }

    private void Search_Executed(object sender, ExecutedRoutedEventArgs e) => FocusSearch();

    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_search)) { FocusSearch(); return; }
        int start = Selected is null ? -1 : _rows.IndexOf(Selected);
        for (int i = 1; i <= _rows.Count; i++)
        {
            var row = _rows[(start + i + _rows.Count) % _rows.Count];
            if (Matches(row, _filter, _search))
            {
                Downloads.SelectedItem = row;
                Downloads.ScrollIntoView(row);
                return;
            }
        }
    }

    private void Always_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_closing && !_dialogOpen;
    private void Selection_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_closing && !_dialogOpen && Selected is not null;
    private void Resume_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_closing && !_dialogOpen && Selected?.Value.State is DownloadState.Paused or DownloadState.Failed;
    private void Pause_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_closing && !_dialogOpen && Selected?.Value.State is DownloadState.Downloading or DownloadState.Queued or DownloadState.Scheduled;
    private void Add_Executed(object sender, ExecutedRoutedEventArgs e) => ShowAdd((AddDownloadOptions?)null);
    private void Resume_Executed(object sender, ExecutedRoutedEventArgs e) => ApplySelection(_manager.Resume);
    private void Pause_Executed(object sender, ExecutedRoutedEventArgs e) => ApplySelection(_manager.Pause);
    private void Up_Executed(object sender, ExecutedRoutedEventArgs e) => ApplySelection(id => _manager.Move(id, -1));
    private void Down_Executed(object sender, ExecutedRoutedEventArgs e) => ApplySelection(id => _manager.Move(id, 1));
    private void PauseAll_Executed(object sender, ExecutedRoutedEventArgs e) { try { _manager.PauseAll(); RefreshSnapshots(); } catch (Exception ex) { ShowError(App.SafeError(ex)); } }
    private void ResumeAll_Executed(object sender, ExecutedRoutedEventArgs e) { try { _manager.ResumeAll(); RefreshSnapshots(); } catch (Exception ex) { ShowError(App.SafeError(ex)); } }
    private void ToggleSidebar_Executed(object sender, ExecutedRoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar() => Sidebar.Visibility = Sidebar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void StartMainQueue_Click(object sender, RoutedEventArgs e) { try { _manager.StartQueue("Main"); RefreshSnapshots(); StatusText.Text = "Main queue started."; } catch (Exception ex) { ShowError(App.SafeError(ex)); } }
    private void StopMainQueue_Click(object sender, RoutedEventArgs e) { try { _manager.StopQueue("Main"); RefreshSnapshots(); StatusText.Text = "Main queue stopped."; } catch (Exception ex) { ShowError(App.SafeError(ex)); } }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    public bool SpeedLimiterChecked
    {
        get => _manager.Settings.SpeedLimiterEnabled;
        set { _manager.Settings.SpeedLimiterEnabled = value; try { _manager.SaveSettings(); StatusText.Text = value ? "Speed limiter enabled." : "Speed limiter disabled."; } catch (Exception ex) { ShowError(App.SafeError(ex)); } }
    }

    private void ApplySelection(Action<string> operation)
    {
        if (Selected is not { } row || _closing) return;
        try { operation(row.Value.Id); RefreshSnapshots(); }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
    }

    private void Remove_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Selected is null) return;
        if (MessageBox.Show(this, "Remove this download from history?\n\nActive transfers will stop. Private incomplete data will be removed. Completed files will not be deleted.",
                "Remove download", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            ApplySelection(_manager.Remove);
    }

    private void Downloads_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Selected is not null)
        {
            RemoveCommand.Execute(null, this);
            e.Handled = true;
        }
    }

    private void Downloads_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not { } row) return;
        var s = row.Value;
        try
        {
            if (s.State == DownloadState.Completed && File.Exists(s.Destination))
            {
                Process.Start(new ProcessStartInfo(s.Destination) { UseShellExecute = true });
            }
            else if (s.State is DownloadState.Paused or DownloadState.Failed)
                _manager.Resume(s.Id);
            RefreshSnapshots();
        }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
    }

    private void Downloads_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null and not DataGridRow) hit = VisualTreeHelper.GetParent(hit);
        if (hit is DataGridRow row && row.Item is DownloadRow item)
        {
            Downloads.SelectedItem = item;
            ShowRowContextMenu(item);
            e.Handled = true;
        }
    }

    private void ShowRowContextMenu(DownloadRow row)
    {
        var s = row.Value;
        var menu = new ContextMenu();
        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) => { try { action(); RefreshSnapshots(); } catch (Exception ex) { ShowError(App.SafeError(ex)); } };
            return mi;
        }

        bool done = s.State == DownloadState.Completed;
        menu.Items.Add(Item("Open file", () =>
        {
            if (done && File.Exists(s.Destination))
                Process.Start(new ProcessStartInfo(s.Destination) { UseShellExecute = true });
        }, done));
        menu.Items.Add(Item("Open containing folder", () =>
        {
            if (File.Exists(s.Destination))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{s.Destination}\"") { UseShellExecute = true });
            else if (Directory.Exists(s.Destination))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{s.Destination}\"") { UseShellExecute = true });
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Resume", () => _manager.Resume(s.Id), s.State is DownloadState.Paused or DownloadState.Failed));
        menu.Items.Add(Item("Pause", () => _manager.Pause(s.Id), s.State is DownloadState.Downloading or DownloadState.Queued or DownloadState.Scheduled));
        menu.Items.Add(Item("Redownload (delete completed file)", () => _manager.Redownload(s.Id), done));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Move up", () => _manager.Move(s.Id, -1)));
        menu.Items.Add(Item("Move down", () => _manager.Move(s.Id, 1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Properties…", () => ShowProperties(s.Id)));
        menu.Items.Add(Item("Remove from history", () =>
        {
            if (MessageBox.Show(this, "Remove this download from history?\n\nActive transfers will stop. Private incomplete data will be removed. Completed files will not be deleted.",
                    "Remove download", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                _manager.Remove(s.Id);
        }));
        menu.PlacementTarget = Downloads;
        menu.IsOpen = true;
    }

    internal void ShowProperties(string id)
    {
        if (_dialogOpen || _closing) return;
        var snap = _manager.GetSnapshots().FirstOrDefault(x => x.Id == id);
        if (snap is null) return;
        _dialogOpen = true;
        try { new PropertiesDialog(snap) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; }
    }

    private void Properties_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Selected is { } row) ShowProperties(row.Value.Id);
    }

    internal bool TryAcceptBrowserDownload(AddDownloadOptions options)
    {
        if (_dialogOpen || _closing) return false;
        _dialogOpen = true; // Reserve before acknowledging; modal UI must not block the pipe reply.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _dialogOpen = false;
            App.Instance.ShowAndActivate();
            ShowAdd(options);
        }));
        return true;
    }

    internal void ShowAdd(AddDownloadOptions? options)
    {
        if (_dialogOpen || _closing) return;
        _dialogOpen = true;
        try
        {
            var dialog = new AddDialog(_manager, options) { Owner = this };
            dialog.ShowDialog();
            RefreshSnapshots();
            if (dialog.AddedId is { } id)
            {
                var row = _rows.FirstOrDefault(item => item.Value.Id == id);
                if (row is not null && Matches(row, _filter, _search))
                {
                    Downloads.SelectedItem = row;
                    Downloads.ScrollIntoView(row);
                }
                StatusText.Text = dialog.StartedNow ? "Download added and started." : "Download added to queue.";
            }
        }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; CommandManager.InvalidateRequerySuggested(); }
    }

    // Back-compat overload used by smoke tests and simple call sites.
    internal void ShowAdd(string? url) => ShowAdd(url is null ? null : new AddDownloadOptions { Url = url, StartImmediately = false });

    private void Batch_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_dialogOpen || _closing) return;
        _dialogOpen = true;
        try { new BatchDialog(_manager) { Owner = this }.ShowDialog(); RefreshSnapshots(); }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; }
    }

    private void Grabber_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_dialogOpen || _closing) return;
        _dialogOpen = true;
        try { new GrabberDialog(_manager) { Owner = this }.ShowDialog(); RefreshSnapshots(); }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; }
    }

    private void Import_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_dialogOpen || _closing) return;
        _dialogOpen = true;
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import download list",
                Filter = "Flugel Download Manager / text lists (*.shb.json;*.txt;*.ef2)|*.shb.json;*.txt;*.ef2|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true)
            {
                int count = _manager.ImportFromFile(dialog.FileName, startImmediately: false);
                StatusText.Text = $"Imported {count} download(s).";
                RefreshSnapshots();
            }
        }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; }
    }

    private void Export_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_dialogOpen || _closing) return;
        _dialogOpen = true;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export download list",
                Filter = "Flugel Download Manager list (*.shb.json)|*.shb.json|Text URL list (*.txt)|*.txt",
                FileName = "flugel-download-manager-list"
            };
            if (dialog.ShowDialog(this) == true)
            {
                if (dialog.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) _manager.ExportUrlList(dialog.FileName);
                else _manager.ExportFlugelFile(dialog.FileName);
                StatusText.Text = "Download list exported.";
            }
        }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; }
    }

    private void Settings_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _dialogOpen = true;
        try { new SettingsDialog(_manager) { Owner = this }.ShowDialog(); RefreshSnapshots(); }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; CommandManager.InvalidateRequerySuggested(); }
    }

    private void QueueDialog_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _dialogOpen = true;
        try { new QueueSchedulerDialog(_manager) { Owner = this }.ShowDialog(); RefreshSnapshots(); }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        if (await StopAsync())
        {
            Close();
            Application.Current.Shutdown();
        }
    }

    internal async Task<bool> StopAsync()
    {
        if (_closed) return true;
        if (_closing) return false;
        _closing = true;
        _timer.Stop();
        StatusText.Text = "Stopping transfers and saving resumable state…";
        IsEnabled = false;
        try
        {
            await _manager.DisposeAsync();
            _closed = true;
            return true;
        }
        catch (Exception ex)
        {
            IsEnabled = true;
            _closing = false;
            StatusText.Text = "Close did not finish. Keep this window open; check disk space and permissions, then close again.";
            ShowError(App.SafeError(ex));
            return false;
        }
    }

    internal DataGrid DownloadTable => Downloads;
}

internal sealed class DownloadRow(DownloadSnapshot value) : INotifyPropertyChanged
{
    public DownloadSnapshot Value { get; private set; } = value;
    public bool IsIndeterminate => Value.Total is null && Value.State == DownloadState.Downloading;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(DownloadSnapshot value)
    {
        if (Value == value) return;
        Value = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIndeterminate)));
    }
}
