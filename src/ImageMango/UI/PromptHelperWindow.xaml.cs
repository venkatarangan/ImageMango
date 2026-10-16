using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ImageMango.Core;

namespace ImageMango.UI;

/// <summary>
/// Prompt helper: improves the user's prompt or writes one from an idea for the selected image model, optionally
/// asking a few questions first. The conversation lives only in this window; nothing is saved or logged.
/// </summary>
public partial class PromptHelperWindow : Window
{
    private readonly MainViewModel _vm;
    private PromptHelperChat? _chat;
    private HelperAnswer? _asked;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public PromptHelperWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ModelBox.ItemsSource = HelperModels.All;
        ModelBox.SelectedItem = vm.HelperModel;
        AskFirst.IsChecked = vm.HelperAskFirst;
        InputBox.Text = vm.Prompt;
        if (string.IsNullOrWhiteSpace(vm.Prompt)) IdeaMode.IsChecked = true;
        vm.PropertyChanged += OnVmChanged;
        Refresh();
        Loaded += (_, _) => { InputBox.Focus(); InputBox.CaretIndex = InputBox.Text.Length; };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Selected)) Refresh();
    }

    /// <summary>Header, download card and button states for the selected image model and helper model.</summary>
    private void Refresh()
    {
        var model = _vm.HelperModel;
        ForModel.Text = _vm.Selected is { } m ? $"Prompt helper for {m.Name}" : "Prompt helper";
        var (parts, gb) = _vm.HelperDownloadsNeeded(model);
        ModelState.Text = parts.Count == 0 ? "Ready" : _vm.IsHelperModelReady(model) ? "Engine not installed" : $"Not downloaded ({model.File.SizeGb:0.0} GB)";
        SetupCard.Visibility = parts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (parts.Count > 0)
            SetupText.Text = $"The helper needs a one-time download of {string.Join(" and ", parts)}, {gb:0.0} GB in all. " +
                             "The model is saved in your models folder; image models that use the same file share it.";
        DownloadButton.IsEnabled = !_busy;
        ModelBox.IsEnabled = !_busy;
        SuggestButton.IsEnabled = !_busy && parts.Count == 0 && _vm.Selected is not null;
        ContinueButton.IsEnabled = SkipButton.IsEnabled = !_busy;
        UseButton.IsEnabled = !_busy && ResultPanel.Visibility == Visibility.Visible && ResultPrompt.Text.Trim().Length > 0;
        SuggestLabel.Text = ImproveMode.IsChecked == true ? "Improve" : "Write it";
    }

    private void SetBusy(bool busy, string status = "")
    {
        _busy = busy;
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = status;
        Refresh();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (InputLabel is null) return; // during InitializeComponent
        bool improve = ImproveMode.IsChecked == true;
        InputLabel.Text = improve ? "Your prompt" : "Your idea, in a few words or sentences";
        if (!improve && InputBox.Text == _vm.Prompt) InputBox.Text = "";
        else if (improve && InputBox.Text.Length == 0) InputBox.Text = _vm.Prompt;
        Refresh();
    }

    private void ModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelBox.SelectedItem is HelperModel m && _vm is not null)
        {
            _vm.HelperModel = m;
            Refresh();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && SuggestButton.IsEnabled)
        {
            e.Handled = true;
            _ = SuggestAsync();
        }
        else if (e.Key == Key.Escape && _busy)
        {
            e.Handled = true;
            _cts?.Cancel();
        }
    }

    // ---------------------------------------------------------------- downloads

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var model = _vm.HelperModel;
        _cts = new CancellationTokenSource();
        SetBusy(true, "Downloading…");
        var status = new Progress<string>(s => SetupProgress.Text = s);
        try
        {
            await _vm.InstallHelperAsync(model, status, _cts.Token);
            SetupProgress.Text = "";
            SetBusy(false, $"{model.Name} is ready.");
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Download paused. Click Download to resume.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Download failed.");
            MessageBox.Show(this, ex.Message, "Prompt helper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------------------------------------------------------- asking

    private void Suggest_Click(object sender, RoutedEventArgs e) => _ = SuggestAsync();

    /// <summary>Starts a new conversation from the box: improve the prompt or write one from the idea.</summary>
    public async Task SuggestAsync()
    {
        if (_vm.Selected is not { } selected || _busy) return;
        var text = InputBox.Text.Trim();
        if (text.Length == 0)
        {
            StatusText.Text = ImproveMode.IsChecked == true ? "Type a prompt to improve first." : "Type an idea first.";
            return;
        }
        bool mayAsk = AskFirst.IsChecked == true;
        _vm.HelperAskFirst = mayAsk;
        _chat = new PromptHelperChat(selected.Model, _vm.CurrentSettings.CfgScale);
        _chat.Begin(ImproveMode.IsChecked == true ? HelperTask.Improve : HelperTask.FromIdea, text, _vm.CurrentSettings.NegativePrompt, mayAsk);
        await RunAsync(mayAsk);
    }

    private async Task RunAsync(bool allowQuestions)
    {
        if (_chat is null) return;
        _cts = new CancellationTokenSource();
        QuestionsCard.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        SetBusy(true, "Starting…");
        var status = new Progress<string>(s => { if (_busy) StatusText.Text = s; });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var answer = await _vm.AskHelperAsync(_chat, allowQuestions, status, _cts.Token);
            if (allowQuestions && answer.IsQuestion) ShowQuestions(answer);
            else ShowResult(answer);
            SetBusy(false, answer.IsQuestion && allowQuestions ? "" : $"Done in {clock.Elapsed.TotalSeconds:0.0} s.");
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Cancelled.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "The helper couldn't answer.");
            MessageBox.Show(this, ex.Message, "Prompt helper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowQuestions(HelperAnswer asked)
    {
        _asked = asked;
        QuestionsPanel.Children.Clear();
        foreach (var q in asked.Questions)
        {
            QuestionsPanel.Children.Add(new TextBlock { Text = q, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 3) });
            QuestionsPanel.Children.Add(new TextBox());
        }
        QuestionsCard.Visibility = Visibility.Visible;
        if (QuestionsPanel.Children.OfType<TextBox>().FirstOrDefault() is { } first) first.Focus();
    }

    private void ShowResult(HelperAnswer a)
    {
        ResultPrompt.Text = a.Prompt;
        ResultNegative.Text = a.NegativePrompt;
        NegativeArea.Visibility = _chat?.UsesNegative == true ? Visibility.Visible : Visibility.Collapsed;
        ResultNotes.Text = a.Notes;
        ResultNotes.Visibility = a.Notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_chat is null || _asked is null) return;
        _chat.Answer(_asked, QuestionsPanel.Children.OfType<TextBox>().Select(t => t.Text).ToList());
        _asked = null;
        _ = RunAsync(false);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_chat is null || _asked is null) return;
        _chat.SkipQuestions(_asked);
        _asked = null;
        _ = RunAsync(false);
    }

    /// <summary>Same request again; a new random seed gives a different suggestion.</summary>
    private void Again_Click(object sender, RoutedEventArgs e) => _ = RunAsync(false);

    private void Refine_Click(object sender, RoutedEventArgs e)
    {
        ImproveMode.IsChecked = true;
        InputBox.Text = ResultPrompt.Text;
        ResultPanel.Visibility = Visibility.Collapsed;
        InputBox.Focus();
        Refresh();
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        var prompt = ResultPrompt.Text.Trim();
        if (prompt.Length == 0) return;
        _vm.Prompt = prompt;
        if (_chat?.UsesNegative == true && ResultNegative.Text.Trim() is { Length: > 0 } neg) _vm.CurrentSettings.NegativePrompt = neg;
        _vm.Status = "The helper's prompt is in the prompt box. Press Generate (Ctrl+Enter).";
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        _cts?.Cancel();
        _vm.PropertyChanged -= OnVmChanged;
        base.OnClosing(e);
    }

    // ---------------------------------------------------------------- self-test

    /// <summary>
    /// "--helper text": opens the helper off-screen, improves <paramref name="text"/>, answers any questions with
    /// "you choose", and saves pictures of the window at each step next to the screenshot (or of the download card,
    /// if something isn't downloaded yet).
    /// </summary>
    public static async Task SelfTestAsync(Window owner, MainViewModel vm, string text, string shotPath)
    {
        var w = new PromptHelperWindow(vm)
        {
            Owner = owner, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0,
        };
        w.Show();
        try
        {
            w.ImproveMode.IsChecked = true;
            w.InputBox.Text = text;
            // Never starts a multi-GB download by itself: shows what it would download and stops.
            if (w.SetupCard.Visibility == Visibility.Visible)
            {
                await Render(w, shotPath + ".helper-setup.png");
                return;
            }
            await w.SuggestAsync();
            if (w.QuestionsCard.Visibility == Visibility.Visible)
            {
                await Render(w, shotPath + ".helper-questions.png");
                w.Skip_Click(w, new RoutedEventArgs());
                do await Task.Delay(300); while (w._busy);
            }
            await Render(w, shotPath + ".helper.png");
        }
        finally
        {
            w.Close();
        }
    }

    private static async Task Render(Window w, string path) =>
        await w.Dispatcher.InvokeAsync(() => MainWindow.RenderToPng(w, path), DispatcherPriority.ApplicationIdle);
}
