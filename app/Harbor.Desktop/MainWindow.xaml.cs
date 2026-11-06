using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Harbor.Core;

namespace Harbor.Desktop;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand AddCommand = new("Add URL", "Add", typeof(MainWindow), [new KeyGesture(Key.N, ModifierKeys.Control)]);
    public static readonly RoutedUICommand ResumeCommand = new("Resume", "Resume", typeof(MainWindow), [new KeyGesture(Key.R, ModifierKeys.Control)]);
    public static readonly RoutedUICommand PauseCommand = new("Pause", "Pause", typeof(MainWindow), [new KeyGesture(Key.P, ModifierKeys.Control)]);
    public static readonly RoutedUICommand RemoveCommand = new("Remove", "Remove", typeof(MainWindow));
    public static readonly RoutedUICommand UpCommand = new("Move up", "Up", typeof(MainWindow), [new KeyGesture(Key.Up, ModifierKeys.Alt)]);
    public static readonly RoutedUICommand DownCommand = new("Move down", "Down", typeof(MainWindow), [new KeyGesture(Key.Down, ModifierKeys.Alt)]);
    public static readonly RoutedUICommand SettingsCommand = new("Settings", "Settings", typeof(MainWindow), [new KeyGesture(Key.OemComma, ModifierKeys.Control)]);

    private readonly DownloadManager _manager;
    private readonly ObservableCollection<DownloadRow> _rows = [];
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _timer;
    private string _filter = "All";
    private bool _closing, _closed, _dialogOpen, _ready;
    private DownloadRow? Selected => Downloads?.SelectedItem as DownloadRow;

    internal MainWindow(DownloadManager manager, string dataDirectory, string? initialUrl, bool smoke)
    {
        _manager = manager;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = value => value is DownloadRow row && Matches(row.Value.State, _filter);
        InitializeComponent();
        Downloads.ItemsSource = _view;
        Downloads.PreviewKeyDown += (_, e) =>
        {
            // Delete applies only while the download table owns focus, never in a text editor.
            if (e.Key == Key.Delete && Selected is not null)
            {
                RemoveCommand.Execute(null, this);
                e.Handled = true;
            }
        };
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
                ShowAdd(initialUrl);
        };
    }

    internal static bool Matches(DownloadState state, string filter) => filter switch
    {
        "Active" => state == DownloadState.Downloading,
        "Queued" => state is DownloadState.Queued or DownloadState.Scheduled,
        "Completed" => state == DownloadState.Completed,
        "Failed" => state == DownloadState.Failed,
        _ => true
    };

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
            // Snapshot state changes may alter filter membership; selection follows stable ID.
            _view.Refresh();
            if (selectedId is not null)
                Downloads.SelectedItem = _rows.FirstOrDefault(row => row.Value.Id == selectedId && Matches(row.Value.State, _filter));
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
        DetailMeta.Text = item is null ? "" : $"{item.State} · {item.Segments} segment(s)" +
            (item.ScheduledAt is { } when ? $" · Scheduled {when.ToLocalTime():g}" : "");
        // Raw transport errors can contain signed URLs; keep private query strings out of visible errors.
        DetailError.Text = string.IsNullOrEmpty(item?.Error) ? "" : "Transfer reported an error. Check the source link, network, destination permissions, and available disk space; then Resume to retry. Source query and transport details are hidden for privacy.";
    }

    internal void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => ErrorBanner.Visibility = Visibility.Collapsed;
    private void Downloads_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetails();

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string filter }) _filter = filter;
        if (Downloads is not null) RefreshSnapshots();
    }

    private void Always_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_closing && !_dialogOpen;
    private void Selection_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_closing && !_dialogOpen && Selected is not null;
    private void Resume_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_closing && !_dialogOpen && Selected?.Value.State is DownloadState.Paused or DownloadState.Failed;
    private void Pause_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_closing && !_dialogOpen && Selected?.Value.State is DownloadState.Downloading or DownloadState.Queued or DownloadState.Scheduled;
    private void Add_Executed(object sender, ExecutedRoutedEventArgs e) => ShowAdd(null);
    private void Resume_Executed(object sender, ExecutedRoutedEventArgs e) => ApplySelection(_manager.Resume);
    private void Pause_Executed(object sender, ExecutedRoutedEventArgs e) => ApplySelection(_manager.Pause);
    private void Up_Executed(object sender, ExecutedRoutedEventArgs e) => ApplySelection(id => _manager.Move(id, -1));
    private void Down_Executed(object sender, ExecutedRoutedEventArgs e) => ApplySelection(id => _manager.Move(id, 1));

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

    private void ShowAdd(string? url)
    {
        if (_dialogOpen || _closing) return;
        _dialogOpen = true;
        try
        {
            var dialog = new AddDialog(_manager, url) { Owner = this };
            dialog.ShowDialog();
            RefreshSnapshots();
            if (dialog.AddedId is { } id)
            {
                var row = _rows.FirstOrDefault(item => item.Value.Id == id);
                if (row is not null && Matches(row.Value.State, _filter))
                {
                    Downloads.SelectedItem = row;
                    Downloads.ScrollIntoView(row);
                }
                StatusText.Text = "Download added. Queue and schedule managed automatically.";
            }
        }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; CommandManager.InvalidateRequerySuggested(); }
    }

    private void Settings_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _dialogOpen = true;
        try { new SettingsDialog(_manager) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { ShowError(App.SafeError(ex)); }
        finally { _dialogOpen = false; CommandManager.InvalidateRequerySuggested(); }
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
