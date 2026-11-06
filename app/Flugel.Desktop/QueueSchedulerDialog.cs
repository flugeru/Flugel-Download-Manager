using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Flugel.Core;

namespace Flugel.Desktop;

// Named queues with concurrency and daily start/stop windows + active days.
internal sealed class QueueSchedulerDialog : FormDialog
{
    private readonly DownloadManager _manager;
    private readonly ObservableCollectionEx<QueueDefinition> _queues;
    private readonly DataGrid _grid;
    private readonly TextBox _name, _concurrent, _startTime, _stopTime;
    private readonly CheckBox _started, _enableStart, _enableStop;
    private readonly List<CheckBox> _days = [];

    internal QueueSchedulerDialog(DownloadManager manager) : base("Queues and scheduler", "Each queue runs downloads with its own concurrency limit and optional daily start/stop window. Changes apply immediately and persist.", "_Save queues", 820, 780)
    {
        _manager = manager;
        _queues = new ObservableCollectionEx<QueueDefinition>(
            manager.Settings.Queues.Select(Clone));

        _grid = new DataGrid
        {
            ItemsSource = new ListCollectionView(_queues),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            Height = 180
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Queue", Binding = new Binding(nameof(QueueDefinition.Name)), Width = 170 });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Running", Binding = new Binding(nameof(QueueDefinition.IsStarted)), Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Max parallel", Binding = new Binding(nameof(QueueDefinition.MaxConcurrent)), Width = 90 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Start", Binding = new Binding(nameof(QueueDefinition.DailyStartTime)), Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Stop", Binding = new Binding(nameof(QueueDefinition.DailyStopTime)), Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Active days", Binding = new Binding { Converter = new DaysConverter() }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        Fields.Children.Add(_grid);

        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 14) };
        var addButton = new Button { Content = "_New queue", Margin = new Thickness(0, 0, 8, 0) };
        var removeButton = new Button { Content = "_Remove queue", Margin = new Thickness(0, 0, 8, 0) };
        var startButton = new Button { Content = "_Start now", Margin = new Thickness(0, 0, 8, 0) };
        var stopButton = new Button { Content = "S_top now" };
        listButtons.Children.Add(addButton);
        listButtons.Children.Add(removeButton);
        listButtons.Children.Add(startButton);
        listButtons.Children.Add(stopButton);
        Fields.Children.Add(listButtons);

        Fields.Children.Add(new Label { Content = "Selected queue" });
        _name = LabeledBox("_Queue name", "QueueNameEditor");
        _concurrent = LabeledBox("_Max parallel downloads · 1–16", "QueueConcurrentEditor");
        _started = new CheckBox { Content = "Queue is running", Margin = new Thickness(0, 0, 0, 10), MinHeight = 28 };
        AutomationProperties.SetName(_started, "Queue is running");
        Fields.Children.Add(_started);
        _enableStart = new CheckBox { Content = "_Start automatically at time", Margin = new Thickness(0, 4, 0, 6), MinHeight = 28 };
        _startTime = new TextBox { Width = 90, Margin = new Thickness(0, 0, 0, 10), Name = "QueueStartTime", VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_enableStart, "Start automatically at time");
        AutomationProperties.SetName(_startTime, "Daily start time, 24-hour HH:mm");
        Fields.Children.Add(_enableStart);
        Fields.Children.Add(_startTime);
        _enableStop = new CheckBox { Content = "_Stop automatically at time", Margin = new Thickness(0, 0, 0, 6), MinHeight = 28 };
        _stopTime = new TextBox { Width = 90, Margin = new Thickness(0, 0, 0, 10), Name = "QueueStopTime", VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_enableStop, "Stop automatically at time");
        AutomationProperties.SetName(_stopTime, "Daily stop time, 24-hour HH:mm");
        Fields.Children.Add(_enableStop);
        Fields.Children.Add(_stopTime);

