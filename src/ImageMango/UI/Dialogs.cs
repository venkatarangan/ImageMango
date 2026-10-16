using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ImageMango.UI;

/// <summary>Small modal dialogs built in code: ask for a line of text, or pick one item from a list.</summary>
public static class Dialogs
{
    public static string? AskText(Window owner, string title, string message, string initial = "")
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 0), MinWidth = 560 };
        var win = MakeWindow(owner, title, new StackPanel
        {
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 }, box },
        }, out var ok);
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        ok.Click += (_, _) => win.DialogResult = !string.IsNullOrWhiteSpace(box.Text);
        return win.ShowDialog() == true ? box.Text.Trim() : null;
    }

    public static T? Choose<T>(Window owner, string title, string message, IReadOnlyList<T> items, Func<T, string> label) where T : class
    {
        var list = new ListBox { Margin = new Thickness(0, 10, 0, 0), MaxHeight = 360, MinWidth = 620, SelectedIndex = 0 };
        foreach (var item in items)
            list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = label(item), TextWrapping = TextWrapping.Wrap, MaxWidth = 640 }, Tag = item, Padding = new Thickness(6, 4, 6, 4) });
        list.SelectedIndex = 0;
        var win = MakeWindow(owner, title, new StackPanel
        {
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 }, list },
        }, out var ok);
        ok.Click += (_, _) => win.DialogResult = list.SelectedItem is not null;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is not null) win.DialogResult = true; };
        return win.ShowDialog() == true ? (list.SelectedItem as ListBoxItem)?.Tag as T : null;
    }

    private static Window MakeWindow(Window owner, string title, UIElement body, out Button ok)
    {
        ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Padding = new Thickness(12, 4, 12, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { ok, cancel } };
        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        return new Window
        {
            Title = title,
            Owner = owner,
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontSize = owner.FontSize,
        };
    }
}
