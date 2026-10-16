using System.Globalization;
using System.Text.Json.Nodes;

namespace ImageMango.Core;

/// <summary>One LoRA found by a search on Hugging Face or Civitai.</summary>
public sealed class LoraSearchResult
{
    public required string Source { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = "";
    /// <summary>The page, as accepted by <see cref="LoraSources.ResolveAsync"/>.</summary>
    public required string PageUrl { get; init; }
    public string? ImageUrl { get; init; }
    /// <summary>The preview may be adult content (Civitai's rating), so it's shown blurred.</summary>
    public bool ImageMature { get; init; }
    public long Downloads { get; init; }
    public long Likes { get; init; }
    public string TriggerWords { get; init; } = "";
    public long? SizeBytes { get; init; }
    public string BaseModel { get; init; } = "";
    public string? Family { get; init; }
    public DateTime? Updated { get; init; }
    /// <summary>Civitai results already know their file; Hugging Face repos are resolved when downloaded.</summary>
    public LoraCandidate? Candidate { get; init; }
}

public enum LoraSort { Downloads, Rating, Newest }

/// <summary>
/// Searches Hugging Face (adapters of a base model) and Civitai (LoRAs of a base model) for one model family.
/// Adult content is left out unless <c>allowMature</c> is set: Hugging Face repos tagged not-for-all-audiences,
/// and Civitai models rated R or above (its nsfwLevel bit flags: 1 PG, 2 PG-13, 4 R, 8 X, 16 XXX).
/// </summary>
public static class LoraSearch
{
    /// <summary>Previews shown without "Not for all audiences": PG and PG-13 only.</summary>
    private const int CivitaiSafeLevels = 1 | 2;
    /// <summary>Models left out without "Not for all audiences": any X or XXX content. (A model's level combines all
    /// its gallery images, so R alone is common for ordinary style LoRAs.)</summary>
    private const int CivitaiExplicitLevels = 8 | 16;

    public static async Task<List<LoraSearchResult>> HuggingFaceAsync(IReadOnlyList<string> baseRepos, string? family, string query,
        LoraSort sort, bool allowMature, string? hfToken, CancellationToken ct)
    {
        string sortKey = sort switch { LoraSort.Rating => "likes", LoraSort.Newest => "lastModified", _ => "downloads" };
        var tasks = baseRepos.Take(8).Select(async repo =>
        {
            var url = $"https://huggingface.co/api/models?filter=base_model:adapter:{repo}&sort={sortKey}&direction=-1&limit=40" +
                      "&expand[]=downloads&expand[]=likes&expand[]=cardData&expand[]=tags&expand[]=lastModified&expand[]=author" +
                      (string.IsNullOrWhiteSpace(query) ? "" : "&search=" + Uri.EscapeDataString(query.Trim()));
            try { return (Page: JsonNode.Parse(await Downloader.GetStringAsync(url, hfToken, ct)) as JsonArray ?? new JsonArray(), Error: (Exception?)null); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return (Page: new JsonArray(), Error: ex); }
        }).ToList();
        var answers = await Task.WhenAll(tasks);
        // One base repo failing is fine; all of them failing is an error worth showing.
        if (answers.Length > 0 && answers.All(a => a.Error is not null)) throw answers[0].Error!;
        var pages = answers.Select(a => a.Page);

        var results = new Dictionary<string, LoraSearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in pages.SelectMany(p => p).OfType<JsonObject>())
        {
            var id = m["id"]?.GetValue<string>();
            if (id is null || results.ContainsKey(id)) continue;
            var tags = (m["tags"] as JsonArray)?.Select(t => t?.GetValue<string>() ?? "").ToList() ?? new();
            if (!allowMature && tags.Contains("not-for-all-audiences")) continue;
            if (!tags.Contains("lora") && !tags.Any(t => t.StartsWith("base_model:adapter:"))) continue;
            var card = m["cardData"] as JsonObject;
            results[id] = new LoraSearchResult
            {
                Source = "Hugging Face",
                Name = id.Split('/')[^1].Replace('_', ' ').Replace('-', ' '),
                Author = m["author"]?.GetValue<string>() ?? id.Split('/')[0],
                PageUrl = $"https://huggingface.co/{id}",
                ImageUrl = WidgetImage(id, card),
                Downloads = Long(m["downloads"]),
                Likes = Long(m["likes"]),
                TriggerWords = LoraLibrary.JoinWords(card?["instance_prompt"]),
                BaseModel = tags.FirstOrDefault(t => t.StartsWith("base_model:adapter:"))?["base_model:adapter:".Length..].Split('/')[^1] ?? "",
                Family = family,
                Updated = m["lastModified"] is JsonValue lm && lm.TryGetValue<DateTime>(out var d) ? d : null,
            };
        }
        IEnumerable<LoraSearchResult> list = results.Values;
        list = sort switch
        {
            LoraSort.Rating => list.OrderByDescending(r => r.Likes),
            LoraSort.Newest => list.OrderByDescending(r => r.Updated),
            _ => list.OrderByDescending(r => r.Downloads),
        };
        return list.ToList();
    }

