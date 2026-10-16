using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMango.Core;

namespace ImageMango.UI;

/// <summary>
/// Finds LoRAs for the selected model on Civitai and Hugging Face, and downloads the ticked ones one after another
/// (they're switched on when they fit). The sites are contacted only while this window searches or loads previews.
/// </summary>
public partial class LoraBrowserWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<LoraResultItem> _items = new();
    private readonly SemaphoreSlim _thumbGate = new(6, 6);
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _queueCts;
    private string? _civitaiCursor;
    private (string Query, LoraSort Sort) _lastSearch;
    private bool _downloading;
    private LoraResultItem? _current;

    public LoraBrowserWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Results.ItemsSource = _items;
        var model = vm.Selected?.Name ?? "the selected model";
        Header.Text = $"Find LoRAs for {model}";
        SubHeader.Text = $"{vm.SelectedFamilyName} LoRAs from civitai.com and huggingface.co. Tick the ones you want and click Download: they're saved in the " +
                         "LoRA folder and switched on. " + (vm.AllowMatureLoras
                             ? "Adult content is included because \"Not for all audiences\" is shown in the model list; its previews are blurred."
                             : "Adult content is left out (tick \"Not for all audiences\" in the model list to include it).");
        CivitaiKeyBox.Text = vm.CivitaiToken;
        vm.PropertyChanged += OnVmChanged;
        Loaded += async (_, _) => await SearchAsync();
    }

    private void CivitaiKey_LostFocus(object sender, RoutedEventArgs e) => _vm.CivitaiToken = CivitaiKeyBox.Text;

    private void GetKey_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://civitai.com/user/account") { UseShellExecute = true });

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_current is null) return;
        if (e.PropertyName == nameof(MainViewModel.LoraDownloadPercent)) _current.Progress = _vm.LoraDownloadPercent;
        if (e.PropertyName == nameof(MainViewModel.LoraDownloadText) && _vm.LoraDownloadText.Length > 0)
            _current.Status = _vm.LoraDownloadText.Replace("Downloading " + (_current.Result.Candidate?.FileName ?? "\0") + ": ", "Downloading: ");
    }

    // ---------------------------------------------------------------- search

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void More_Click(object sender, RoutedEventArgs e) => await SearchAsync(more: true);

    public async Task SearchAsync(bool more = false)
    {
        var (hf, civitai) = _vm.LoraSearchBases();
        if (hf.Count == 0 && civitai.Count == 0)
        {
            StatusText.Text = "ImageMango doesn't know which LoRAs fit this model, so it can't search for them.";
            return;
        }
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        if (!more)
        {
            _lastSearch = (QueryBox.Text, (LoraSort)SortBox.SelectedIndex);
            _civitaiCursor = null;
            _items.Clear();
        }
        bool useCivitai = SourceBox.SelectedIndex is 0 or 1 && civitai.Count > 0;
        bool useHf = !more && SourceBox.SelectedIndex is 0 or 2 && hf.Count > 0;
        SetBusy(true, "Searching…");
        try
        {
            var civTask = useCivitai
                ? LoraSearch.CivitaiAsync(civitai, _vm.SelectedFamily, _lastSearch.Query, _lastSearch.Sort, _vm.AllowMatureLoras, _civitaiCursor, ct)
                : Task.FromResult((new List<LoraSearchResult>(), (string?)null));
            var hfTask = useHf
                ? LoraSearch.HuggingFaceAsync(hf, _vm.SelectedFamily, _lastSearch.Query, _lastSearch.Sort, _vm.AllowMatureLoras, _vm.HfTokenForSearch, ct)
                : Task.FromResult(new List<LoraSearchResult>());
            string? civError = null;
            List<LoraSearchResult> civ;
            try { (civ, _civitaiCursor) = await civTask; }
            catch (Exception ex) when (ex is not OperationCanceledException) { civ = new(); _civitaiCursor = null; civError = ex.Message; }
            var hfResults = await hfTask;

            // Alternate the two sites so both show near the top.
            var seen = _items.Select(i => i.Result.PageUrl).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int added = 0;
            for (int i = 0; i < Math.Max(civ.Count, hfResults.Count); i++)
                foreach (var r in new[] { i < civ.Count ? civ[i] : null, i < hfResults.Count ? hfResults[i] : null })
                    if (r is not null && seen.Add(r.PageUrl))
                    {
                        var item = new LoraResultItem(r, _vm.IsLoraDownloaded(r)) { BlurThumb = r.ImageMature };
                        _items.Add(item);
                        added++;
                        _ = LoadThumbAsync(item, ct);
                    }
            MoreButton.Visibility = _civitaiCursor is not null && useCivitai ? Visibility.Visible : Visibility.Collapsed;
            SetBusy(false, _items.Count == 0 ? "Nothing found. Try other words, or another sort or site."
                : $"{_items.Count} LoRAs" + (more ? $" ({added} more)" : "") + (civError is null ? "." : $". Civitai didn't answer: {civError}"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetBusy(false, "Search failed: " + ex.Message);
        }
    }

    /// <summary>Loads a preview (at most 6 at once). With the NSFW classifier on, flagged previews are blurred.</summary>
    private async Task LoadThumbAsync(LoraResultItem item, CancellationToken ct)
    {
        if (item.Result.ImageUrl is not { } url) return;
        await _thumbGate.WaitAsync(ct);
        try
        {
            var bytes = await Downloader.GetBytesAsync(url, url.Contains("huggingface.co") ? _vm.HfTokenForSearch : null, 15_000_000, ct);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 224;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            if (!item.BlurThumb && await _vm.IsPictureFlaggedAsync(bmp)) item.BlurThumb = true;
            item.Thumb = bmp;
        }
        catch { /* no preview: the placeholder stays */ }
        finally
        {
            _thumbGate.Release();
        }
    }

    private void OpenPage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LoraResultItem item)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Result.PageUrl) { UseShellExecute = true });
    }

    private void Tick_Changed(object sender, RoutedEventArgs e) => UpdateDownloadButton();

    private void UpdateDownloadButton()
    {
        int n = _items.Count(i => i.IsChecked);
        DownloadButton.IsEnabled = n > 0 && !_downloading;
        DownloadLabel.Text = n == 0 ? "Download ticked" : $"Download {n}";
    }

    private void SetBusy(bool busy, string status)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        StatusText.Text = status;
    }

    // ---------------------------------------------------------------- downloads

    private async void Download_Click(object sender, RoutedEventArgs e) => await DownloadTickedAsync();

    /// <summary>Downloads the ticked LoRAs in order. A failure is shown on its row and the queue goes on.</summary>
    public async Task DownloadTickedAsync()
    {
        var queue = _items.Where(i => i.IsChecked && !i.IsDownloaded).ToList();
        if (queue.Count == 0 || _downloading) return;
        _downloading = true;
        _queueCts = new CancellationTokenSource();
        StopButton.Visibility = Visibility.Visible;
        UpdateDownloadButton();
        foreach (var i in queue) i.Status = "Waiting";
        int done = 0, failed = 0;
        try
        {
            foreach (var item in queue)
            {
                if (_queueCts.IsCancellationRequested) { item.Status = ""; continue; }
                SetBusy(true, $"Downloading {done + failed + 1} of {queue.Count}: {item.Name}");
                Results.ScrollIntoView(item);
                var error = await DownloadOneAsync(item, _queueCts.Token);
                if (error is null)
                {
                    done++;
                    item.IsDownloaded = true;
                    item.Status = "Downloaded and switched on ✓";
                }
                else if (error.Length > 0)
                {
                    failed++;
                    item.Status = error;
                    item.IsChecked = false;
                }
                else item.Status = "";
            }
            SetBusy(false, _queueCts.IsCancellationRequested ? $"Stopped. {done} downloaded."
                : failed == 0 ? $"{done} LoRA{(done == 1 ? "" : "s")} downloaded and switched on. Close this window and Generate."
                : $"{done} downloaded, {failed} failed (see the rows).");
        }
        finally
        {
            _current = null;
            _downloading = false;
            StopButton.Visibility = Visibility.Collapsed;
            UpdateDownloadButton();
        }
    }

    /// <summary>Null when it worked, "" when skipped by the user, else the reason it failed.</summary>
    private async Task<string?> DownloadOneAsync(LoraResultItem item, CancellationToken ct)
    {
        var candidate = item.Result.Candidate;
        if (candidate is null)
        {
            item.Status = "Looking at the files…";
            IReadOnlyList<LoraCandidate> files;
            try { files = await LoraSources.ResolveAsync(item.Result.PageUrl, _vm.HfTokenForSearch, ct); }
            catch (OperationCanceledException) { return ""; }
            catch (Exception ex) { return "Couldn't read the page: " + ex.Message; }
            candidate = MainFile(files);
            if (candidate is null)
            {
                if (_selfTest) return "Skipped: several files to choose from.";
                candidate = Dialogs.Choose(this, "Download a LoRA", $"{item.Name} has several files. Pick the one to download:", files, c => c.Label);
            }
            if (candidate is null) return "";
        }
        if (candidate.SizeBytes > 2_500_000_000)
            return $"Not downloaded: {candidate.SizeBytes / 1e9:0.0} GB is too big for a LoRA; it's probably a full model.";
        if (LoraFamilies.Fit(candidate.Family, _vm.SelectedFamily) == LoraFit.Mismatch)
            return $"Not downloaded: it's made for {LoraFamilies.NameOf(candidate.Family)} models.";

        _current = item;
        item.IsDownloading = true;
        item.Progress = 0;
        item.Status = "Downloading…";
        using var reg = ct.Register(() => Dispatcher.Invoke(() => _vm.CancelLoraDownloadCommand.Execute(null)));
        try
        {
            var error = await _vm.DownloadLoraAsync(candidate, showErrors: false);
            return error == "Cancelled." ? "" : error;
        }
        finally
        {
            item.IsDownloading = false;
            _current = null;
        }
    }

    /// <summary>
    /// The LoRA in a Hugging Face repo with several .safetensors files, when it's clear which one: the only one
    /// outside subfolders, else the only one named like a LoRA (not an embedding). Null when it's a real choice.
    /// </summary>
    private static LoraCandidate? MainFile(IReadOnlyList<LoraCandidate> files)
    {
        if (files.Count == 1) return files[0];
        var notEmbeddings = files.Where(f => !f.FileName.Contains("emb", StringComparison.OrdinalIgnoreCase) &&
                                             !f.FileName.Contains("pivotal", StringComparison.OrdinalIgnoreCase)).ToList();
        if (notEmbeddings.Count == 1) return notEmbeddings[0];
        var atRoot = notEmbeddings.Where(f => !f.DownloadUrl.Contains("/resolve/main/") ||
                                              !f.DownloadUrl[(f.DownloadUrl.IndexOf("/resolve/main/", StringComparison.Ordinal) + 14)..].Contains('/')).ToList();
        if (atRoot.Count == 1) return atRoot[0];
        var named = atRoot.Where(f => f.FileName.Contains("lora", StringComparison.OrdinalIgnoreCase)).ToList();
        return named.Count == 1 ? named[0] : null;
    }

    private bool _selfTest;

    private void Stop_Click(object sender, RoutedEventArgs e) => _queueCts?.Cancel();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_downloading && MessageBox.Show(this, "LoRAs are still downloading. Stop and close?", "Find LoRAs",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _queueCts?.Cancel();
        _searchCts?.Cancel();
        if (CivitaiKeyBox.Text.Trim() != _vm.CivitaiToken) _vm.CivitaiToken = CivitaiKeyBox.Text;
        _vm.PropertyChanged -= OnVmChanged;
        base.OnClosing(e);
    }

    // ---------------------------------------------------------------- self-test

    /// <summary>
    /// "--find-loras [query]": opens the browser off-screen, searches, waits for previews and saves a picture of it.
    /// With "--download-loras N" it then ticks the first N small (under 300 MB) results, downloads them and saves a
    /// second picture.
    /// </summary>
    public static async Task SelfTestAsync(Window owner, MainViewModel vm, string query, string shotPath, int download)
    {
        var w = new LoraBrowserWindow(vm) { Owner = owner, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, _selfTest = true };
        w.QueryBox.Text = query;
        w.Show();
        try
        {
            await Task.Delay(1500);
            do await Task.Delay(500); while (w.Busy.Visibility == Visibility.Visible);
            await Task.Delay(4000); // previews
            await w.Dispatcher.InvokeAsync(() => MainWindow.RenderToPng(w, shotPath + ".loras.png"), DispatcherPriority.ApplicationIdle);
            if (download > 0)
            {
                // Both sites: small Civitai files and Hugging Face repos (sizes known only after resolving).
                foreach (var item in w._items.Where(i => !i.IsDownloaded && (i.Result.SizeBytes is < 300_000_000 || i.Result.Source == "Hugging Face")).Take(download))
                    item.IsChecked = true;
                using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var stop = limit.Token.Register(() => w.Dispatcher.Invoke(() => w._queueCts?.Cancel()));
                await w.DownloadTickedAsync();
                await w.Dispatcher.InvokeAsync(() => MainWindow.RenderToPng(w, shotPath + ".loras-downloaded.png"), DispatcherPriority.ApplicationIdle);
            }
        }
        finally
        {
            w.Close();
        }
    }
}