        Fields.Children.Add(new Label { Content = "_Active days" });
        var dayRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            var box = new CheckBox
            {
                Content = day.ToString()[..3],
                Margin = new Thickness(0, 0, 10, 4),
                MinHeight = 26,
                Tag = day
            };
            AutomationProperties.SetName(box, day.ToString());
            _days.Add(box);
            dayRow.Children.Add(box);
        }
        Fields.Children.Add(dayRow);
        Fields.Children.Add(Error);

        void LoadEditor()
        {
            if (_grid.SelectedItem is not QueueDefinition q) return;
            _name.Text = q.Name;
            _concurrent.Text = q.MaxConcurrent.ToString(CultureInfo.InvariantCulture);
            _started.IsChecked = q.IsStarted;
            _enableStart.IsChecked = q.EnableScheduleStart;
            _startTime.Text = q.DailyStartTime;
            _enableStop.IsChecked = q.EnableScheduleStop;
            _stopTime.Text = q.DailyStopTime;
            foreach (var box in _days)
                box.IsChecked = q.ActiveDays.Contains((DayOfWeek)(box.Tag ?? DayOfWeek.Sunday));
        }

        bool StoreEditor()
        {
            if (_grid.SelectedItem is not QueueDefinition q) return true;
            try
            {
                string name = _name.Text.Trim();
                if (name.Length == 0) throw new FormException("Queue name cannot be empty.");
                if (_queues.Any(x => !ReferenceEquals(x, q) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw new FormException($"Another queue is already named “{name}”.");
                q.Name = name;
                q.MaxConcurrent = Number(_concurrent, 1, 16, "Max parallel downloads");
                q.IsStarted = _started.IsChecked == true;
                q.EnableScheduleStart = _enableStart.IsChecked == true;
                q.EnableScheduleStop = _enableStop.IsChecked == true;
                if (q.EnableScheduleStart) q.DailyStartTime = ParseTime(_startTime, "Start time").ToString("HH:mm", CultureInfo.InvariantCulture);
                if (q.EnableScheduleStop) q.DailyStopTime = ParseTime(_stopTime, "Stop time").ToString("HH:mm", CultureInfo.InvariantCulture);
                var days = _days.Where(b => b.IsChecked == true).Select(b => (DayOfWeek)b.Tag!).ToList();
                if (days.Count == 0) throw new FormException("Select at least one active day.");
                q.ActiveDays = days;
                _grid.Items.Refresh();
                return true;
            }
            catch (Exception ex) { Problem(ex); return false; }
        }

        _grid.SelectionChanged += (_, _) => { Error.Text = ""; LoadEditor(); };
        addButton.Click += (_, _) =>
        {
            if (!StoreEditor()) return;
            string baseName = "New queue";
            string name = baseName;
            int suffix = 2;
            while (_queues.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) name = $"{baseName} {suffix++}";
            var queue = new QueueDefinition { Name = name, IsStarted = false, MaxConcurrent = 2 };
            _queues.Add(queue);
            _grid.SelectedItem = queue;
            _name.Focus();
            _name.SelectAll();
        };
        removeButton.Click += (_, _) =>
        {
            if (_grid.SelectedItem is not QueueDefinition q) return;
            if (string.Equals(q.Name, "Main", StringComparison.OrdinalIgnoreCase))
            {
                Error.Text = "The Main queue cannot be removed.";
                return;
            }
            if (_manager.GetSnapshots().Any(s => string.Equals(s.QueueName, q.Name, StringComparison.OrdinalIgnoreCase) && s.State is DownloadState.Queued or DownloadState.Scheduled or DownloadState.Downloading))
            {
                Error.Text = $"“{q.Name}” still has downloads. Move them to another queue first.";
                return;
            }
            _queues.Remove(q);
        };
        startButton.Click += (_, _) =>
        {
            if (_grid.SelectedItem is not QueueDefinition q) return;
            try { _manager.StartQueue(q.Name); q.IsStarted = true; _started.IsChecked = true; _grid.Items.Refresh(); }
            catch (Exception ex) { Problem(ex); }
        };
        stopButton.Click += (_, _) =>
        {
            if (_grid.SelectedItem is not QueueDefinition q) return;
            try { _manager.StopQueue(q.Name); q.IsStarted = false; _started.IsChecked = false; _grid.Items.Refresh(); }
            catch (Exception ex) { Problem(ex); }
        };

        _grid.SelectedItem = _queues.Count > 0 ? _queues[0] : null;
        Accept.Click += (_, _) =>
        {
            try
            {
                if (!StoreEditor()) return;
                if (_queues.Count == 0) throw new FormException("Keep at least one queue.");
                foreach (var q in _queues)
                {
                    if (string.IsNullOrWhiteSpace(q.Name)) throw new FormException("Every queue needs a name.");
                    if (q.EnableScheduleStart || q.EnableScheduleStop)
                    {
                        _ = ParseTime(new TextBox { Text = q.DailyStartTime }, "Start time");
                        _ = ParseTime(new TextBox { Text = q.DailyStopTime }, "Stop time");
                    }
                }
                var settings = _manager.Settings;
                var backup = settings.Queues;
                try
                {
                    settings.Queues = [.. _queues.Select(Clone)];
                    _manager.SaveSettings();
                    // Apply running/stopped state to the live engine.
                    foreach (var q in settings.Queues)
                    {
                        if (q.IsStarted) { try { _manager.StartQueue(q.Name); } catch { } }
                        else { try { _manager.StopQueue(q.Name); } catch { } }
                    }
                }
                catch { settings.Queues = backup; throw; }
                CloseOk();
            }
            catch (Exception ex) { Problem(ex); }
        };
    }

    private TextBox LabeledBox(string label, string name)
    {
        var input = new TextBox { Name = name, Width = 220, Margin = new Thickness(0, 0, 0, 12), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(input, label.Replace("_", ""));
        var text = new Label { Content = label, Target = input };
        Fields.Children.Add(text);
        Fields.Children.Add(input);
        return input;
    }

    private static QueueDefinition Clone(QueueDefinition q) => new()
    {
        Name = q.Name,
        IsStarted = q.IsStarted,
        MaxConcurrent = q.MaxConcurrent,
        EnableScheduleStart = q.EnableScheduleStart,
        DailyStartTime = q.DailyStartTime,
        EnableScheduleStop = q.EnableScheduleStop,
        DailyStopTime = q.DailyStopTime,
        ActiveDays = [.. q.ActiveDays]
    };

    private sealed class DaysConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is QueueDefinition q ? string.Join(", ", q.ActiveDays.Select(d => d.ToString()[..2])) : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    // Minimal observable list that raises reset notifications on add/remove.
    private sealed class ObservableCollectionEx<T>(IEnumerable<T> items) : System.Collections.ObjectModel.ObservableCollection<T>(items)
    {
        public void ReplaceAll(IEnumerable<T> replacement)
        {
            Items.Clear();
            foreach (var item in replacement) Items.Add(item);
        }
    }
}
