using System.Windows;
using System.Windows.Threading;

namespace ImageMango;

public partial class App : Application
{
    /// <summary>Set by "--screenshot file.png": render the main window to a PNG and exit.</summary>
    public static string? ScreenshotPath { get; private set; }

    /// <summary>With --screenshot: "--prompt text" generates one image with the last-used model first (self-test).</summary>
    public static string? ScreenshotPrompt { get; private set; }

    /// <summary>Self-test: "--init-image file" starts from that picture, "--strength 0.6" sets how much it changes.</summary>
    public static string? SelfTestInitImage { get; private set; }
    public static double? SelfTestStrength { get; private set; }

    /// <summary>Self-test: "--lora file.safetensors[:weight]", repeatable; the files are added to the LoRA folder and switched on.</summary>
    public static List<string> SelfTestLoras { get; } = new();

    /// <summary>Self-test: "--reuse image.png" puts back that image's prompt and settings (from its .json or the PNG text).</summary>
    public static string? SelfTestReuse { get; private set; }

    /// <summary>Self-test: "--helper text" runs the prompt helper on that text and saves pictures of its window.</summary>
    public static string? SelfTestHelper { get; private set; }

    /// <summary>Self-test: "--find-loras [words]" opens the LoRA browser, searches and saves a picture of it.</summary>
    public static string? SelfTestFindLoras { get; private set; }
    public static int SelfTestDownloadLoras { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        int i = Array.IndexOf(e.Args, "--screenshot");
        if (i >= 0 && i + 1 < e.Args.Length) ScreenshotPath = System.IO.Path.GetFullPath(e.Args[i + 1]);
        int p = Array.IndexOf(e.Args, "--prompt");
        if (p >= 0 && p + 1 < e.Args.Length) ScreenshotPrompt = e.Args[p + 1];
        int ii = Array.IndexOf(e.Args, "--init-image");
        if (ii >= 0 && ii + 1 < e.Args.Length) SelfTestInitImage = e.Args[ii + 1];
        int st = Array.IndexOf(e.Args, "--strength");
        if (st >= 0 && st + 1 < e.Args.Length && double.TryParse(e.Args[st + 1], System.Globalization.CultureInfo.InvariantCulture, out var sv)) SelfTestStrength = sv;
        int ru = Array.IndexOf(e.Args, "--reuse");
        if (ru >= 0 && ru + 1 < e.Args.Length) SelfTestReuse = e.Args[ru + 1];
        int he = Array.IndexOf(e.Args, "--helper");
        if (he >= 0 && he + 1 < e.Args.Length) SelfTestHelper = e.Args[he + 1];
        int dl = Array.IndexOf(e.Args, "--download-loras");
        if (dl >= 0 && dl + 1 < e.Args.Length && int.TryParse(e.Args[dl + 1], out var dn)) SelfTestDownloadLoras = dn;
        int fl = Array.IndexOf(e.Args, "--find-loras");
        if (fl >= 0) SelfTestFindLoras = fl + 1 < e.Args.Length && !e.Args[fl + 1].StartsWith("--") ? e.Args[fl + 1] : "";
        for (int k = 0; k + 1 < e.Args.Length; k++)
            if (e.Args[k] == "--lora") SelfTestLoras.Add(e.Args[k + 1]);

        // ggml discovers its backend DLLs (ggml-cuda.dll, ggml-cpu-*.dll) next to the exe or in the
        // current directory; make sure the latter is the app folder even when started from a shortcut.
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        // Windows 11 Fluent look, following the system light/dark setting.
        ThemeMode = ThemeMode.System;

        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Self-test runs are off-screen: write errors next to the screenshot instead of a dialog on the desktop.
        if (ScreenshotPath is { } shot)
        {
            try { System.IO.File.AppendAllText(shot + ".errors.txt", $"{DateTime.Now:HH:mm:ss} {e.Exception}\n"); } catch { }
            e.Handled = true;
            return;
        }
        MessageBox.Show(e.Exception.Message, "ImageMango — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
