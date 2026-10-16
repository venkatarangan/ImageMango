using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMango.Core;
using ImageMango.Native;

namespace ImageMango.UI;

public static class ImageStore
{
    public static BitmapSource ToBitmap(RawImage img)
    {
        var format = img.Channels switch
        {
            4 => PixelFormats.Bgra32,
            1 => PixelFormats.Gray8,
            _ => PixelFormats.Rgb24,
        };
        var pixels = img.Pixels;
        if (img.Channels == 4)
        {
            // Engine gives RGBA; WPF wants BGRA.
            pixels = (byte[])pixels.Clone();
            for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
        var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, format, null, pixels, img.Width * img.Channels);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// File names the app gives its images and sidecars: yyyyMMdd-HHmmss-{model}-{seed}[-n].png/.json.
    /// "Clear folder" deletes only files matching this, never anything else in the folder.
    /// </summary>
    public static readonly System.Text.RegularExpressions.Regex OwnFileName =
        new(@"^\d{8}-\d{6}-.+-\d+(-\d+)?\.(png|json)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Saves a PNG plus, unless <paramref name="metadata"/> is null, a .json sidecar with everything needed to reproduce the image.
    /// <paramref name="description"/>, when given, is written inside the PNG (see <see cref="PngMetadata"/>).
    /// </summary>
    public static string Save(BitmapSource bmp, string folder, string modelId, long seed, object? metadata, ImageDescription? description = null)
    {
        Directory.CreateDirectory(folder);
        var stem = $"{DateTime.Now:yyyyMMdd-HHmmss}-{modelId}-{seed}";
        var path = Path.Combine(folder, stem + ".png");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{stem}-{i}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        var bytes = ms.ToArray();
        if (description is not null) bytes = PngMetadata.Embed(bytes, description);
        File.WriteAllBytes(path, bytes);
        if (metadata is not null) Json.Save(Path.ChangeExtension(path, ".json"), metadata);
        return path;
    }

    public static BitmapSource? LoadThumbnail(string path, int width = 160)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = width;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>File types offered when picking a starting image.</summary>
    public const string PictureFilter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff|All files|*.*";

    public static bool IsPictureFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".tif" or ".tiff";

    /// <summary>
    /// Loads a starting picture into memory: turned upright using the camera's EXIF orientation and scaled so the
    /// long side is at most 2048 px. Kept in memory, so the file can go away (incognito images do).
    /// </summary>
    public static BitmapSource LoadPicture(string path)
    {
        BitmapSource src;
        ushort orientation = 1;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            try
            {
                if (frame.Metadata is BitmapMetadata md && md.ContainsQuery("System.Photo.Orientation") &&
                    md.GetQuery("System.Photo.Orientation") is ushort o)
                    orientation = o;
            }
            catch { /* formats without metadata support */ }
            src = frame;
        }
        double angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        if (angle != 0) src = new TransformedBitmap(src, new RotateTransform(angle));
        double scale = Math.Min(1.0, 2048.0 / Math.Max(src.PixelWidth, src.PixelHeight));
        if (scale < 1) src = new TransformedBitmap(src, new ScaleTransform(scale, scale));
        return Freeze(src);
    }

    /// <summary>Copies a frozen, self-contained bitmap (pixels decoded now, not on first draw).</summary>
    public static BitmapSource Freeze(BitmapSource src)
    {
        var copy = new WriteableBitmap(new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0));
        copy.Freeze();
        return copy;
    }

    /// <summary>
    /// The starting image for the engine: scaled to cover <paramref name="width"/>×<paramref name="height"/>, centre-cropped
    /// (never stretched) and laid on white (transparent areas would otherwise turn black). Must run on the UI thread.
    /// </summary>
    public static RawImage ToRawImage(BitmapSource src, int width, int height)
    {
        double scale = Math.Max((double)width / src.PixelWidth, (double)height / src.PixelHeight);
        double w = src.PixelWidth * scale, h = src.PixelHeight * scale;
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, width, height));
            dc.DrawImage(src, new System.Windows.Rect((width - w) / 2, (height - h) / 2, w, h));
        }
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var bgra = new byte[width * height * 4];
        rtb.CopyPixels(bgra, width * 4, 0);
        var rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
        {
            rgb[j] = bgra[i + 2];
            rgb[j + 1] = bgra[i + 1];
            rgb[j + 2] = bgra[i];
        }
        return new RawImage { Width = width, Height = height, Channels = 3, Pixels = rgb };
    }

    public static BitmapSource? LoadFull(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }
}
