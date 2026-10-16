using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ImageMango.Core;

/// <summary>What gets written into a generated PNG so it can be identified and reproduced later.</summary>
public sealed class ImageDescription
{
    public string Prompt { get; init; } = "";
    public string NegativePrompt { get; init; } = "";
    public string ModelName { get; init; } = "";
    /// <summary>"Steps: 28, Sampler: euler_a, …" pairs, in order (A1111 style, also shown in Explorer's Comments).</summary>
    public IReadOnlyList<(string Key, string Value)> Settings { get; init; } = Array.Empty<(string, string)>();
    /// <summary>Explorer tags / XMP keywords (the model name, so Windows Search finds images by model).</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string Software { get; init; } = "ImageMango";
}

/// <summary>
/// Text metadata inside PNG files, written so each OS shows it:
/// <list type="bullet">
/// <item>XMP (iTXt "XML:com.adobe.xmp"): Windows Explorer's Title, Comments, Tags and Program name; macOS and
/// Linux photo tools (Finder/Preview via ImageIO, digiKam, gThumb, exiftool) read it too.</item>
/// <item>PNG text chunks (Title, Description, Comment, Software, Source, Creation Time): GIMP, ImageMagick, exiftool,
/// macOS Preview's PNG tab. Explorer ignores these.</item>
/// <item>"parameters" in the AUTOMATIC1111 format: read by A1111/Forge "PNG Info", Civitai uploads, ComfyUI and
/// SD prompt readers.</item>
/// </list>
/// </summary>
public static class PngMetadata
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private const string XmpKeyword = "XML:com.adobe.xmp";
    private const int TitleMax = 200;

    /// <summary>Returns <paramref name="png"/> with the description added as text chunks right after IHDR.</summary>
    public static byte[] Embed(byte[] png, ImageDescription d)
    {
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("Not a PNG file.");
        // IHDR is always first: 8 signature + 4 length + 4 type + 13 data + 4 CRC.
        const int afterIhdr = 33;

        var parameters = Parameters(d);
        var comment = Comment(d);
        var title = Shorten(d.Prompt, TitleMax);
        using var ms = new MemoryStream(png.Length + 8192);
        ms.Write(png, 0, afterIhdr);
        WriteText(ms, "Title", title);
        WriteText(ms, "Description", d.Prompt);
        WriteText(ms, "Comment", comment);
        WriteText(ms, "Software", d.Software);
        if (d.ModelName.Length > 0) WriteText(ms, "Source", d.ModelName);
        WriteText(ms, "Creation Time", d.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
        WriteText(ms, "parameters", parameters);
        WriteChunk(ms, "iTXt", InternationalText(XmpKeyword, Xmp(d, title, comment)));
        ms.Write(png, afterIhdr, png.Length - afterIhdr);
        return ms.ToArray();
    }

    /// <summary>
    /// The AUTOMATIC1111 "parameters" text: prompt, "Negative prompt: …", then one line of "Key: value" pairs.
    /// </summary>
    public static string Parameters(ImageDescription d)
    {
        var sb = new StringBuilder(d.Prompt.Trim());
        if (!string.IsNullOrWhiteSpace(d.NegativePrompt)) sb.Append("\nNegative prompt: ").Append(d.NegativePrompt.Trim());
        sb.Append('\n').Append(string.Join(", ", d.Settings.Select(s => $"{s.Key}: {QuoteValue(s.Value)}")));
        return sb.ToString();
    }

    /// <summary>A readable summary for Explorer's Comments field.</summary>
    public static string Comment(ImageDescription d)
    {
        var sb = new StringBuilder();
        sb.Append("Prompt: ").Append(d.Prompt.Trim());
        if (!string.IsNullOrWhiteSpace(d.NegativePrompt)) sb.Append("\nNegative prompt: ").Append(d.NegativePrompt.Trim());
        if (d.ModelName.Length > 0) sb.Append("\nModel: ").Append(d.ModelName);
        var rest = d.Settings.Where(s => s.Key != "Model").Select(s => $"{s.Key}: {s.Value}").ToList();
        if (rest.Count > 0) sb.Append('\n').Append(string.Join(", ", rest));
        sb.Append("\nMade with ").Append(d.Software);
        return sb.ToString();
    }

    /// <summary>A1111 quotes values that contain a comma, colon or quote (LoRA lists, for example).</summary>
    private static string QuoteValue(string v) =>
        v.IndexOfAny(new[] { ',', ':', '"', '\n' }) < 0 ? v
            : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    private static string Shorten(string text, int max)
    {
        var t = Regex.Replace(text.Trim(), @"\s+", " ");
        if (t.Length <= max) return t;
        int cut = t.LastIndexOf(' ', max - 1);
        return t[..(cut > max / 2 ? cut : max - 1)].TrimEnd(',', ' ') + "…";
    }

    private static string Xmp(ImageDescription d, string title, string comment)
    {
        string Alt(string s) => $"<rdf:Alt><rdf:li xml:lang=\"x-default\">{Escape(s)}</rdf:li></rdf:Alt>";
        var sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");
        sb.Append("<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"");
        sb.Append(" xmlns:exif=\"http://ns.adobe.com/exif/1.0/\" xmlns:Iptc4xmpExt=\"http://iptc.org/std/Iptc4xmpExt/2008-02-29/\">\n");
        sb.Append("<dc:title>").Append(Alt(title)).Append("</dc:title>\n");
        sb.Append("<dc:description>").Append(Alt(d.Prompt.Trim())).Append("</dc:description>\n");
        sb.Append("<exif:UserComment>").Append(Alt(comment)).Append("</exif:UserComment>\n");
        if (d.Tags.Count > 0)
            sb.Append("<dc:subject><rdf:Bag>").Append(string.Concat(d.Tags.Select(t => $"<rdf:li>{Escape(t)}</rdf:li>"))).Append("</rdf:Bag></dc:subject>\n");
        sb.Append("<xmp:CreatorTool>").Append(Escape(d.Software)).Append("</xmp:CreatorTool>\n");
        sb.Append("<xmp:CreateDate>").Append(d.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)).Append("</xmp:CreateDate>\n");
        // The IPTC marker for "made by a generative AI model".
        sb.Append("<Iptc4xmpExt:DigitalSourceType>http://cv.iptc.org/newscodes/digitalsourcetype/trainedAlgorithmicMedia</Iptc4xmpExt:DigitalSourceType>\n");
        sb.Append("</rdf:Description></rdf:RDF></x:xmpmeta>\n<?xpacket end=\"w\"?>");
        return sb.ToString();
    }

    /// <summary>XML-escapes and drops control characters XML doesn't allow.</summary>
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\t' or '\n' or '\r': sb.Append(c); break;
                default: if (c >= 0x20 && c != '￾' && c != '￿') sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>tEXt when the text fits Latin-1 (the widest support), otherwise iTXt (UTF-8, e.g. Tamil or emoji).</summary>
    private static void WriteText(Stream s, string keyword, string text)
    {
        text = text.Replace("\0", "");
        if (text.All(c => c <= 0xFF))
        {
            var data = new byte[keyword.Length + 1 + text.Length];
            Encoding.Latin1.GetBytes(keyword, data);
            Encoding.Latin1.GetBytes(text, data.AsSpan(keyword.Length + 1));
            WriteChunk(s, "tEXt", data);
        }
        else WriteChunk(s, "iTXt", InternationalText(keyword, text));
    }

    /// <summary>iTXt: keyword, NUL, not compressed, method 0, empty language tag, empty translated keyword, UTF-8 text.</summary>
    private static byte[] InternationalText(string keyword, string text)
    {
        var head = Encoding.Latin1.GetBytes(keyword);
        var body = Encoding.UTF8.GetBytes(text);
        var data = new byte[head.Length + 5 + body.Length];
        head.CopyTo(data, 0);
        body.CopyTo(data, head.Length + 5);
        return data;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)data.Length);
        s.Write(buf);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(buf, ~Crc(Crc(0xFFFFFFFFu, typeBytes), data));
        s.Write(buf);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>The PNG chunk CRC (CRC-32, ISO 3309), continued over <paramref name="bytes"/>.</summary>
    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    /// <summary>Every tEXt, zTXt and iTXt entry in a PNG (first one wins for repeated keywords). Empty if unreadable.</summary>
    public static Dictionary<string, string> ReadText(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var br = new BinaryReader(fs);
            if (!br.ReadBytes(8).AsSpan().SequenceEqual(Signature)) return result;
            while (fs.Position + 12 <= fs.Length)
            {
                int len = BinaryPrimitives.ReadInt32BigEndian(br.ReadBytes(4));
                var type = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (len < 0 || fs.Position + len + 4 > fs.Length || type == "IEND") break;
                // Text before the image data is what matters; skip pixel data without reading it.
                if (type is not ("tEXt" or "zTXt" or "iTXt"))
                {
                    fs.Seek(len + 4, SeekOrigin.Current);
                    continue;
                }
                var data = br.ReadBytes(len);
                fs.Seek(4, SeekOrigin.Current);
                if (Parse(type, data) is (string k, string v)) result.TryAdd(k, v);
            }
        }
        catch { /* missing, locked or damaged: no metadata */ }
        return result;
    }

    private static (string, string)? Parse(string type, byte[] data)
    {
        int nul = Array.IndexOf(data, (byte)0);
        if (nul <= 0) return null;
        var keyword = Encoding.Latin1.GetString(data, 0, nul);
        switch (type)
        {
            case "tEXt":
                return (keyword, Encoding.Latin1.GetString(data, nul + 1, data.Length - nul - 1));
            case "zTXt":
                return (keyword, Encoding.Latin1.GetString(Inflate(data, nul + 2)));
            default: // iTXt
                if (nul + 3 > data.Length) return null;
                bool compressed = data[nul + 1] == 1;
                int lang = Array.IndexOf(data, (byte)0, nul + 3);
                int trans = lang < 0 ? -1 : Array.IndexOf(data, (byte)0, lang + 1);
                if (trans < 0) return null;
                var text = compressed ? Inflate(data, trans + 1) : data[(trans + 1)..];
                return (keyword, Encoding.UTF8.GetString(text));
        }
    }

    private static byte[] Inflate(byte[] data, int offset)
    {
        using var z = new ZLibStream(new MemoryStream(data, offset, data.Length - offset), CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    // ---------------------------------------------------------------- reading prompts back

    /// <summary>Prompt, negative prompt and settings parsed from an A1111-style "parameters" text.</summary>
    public sealed record ParsedParameters(string Prompt, string NegativePrompt, IReadOnlyDictionary<string, string> Settings);

    private static readonly Regex ParamPair = new(@"\s*([\w ]+):\s*(""(?:\\.|[^\\""])+""|[^,]*)(?:,|$)");

    /// <summary>Parses "parameters" the way A1111's PNG Info does: last line of "Key: value" pairs, "Negative prompt:" before it.</summary>
    public static ParsedParameters ParseParameters(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (lines.Count > 1 && Regex.IsMatch(lines[^1], @"^\s*Steps:\s"))
        {
            foreach (Match m in ParamPair.Matches(lines[^1]))
            {
                var v = m.Groups[2].Value.Trim();
                if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = Regex.Unescape(v[1..^1]);
                settings[m.Groups[1].Value.Trim()] = v;
            }
            lines.RemoveAt(lines.Count - 1);
        }
        int neg = lines.FindIndex(l => l.StartsWith("Negative prompt:", StringComparison.Ordinal));
        string prompt, negative = "";
        if (neg >= 0)
        {
            prompt = string.Join("\n", lines.Take(neg));
            negative = string.Join("\n", lines.Skip(neg)).Substring("Negative prompt:".Length);
        }
        else prompt = string.Join("\n", lines);
        return new ParsedParameters(prompt.Trim(), negative.Trim(), settings);
    }
}
