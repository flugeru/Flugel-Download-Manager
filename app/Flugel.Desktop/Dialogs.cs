using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Flugel.Core;
using Microsoft.Win32;

namespace Flugel.Desktop;

// Native controls retain their platform keyboard, automation, focus and contrast behavior.
internal abstract class FormDialog : Window
{
    protected readonly StackPanel Fields = new() { Margin = new Thickness(24) };
    protected readonly TextBlock Error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
    protected readonly Button Accept = new() { IsDefault = true, MinWidth = 100 };

    protected FormDialog(string title, string subtitle, string action, double width = 620, double height = 720)
    {
        Style = (Style)FindResource(typeof(Window));
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 480;
        MinHeight = 420;
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

    protected TextBox InputNoGap(string label, string value, string name)
    {
        var input = Input(label, value, name);
        input.Margin = new Thickness(0, 0, 0, 6);
        return input;
    }

    protected TextBox DirectoryInput(string value, string label = "_Destination folder")
    {
        var input = new TextBox { Text = value, MinWidth = 160, Name = "DestinationDirectory" };
        AutomationProperties.SetName(input, "Destination directory");
        Fields.Children.Add(new Label { Content = label, Target = input });
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

    protected ComboBox Choice(string label, IEnumerable<string> items, string? selected, string name)
    {
        var combo = new ComboBox { Name = name, Margin = new Thickness(0, 0, 0, 14), MinHeight = 30 };
        foreach (string item in items) combo.Items.Add(item);
        if (selected is not null && combo.Items.Contains(selected)) combo.SelectedItem = selected;
        else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        AutomationProperties.SetName(combo, label.Replace("_", ""));
        Fields.Children.Add(new Label { Content = label, Target = combo });
        Fields.Children.Add(combo);
        return combo;
    }

    protected CheckBox Check(string label, bool value, string name)
    {
        var box = new CheckBox { Content = label.Replace("_", ""), IsChecked = value, Name = name, Margin = new Thickness(0, 0, 0, 10), MinHeight = 30 };
        AutomationProperties.SetName(box, label.Replace("_", ""));
        Fields.Children.Add(box);
        return box;
    }

    protected TextBlock Note(string text) =>
        new() { Text = text, FontSize = 12, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };

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

    protected static TimeOnly ParseTime(TextBox input, string label)
    {
        if (!TimeOnly.TryParseExact(input.Text.Trim(), "HH\\:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            input.Focus();
            input.SelectAll();
            throw new FormException($"{label} must use 24-hour HH:mm (for example, 18:30).");
        }
        return time;
    }

    protected void Problem(Exception ex) => Error.Text = ex is FormException ? ex.Message : App.SafeError(ex);
    protected void CloseOk()
    {
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true;
        else Close();
    }
    internal void ConfirmForSmoke() => Accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    protected sealed class FormException(string message) : Exception(message);
}

// Details window for one transfer: source, destination, live state and editable notes.
internal sealed class PropertiesDialog : FormDialog
{
    internal PropertiesDialog(DownloadSnapshot snapshot)
        : base("Download properties", "Review source, destination and transfer facts. Notes and queue assignment are saved to the running engine.", "_Save changes", 640, 700)
    {
        var manager = App.Instance.MainWin?.Manager;
        static string Mask(string source)
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return "Source unavailable";
            var safe = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
            return safe.Uri.GetLeftPart(UriPartial.Path);
        }

        Fields.Children.Add(new TextBlock { Text = "File name", FontWeight = FontWeights.SemiBold });
        Fields.Children.Add(new TextBlock { Text = snapshot.FileName, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        Fields.Children.Add(new TextBlock { Text = "Source (query and credentials hidden)", FontWeight = FontWeights.SemiBold });
        Fields.Children.Add(new TextBlock { Text = Mask(snapshot.Url), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        Fields.Children.Add(new TextBlock { Text = "Destination", FontWeight = FontWeights.SemiBold });
        Fields.Children.Add(new TextBlock { Text = snapshot.Destination, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        var facts = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Fact(string label, string value, int row)
        {
            var l = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 12, 3) };
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3), Foreground = (System.Windows.Media.Brush)FindResource("Muted") };
            Grid.SetRow(l, row); Grid.SetRow(v, row); Grid.SetColumn(l, 0); Grid.SetColumn(v, 1);
            facts.Children.Add(l); facts.Children.Add(v);
        }
        for (int i = 0; i < 8; i++) facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Fact("State", snapshot.State.ToString(), 0);
        Fact("Transferred", $"{DownloadSnapshot.FormatSize(snapshot.Received)} of {snapshot.SizeText}", 1);
        Fact("Speed", snapshot.SpeedText, 2);
        Fact("Time left", snapshot.EtaText, 3);
        Fact("Category", snapshot.Category, 4);
        Fact("Queue", snapshot.QueueName, 5);
        Fact("Segments", $"{snapshot.Segments} · resume support: {(snapshot.ResumeSupported ? "yes" : "not detected")}", 6);
        Fact("Last try", snapshot.LastAttemptText, 7);
        Fields.Children.Add(new TextBlock { Text = "Transfer facts", FontWeight = FontWeights.SemiBold });
        Fields.Children.Add(facts);

        if (snapshot.MediaQualityLabel is { Length: > 0 } || snapshot.IsMediaStream)
            Fields.Children.Add(Note($"Media capture: {(snapshot.MediaQualityLabel is { Length: > 0 } q ? q + " · " : "")}{(snapshot.IsMediaStream ? "streaming playlist" : "direct file")}"));

        var description = Input("Description", snapshot.Description, "PropertyDescription");
        var queueItems = manager?.Settings.Queues.Select(q => q.Name).ToList() ?? ["Main"];
        var queue = Choice("_Queue", queueItems, snapshot.QueueName, "PropertyQueue");
        var categoryItems = manager?.Settings.Categories.Select(c => c.Name).ToList() ?? ["General"];
        var category = Choice("_Category", categoryItems, snapshot.Category, "PropertyCategory");
        var limit = Input("_Speed limit for this download (KiB/s, 0 = unlimited)", snapshot.MaxBytesPerSecond is { } b ? (b / 1024).ToString(CultureInfo.InvariantCulture) : "0", "PropertyLimit");
        if (!string.IsNullOrEmpty(snapshot.Error))
            Fields.Children.Add(new TextBlock { Text = "Last error: " + snapshot.Error, Foreground = (System.Windows.Media.Brush)FindResource("Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        Fields.Children.Add(Error);

        Accept.Click += (_, _) =>
        {
            try
            {
                long? limitValue = Number(limit, 0, 1_000_000, "Speed limit") is 0 ? null : Number(limit, 0, 1_000_000, "Speed limit") * 1024L;
                manager?.UpdateJobProperties(snapshot.Id, description.Text.Trim(), queue.SelectedItem as string ?? snapshot.QueueName, category.SelectedItem as string ?? snapshot.Category, limitValue);
                CloseOk();
            }
            catch (Exception ex) { Problem(ex); }
        };
    }
}