    /// <summary>The first sample image on the model card (relative paths point into the repo).</summary>
    private static string? WidgetImage(string repo, JsonObject? card)
    {
        var url = (card?["widget"] as JsonArray)?.OfType<JsonObject>()
            .Select(w => w["output"]?["url"]?.GetValue<string>())
            .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u) && !u.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
        if (url is null) return null;
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url
            : $"https://huggingface.co/{repo}/resolve/main/{string.Join('/', url.TrimStart('/').Split('/').Select(Uri.EscapeDataString))}";
    }

    /// <summary>One page of Civitai results; pass the returned cursor to get the next page.</summary>
    public static async Task<(List<LoraSearchResult> Results, string? NextCursor)> CivitaiAsync(IReadOnlyList<string> baseModels, string? family,
        string query, LoraSort sort, bool allowMature, string? cursor, CancellationToken ct)
    {
        string sortKey = sort switch { LoraSort.Rating => "Highest Rated", LoraSort.Newest => "Newest", _ => "Most Downloaded" };
        var url = "https://civitai.com/api/v1/models?types=LORA&limit=40&sort=" + Uri.EscapeDataString(sortKey) +
                  string.Concat(baseModels.Take(10).Select(b => "&baseModels=" + Uri.EscapeDataString(b))) +
                  (allowMature ? "" : "&nsfw=false") +
                  (string.IsNullOrWhiteSpace(query) ? "" : "&query=" + Uri.EscapeDataString(query.Trim())) +
                  (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        var doc = JsonNode.Parse(await Downloader.GetStringAsync(url, null, ct)) as JsonObject ?? new JsonObject();
        var list = new List<LoraSearchResult>();
        foreach (var m in (doc["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            int level = (int)Long(m["nsfwLevel"]);
            bool mature = m["nsfw"] is JsonValue n && n.TryGetValue<bool>(out var nb) && nb || (level & CivitaiExplicitLevels) != 0;
            if (mature && !allowMature) continue;
            var version = (m["modelVersions"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            var file = (version?["files"] as JsonArray)?.OfType<JsonObject>()
                .Where(f => (f["name"]?.GetValue<string>() ?? "").EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f["primary"] is JsonValue p && p.TryGetValue<bool>(out var b) && b)
                .FirstOrDefault();
            if (version is null || file is null) continue;

            // The safest still image: PG first. Without allowMature, only PG / PG-13 images are shown at all.
            var image = (version["images"] as JsonArray)?.OfType<JsonObject>()
                .Where(i => (i["type"]?.GetValue<string>() ?? "image") == "image")
                .Select(i => (Url: i["url"]?.GetValue<string>(), Level: (int)Long(i["nsfwLevel"])))
                .Where(i => i.Url is not null && (allowMature || (i.Level & ~CivitaiSafeLevels) == 0))
                .OrderBy(i => i.Level)
                .FirstOrDefault();
            string? imageUrl = image?.Url;
            int imageLevel = image?.Level ?? 0;

            string name = m["name"]?.GetValue<string>() ?? "Civitai LoRA";
            string baseModel = version["baseModel"]?.GetValue<string>() ?? "";
            string page = $"https://civitai.com/models/{Long(m["id"])}?modelVersionId={Long(version["id"])}";
            double kb = file["sizeKB"] is JsonValue s && s.TryGetValue<double>(out var k) ? k : 0;
            string? fam = LoraFamilies.FromCivitaiBase(baseModel) ?? family;
            var stats = m["stats"] as JsonObject;
            list.Add(new LoraSearchResult
            {
                Source = "Civitai",
                Name = name,
                Author = m["creator"]?["username"]?.GetValue<string>() ?? "",
                PageUrl = page,
                ImageUrl = imageUrl is null ? null : Thumbnail(imageUrl),
                ImageMature = imageUrl is not null && (imageLevel & ~CivitaiSafeLevels) != 0,
                Downloads = Long(stats?["downloadCount"]),
                Likes = Long(stats?["thumbsUpCount"]),
                TriggerWords = LoraLibrary.JoinWords(version["trainedWords"]),
                SizeBytes = kb > 0 ? (long)(kb * 1024) : null,
                BaseModel = baseModel,
                Family = fam,
                Updated = version["publishedAt"] is JsonValue pa && pa.TryGetValue<DateTime>(out var d) ? d : null,
                Candidate = new LoraCandidate
                {
                    Name = name,
                    FileName = file["name"]!.GetValue<string>(),
                    DownloadUrl = file["downloadUrl"]?.GetValue<string>() ?? $"https://civitai.com/api/download/models/{Long(version["id"])}",
                    SizeBytes = kb > 0 ? (long)(kb * 1024) : null,
                    Family = fam,
                    FamilySource = "Civitai",
                    TriggerWords = LoraLibrary.JoinWords(version["trainedWords"]),
                    SourceUrl = page,
                },
            });
        }
        var next = doc["metadata"]?["nextCursor"]?.ToString();
        return (list, string.IsNullOrEmpty(next) ? null : next);
    }

    /// <summary>Civitai serves resized images when the URL asks for a width instead of the original.</summary>
    private static string Thumbnail(string url) =>
        url.Contains("/original=true/") ? url.Replace("/original=true/", "/width=320/") : url;

    private static long Long(JsonNode? n) => n switch
    {
        JsonValue v when v.TryGetValue<long>(out var l) => l,
        JsonValue v when v.TryGetValue<double>(out var d) => (long)d,
        JsonValue v when v.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
        _ => 0,
    };
}
