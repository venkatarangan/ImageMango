using System.IO;

namespace ImageMango.Core;

/// <summary>
/// All locations the app reads and writes.
///   %LOCALAPPDATA%\ImageMango\config\settings.json   user settings + per-model overrides
///   %LOCALAPPDATA%\ImageMango\config\machine.json    detected hardware (edit to simulate another PC)
///   %LOCALAPPDATA%\ImageMango\config\models.json     optional catalog override
///   %LOCALAPPDATA%\ImageMango\models\                 default model download folder (LoRAs in models\loras)
///   %TEMP%\ImageMango\                                generated images (changeable in the app)
/// The app was called LocalImageGen until 2026-10-16; its folders are moved over on first start.
/// </summary>
public static class AppPaths
{
    public const string AppName = "ImageMango";
    private const string OldAppName = "LocalImageGen";

    /// <summary>Set IMAGEMANGO_HOME (formerly LOCALIMAGEGEN_HOME) to keep config, models and images in one portable folder.</summary>
    private static readonly string? HomeOverride =
        NonEmpty(Environment.GetEnvironmentVariable("IMAGEMANGO_HOME")) ?? NonEmpty(Environment.GetEnvironmentVariable("LOCALIMAGEGEN_HOME"));

    /// <summary>True when IMAGEMANGO_HOME / LOCALIMAGEGEN_HOME points the app at a portable folder.</summary>
    public static bool IsPortable => HomeOverride is not null;

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>%LOCALAPPDATA%\LocalImageGen, the data folder before the rename.</summary>
    public static string OldDataRoot => Path.Combine(LocalAppData, OldAppName);

    public static string DataRoot { get; } = ResolveDataRoot();

    /// <summary>Set when the old LocalImageGen folder was moved to the new name at this start.</summary>
    public static bool MigratedFromOldName { get; private set; }

    public static string ConfigDir => Path.Combine(DataRoot, "config");
    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public static string MachineFile => Path.Combine(ConfigDir, "machine.json");
    public static string CatalogOverrideFile => Path.Combine(ConfigDir, "models.json");
    public static string BundledCatalogFile => Path.Combine(AppContext.BaseDirectory, "catalog", "models.json");
    public static string DefaultModelsDir => Path.Combine(DataRoot, "models");
    public static string ToolsDir => Path.Combine(DataRoot, "tools");
    public static string LogFile => Path.Combine(DataRoot, "engine.log");

    /// <summary>LoRA files live in a "loras" folder inside the models folder, so they move with it.</summary>
    public static string LorasDirFor(string modelsDir) => Path.Combine(modelsDir, "loras");

    /// <summary>
    /// %TEMP%\ImageMango. Pictures is often redirected into OneDrive or guarded by Controlled Folder Access,
    /// where writes fail, so the default is the Windows temp folder; Settings › Storage › Change… moves it.
    /// </summary>
    public static string DefaultOutputDir => HomeOverride is null
        ? Path.Combine(Path.GetTempPath(), AppName)
        : Path.Combine(DataRoot, "output");

    /// <summary>%TEMP%\LocalImageGen, the default output folder before the rename.</summary>
    public static string OldDefaultOutputDir => Path.Combine(Path.GetTempPath(), OldAppName);

    /// <summary>The default before 2026-10-15; settings still pointing here are moved to <see cref="DefaultOutputDir"/>.</summary>
    public static string LegacyOutputDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), OldAppName);

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(ToolsDir);
    }

    /// <summary>
    /// The data folder, after moving %LOCALAPPDATA%\LocalImageGen to %LOCALAPPDATA%\ImageMango the first time
    /// (a rename on the same drive, so downloaded models aren't copied). If the move fails, for example because
    /// another copy of the old app is running, the old folder keeps being used.
    /// </summary>
    private static string ResolveDataRoot()
    {
        if (HomeOverride is not null) return Path.GetFullPath(HomeOverride);
        var root = Path.Combine(LocalAppData, AppName);
        var old = OldDataRoot;
        if (Directory.Exists(root) || !Directory.Exists(old)) return root;
        try
        {
            Directory.Move(old, root);
            MigratedFromOldName = true;
            return root;
        }
        catch
        {
            return old;
        }
    }

    /// <summary>Rewrites a path saved under an old folder (<paramref name="from"/>) to the same place under <paramref name="to"/>.</summary>
    public static string Rebase(string path, string from, string to)
    {
        if (string.IsNullOrEmpty(path) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return path;
        var f = from.TrimEnd('\\', '/');
        if (string.Equals(path.TrimEnd('\\', '/'), f, StringComparison.OrdinalIgnoreCase)) return to;
        if (path.Length > f.Length && path.StartsWith(f, StringComparison.OrdinalIgnoreCase) && path[f.Length] is '\\' or '/')
            return Path.Combine(to, path[(f.Length + 1)..]);
        return path;
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
