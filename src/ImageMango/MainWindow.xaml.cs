using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageMango.Core;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMango.UI;

namespace ImageMango;

public partial class MainWindow : Window
{
    private readonly MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            _vm = new MainViewModel();
        }
        catch (Exception ex)
        {
            MessageBox.Show("ImageMango could not start:\n\n" + ex.Message, "ImageMango", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown(1);
            return;
        }
        DataContext = _vm;

        ApplyPanels();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ModelsPanelOpen) or nameof(MainViewModel.SettingsPanelOpen)) ApplyPanels();
        };

        // Keep the newest engine log line in view.
        ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };

        if (App.ScreenshotPath is { } shot)
        {
            ShowActivated = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000;
            var vm = _vm;
            Loaded += async (_, _) =>
            {
                if (App.SelfTestLoras.Count > 0)
                {
                    foreach (var spec in App.SelfTestLoras)
                    {
                        // "path:weight" — the weight is after the last colon, unless that colon is the drive letter's.
                        int colon = spec.LastIndexOf(':');
                        bool hasWeight = colon > 1 && double.TryParse(spec[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture, out _);
                        var file = hasWeight ? spec[..colon] : spec;
                        await vm.ImportLoraFilesAsync(new[] { file });
                        // Switch it on even when made for another model type (tests the "didn't fit" warning).
                        var name = System.IO.Path.GetFileName(file);
                        if (vm.LoraLibrary.FirstOrDefault(l => l.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } info && !vm.IsLoraActive(info))
                            vm.ToggleLora(info);
                        if (hasWeight && vm.ActiveLoras.FirstOrDefault(r => r.Info.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } row)
                            row.Weight = double.Parse(spec[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                if (App.SelfTestReuse is { } reuse) vm.ReuseImage(reuse);
                if (App.SelfTestHelper is { } helperText) await PromptHelperWindow.SelfTestAsync(this, vm, helperText, shot);
                if (App.SelfTestFindLoras is { } loraQuery) await LoraBrowserWindow.SelfTestAsync(this, vm, loraQuery, shot, App.SelfTestDownloadLoras);
                if (App.SelfTestInitImage is { } init) vm.SetInitImageFromFile(init);
                if (App.SelfTestStrength is double strength) vm.InitStrength = strength;
                if (App.ScreenshotPrompt is { } prompt)
                {
                    vm.Prompt = prompt;
                    vm.GenerateCommand.Execute(null);
                    do await Task.Delay(500); while (vm.IsBusy);
                }
                await Dispatcher.InvokeAsync(() => SaveScreenshot(shot), DispatcherPriority.ApplicationIdle);
            };
            return;
        }

        Loaded += (_, _) =>
        {
            if (_vm.EngineError is { } err)
                MessageBox.Show(err, "Engine missing", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
    }

    private void SaveScreenshot(string path)
    {
        try
        {
            RenderToPng(this, path);
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    /// <summary>Self-test pictures: draws a window's content (on its background) into a PNG.</summary>
    internal static void RenderToPng(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Paint the window background first; the root panel itself is transparent.
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(dv);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }

    // ---------------------------------------------------------------- LoRA browser

    private LoraBrowserWindow? _loraBrowser;

    private void OpenLoraBrowser()
    {
        if (_vm is null) return;
        if (_loraBrowser is { IsLoaded: true })
        {
            _loraBrowser.Activate();
            return;
        }
        _loraBrowser = new LoraBrowserWindow(_vm) { Owner = this };
        _loraBrowser.Closed += (_, _) => _loraBrowser = null;
        _loraBrowser.Show();
    }

    // ---------------------------------------------------------------- prompt helper

    private PromptHelperWindow? _helper;

    /// <summary>Opens the prompt helper (one window; clicking again brings it to the front).</summary>
    private void PromptHelper_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (_helper is { IsLoaded: true })
        {
            _helper.Activate();
            return;
        }
        _helper = new PromptHelperWindow(_vm) { Owner = this };
        _helper.Closed += (_, _) => _helper = null;
        _helper.Show();
    }

    // ---------------------------------------------------------------- side panels

    /// <summary>
    /// Sizes the side columns for the panels' shown/hidden state. A hidden panel's column shrinks to its thin
    /// "show" strip and its splitter column closes; the width it had is remembered for when it comes back.
    /// </summary>
    private void ApplyPanels()
    {
        if (_vm is null) return;
        ApplyPanel(ModelsColumn, ModelsSplitterColumn, _vm.ModelsPanelOpen, 300, w => _vm.ModelsPanelWidth = w, _vm.ModelsPanelWidth);
        ApplyPanel(SettingsColumn, SettingsSplitterColumn, _vm.SettingsPanelOpen, 320, w => _vm.SettingsPanelWidth = w, _vm.SettingsPanelWidth);
    }

    private static void ApplyPanel(ColumnDefinition column, ColumnDefinition splitter, bool open, double minWidth, Action<double> remember, double width)
    {
        bool isOpen = column.Width.IsAbsolute;
        if (open == isOpen && column.ActualWidth > 0) return;
        if (open)
        {
            column.MinWidth = minWidth;
            column.Width = new GridLength(width);
            splitter.Width = new GridLength(5);
        }
        else
        {
            // Still laid out at the old width here: keep it for later.
            if (column.ActualWidth >= minWidth) remember(column.ActualWidth);
            column.MinWidth = 0;
            column.Width = GridLength.Auto;
            splitter.Width = new GridLength(0);
        }
    }

    /// <summary>Remembers the widths the splitters left the open panels at.</summary>
    private void RememberPanelWidths()
    {
        if (_vm is null) return;
        if (_vm.ModelsPanelOpen && ModelsColumn.ActualWidth > 0) _vm.ModelsPanelWidth = ModelsColumn.ActualWidth;
        if (_vm.SettingsPanelOpen && SettingsColumn.ActualWidth > 0) _vm.SettingsPanelWidth = SettingsColumn.ActualWidth;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        RememberPanelWidths();
        _vm?.OnClosing();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm?.Dispose();
        base.OnClosed(e);
    }

    private void Gallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is GalleryItem item)
            _vm?.ShowGalleryItemCommand.Execute(item);
    }

    /// <summary>Opens a menu of the selected model's sample prompts; picking one replaces the prompt.</summary>
    private void SamplePrompts_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var prompts = _vm.Selected?.SamplePrompts ?? Array.Empty<string>();
        if (prompts.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No sample prompts for this model", IsEnabled = false });
        foreach (var prompt in prompts)
        {
            var item = new MenuItem
            {
                Header = new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, MaxWidth = 620, Margin = new Thickness(0, 3, 0, 3) },
                ToolTip = "Use this prompt (replaces the current one)",
            };
            item.Click += (_, _) => _vm.Prompt = prompt;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- start image: drop, paste, gallery menu

    /// <summary>Accepts dropped picture files (start image) and .safetensors files (LoRAs) anywhere in the window.</summary>
    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (DroppedFiles(e).Any(f => ImageStore.IsPictureFile(f) || IsLoraFile(f)))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (_vm is null) return;
        var files = DroppedFiles(e).ToList();
        var loras = files.Where(IsLoraFile).ToList();
        var picture = files.FirstOrDefault(ImageStore.IsPictureFile);
        if (loras.Count == 0 && picture is null) return;
        e.Handled = true;
        if (picture is not null) _vm.SetInitImageFromFile(picture);
        if (loras.Count > 0) await _vm.ImportLoraFilesAsync(loras);
    }

    private static IEnumerable<string> DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files ? files : Array.Empty<string>();

    private static bool IsLoraFile(string path) => path.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ctrl+V with a picture on the clipboard sets the start image (text still pastes into the prompt).</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null || e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
        try
        {
            if (Clipboard.ContainsText()) return;
        }
        catch { return; }
        if (_vm.TryPasteInitImage()) e.Handled = true;
    }

    private void GalleryUseAsStart_Click(object sender, RoutedEventArgs e) =>
        _vm?.UseGalleryItemAsInit((sender as FrameworkElement)?.DataContext as GalleryItem);

    private void GalleryReuse_Click(object sender, RoutedEventArgs e) =>
        _vm?.ReuseImage(((sender as FrameworkElement)?.DataContext as GalleryItem)?.Path);

    private void GalleryOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GalleryItem item)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Path) { UseShellExecute = true });
    }

    // ---------------------------------------------------------------- LoRA menu

    /// <summary>
    /// The Add LoRA menu: downloaded LoRAs made for the selected model (click to switch on/off), others in a
    /// submenu, then ways to get more: browse Hugging Face / Civitai, download from a link, or add a file.
    /// </summary>
    private void AddLora_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button button) return;
        _vm.RefreshLoraLibrary();
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxWidth = 720 };
        var (fits, others) = _vm.LorasForSelected();
        string family = _vm.SelectedFamilyName;

        menu.Items.Add(Header(_vm.SelectedFamily is null ? "Your LoRAs" : $"Your LoRAs for {family} models"));
        if (fits.Count == 0)
            menu.Items.Add(new MenuItem { Header = "None downloaded yet. Find some below.", IsEnabled = false });
        foreach (var l in fits) menu.Items.Add(LoraMenuItem(l));
        if (others.Count > 0)
        {
            var sub = new MenuItem { Header = $"Made for other models ({others.Count})", ToolTip = "These LoRAs were made for a different model type and won't work with the selected model." };
            foreach (var l in others) sub.Items.Add(LoraMenuItem(l));
            menu.Items.Add(sub);
        }

        menu.Items.Add(new Separator());
        var find = new MenuItem
        {
            Header = new TextBlock { Text = $"Find and download {family} LoRAs…", FontWeight = FontWeights.SemiBold },
            Icon = Glyph(""),
            ToolTip = "Search Civitai and Hugging Face from here, tick the LoRAs you want, and ImageMango downloads and switches them on",
            IsEnabled = _vm.SelectedFamily is not null && !_vm.IsLoraDownloading,
        };
        find.Click += (_, _) => OpenLoraBrowser();
        menu.Items.Add(find);
        var links = _vm.LoraBrowseLinks();
        if (links.Count > 0)
        {
            menu.Items.Add(Header($"Find LoRAs for {family} (opens your browser)"));
            foreach (var (label, url) in links)
            {
                var item = new MenuItem { Header = label, Icon = Glyph(""), ToolTip = url };
                item.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                menu.Items.Add(item);
            }
            menu.Items.Add(new MenuItem
            {
                Header = "Then copy the page's address and use \"Download from a link…\"",
                IsEnabled = false,
                FontSize = 13,
            });
        }
        var download = new MenuItem { Header = "Download from a link…", Icon = Glyph(""), ToolTip = "Paste a Hugging Face or Civitai page, or a direct link to a .safetensors file" };
        download.Click += async (_, _) => await DownloadLoraFromLinkAsync();
        download.IsEnabled = !_vm.IsLoraDownloading;
        menu.Items.Add(download);
        var add = new MenuItem { Header = "Add a LoRA file from this PC…", Icon = Glyph(""), ToolTip = "Copies .safetensors files into the LoRA folder. You can also drop them on the window." };
        add.Click += async (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Add LoRA files", Filter = "LoRA files (*.safetensors)|*.safetensors", Multiselect = true };
            if (dlg.ShowDialog(this) == true) await _vm.ImportLoraFilesAsync(dlg.FileNames);
        };
        menu.Items.Add(add);
        var folder = new MenuItem { Header = "Open the LoRA folder", Icon = Glyph(""), Command = _vm.OpenLoraFolderCommand, ToolTip = _vm.LorasDir };
        menu.Items.Add(folder);
        menu.IsOpen = true;
    }

    private MenuItem LoraMenuItem(LoraInfo l)
    {
        bool on = _vm!.IsLoraActive(l);
        var details = $"{(l.Family is null ? "model type unknown" : $"for {LoraFamilies.NameOf(l.Family)}")} · {l.SizeText}" +
                      (string.IsNullOrWhiteSpace(l.TriggerWords) ? "" : $" · trigger: {l.TriggerWords}");
        var item = new MenuItem
        {
            Header = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = l.Name, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = details, FontSize = 13, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 },
                },
            },
            IsCheckable = true,
            IsChecked = on,
            ToolTip = on ? "Click to switch it off" : "Click to switch it on for this model",
        };
        item.Click += (_, _) => _vm.ToggleLora(l);
        return item;
    }

    private async Task DownloadLoraFromLinkAsync()
    {
        if (_vm is null) return;
        string initial = "";
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText().Trim() is { } t && t.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !t.Contains('\n'))
                initial = t;
        }
        catch { }
        var link = Dialogs.AskText(this, "Download a LoRA",
            "Paste the address of a LoRA page on Hugging Face (huggingface.co/…) or Civitai (civitai.com/models/…), " +
            "or a direct link to a .safetensors file. ImageMango reads the page to find the file, the model type it's for and its trigger words.",
            initial);
        if (link is null) return;
        var candidates = await _vm.ResolveLoraLinkAsync(link);
        if (candidates is null || candidates.Count == 0) return;
        var pick = candidates.Count == 1 ? candidates[0]
            : Dialogs.Choose(this, "Download a LoRA", "This page has several files. Pick the one to download:", candidates, c => c.Label);
        if (pick is null) return;

        var fit = LoraFamilies.Fit(pick.Family, _vm.SelectedFamily);
        if (fit == LoraFit.Mismatch && MessageBox.Show(
                $"{pick.Name} is made for {LoraFamilies.NameOf(pick.Family)} models, not for {_vm.Selected?.Name}.\n\nDownload it anyway, to use with a {LoraFamilies.NameOf(pick.Family)} model?",
                "Download a LoRA", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (pick.SizeBytes > 2_500_000_000 && MessageBox.Show(
                $"{pick.FileName} is {pick.SizeBytes / 1e9:0.0} GB, much bigger than a typical LoRA (10–500 MB). It may be a full model, which won't work as a LoRA.\n\nDownload anyway?",
                "Download a LoRA", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await _vm.DownloadLoraAsync(pick);
    }

    private static MenuItem Header(string text) =>
        new() { Header = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Opacity = 0.8 }, IsEnabled = false };

    private static TextBlock Glyph(string glyph) =>
        new() { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15 };

    private void SizePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is SizePreset preset)
        {
            _vm?.ApplySizePresetCommand.Execute(preset);
            cb.SelectedItem = null;
        }
    }
}
