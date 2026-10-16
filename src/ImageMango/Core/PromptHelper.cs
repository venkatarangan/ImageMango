using System.Text;
using System.Text.Json.Nodes;

namespace ImageMango.Core;

/// <summary>A language model the prompt helper can run. The files are the same ones some image models use as text encoder.</summary>
public sealed record HelperModel(string Id, string Name, string Blurb, CatalogFile File);

public static class HelperModels
{
    public static readonly IReadOnlyList<HelperModel> All = new[]
    {
        new HelperModel("qwen3-4b", "Qwen3 4B Instruct (2507)", "Fast and good. The default.",
            new CatalogFile { Role = "llm", Repo = "unsloth/Qwen3-4B-Instruct-2507-GGUF", Path = "Qwen3-4B-Instruct-2507-Q8_0.gguf", SizeGb = 4.28 }),
        new HelperModel("qwen3-8b", "Qwen3 8B", "Better at long or tricky prompts; about half as fast.",
            new CatalogFile { Role = "llm", Repo = "unsloth/Qwen3-8B-GGUF", Path = "Qwen3-8B-Q4_K_M.gguf", SizeGb = 5.03 }),
    };

    public static HelperModel Get(string? id) => All.FirstOrDefault(m => m.Id == id) ?? All[0];
}

/// <summary>What the helper answered: either questions to ask first, or a prompt (with negative prompt and a note).</summary>
public sealed class HelperAnswer
{
    public List<string> Questions { get; init; } = new();
    public string Prompt { get; init; } = "";
    public string NegativePrompt { get; init; } = "";
    public string Notes { get; init; } = "";
    public bool IsQuestion => Questions.Count > 0 && string.IsNullOrWhiteSpace(Prompt);
}

public enum HelperTask { Improve, FromIdea }

/// <summary>
/// The prompt helper's conversation with the language model, written for the selected image model: its prompt guide,
/// sample prompts and whether it uses a negative prompt. Replies are forced into a small JSON shape.
/// </summary>
public sealed class PromptHelperChat
{
    private readonly CatalogModel _model;
    private readonly JsonArray _messages = new();

    public bool UsesNegative { get; }
    /// <summary>Tag-list models (Danbooru style) get shorter prompts.</summary>
    public bool TagStyle { get; }

    public PromptHelperChat(CatalogModel model, double cfgScale)
    {
        _model = model;
        UsesNegative = cfgScale > 1.0;
        TagStyle = (model.PromptGuide ?? model.Tips).Contains("Danbooru", StringComparison.OrdinalIgnoreCase);
        _messages.Add(Message("system", SystemPrompt()));
    }

    public int MaxPromptChars => TagStyle ? 520 : 1100;

    private string SystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are the prompt helper inside ImageMango, an app that makes images on the user's PC. You help the user write a prompt for the image model {_model.Name}.");
        sb.AppendLine();
        sb.AppendLine($"About {_model.Name}: {_model.Description}");
        sb.AppendLine();
        sb.AppendLine("How to write prompts for it:");
        sb.AppendLine(_model.PromptGuide ?? $"{_model.Tips} Describe the subject first, then the setting, style, lighting and framing.");
        if (_model.SamplePrompts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Prompts that work well with it (follow their form and length):");
            foreach (var p in _model.SamplePrompts.Take(4)) sb.AppendLine("- " + p);
        }
        sb.AppendLine();
        sb.AppendLine(UsesNegative
            ? "This model uses a negative prompt (things to avoid). Write a short one fitting the model's style: 5 to 15 comma-separated items, no sentences. " +
              "List image-quality problems and anything the user said they don't want; never exclude ordinary content the user might want. " +
              "Never put the model's own style, or the style the prompt asks for, in the negative prompt (for an anime model: never anime, cartoon, illustration or 2d; for a photo model: never photo or realistic)."
            : "With the current settings this model ignores the negative prompt, so negative_prompt must be an empty string.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Keep everything the user asked for: the subject, the people or things in it, the details, the mood. Make it clearer and more vivid; never replace the idea with a different one.");
        sb.AppendLine(TagStyle
            ? "- Length: about 15 to 35 tags. Every tag once; never repeat a tag or a near-duplicate."
            : "- Length: as long as the examples. Every idea once; no repetition, no filler words like '8k', 'ultra detailed', 'best quality' unless the examples use them.");
        sb.AppendLine("- Write the prompt in English, even if the user writes in another language. Text that should appear inside the image stays in its own language, in double quotes.");
        sb.AppendLine("- Never add words, titles or signs to be drawn in the image unless the user asked for text in the image.");
        sb.AppendLine("- If people appear in a romantic or suggestive scene, make clear they are adults.");
        sb.AppendLine("- The prompt contains only the description: no explanations, no labels like 'Prompt:', no quotes around the whole prompt.");
        sb.AppendLine("- notes: one short sentence (under 30 words) telling the user what you changed or added, in plain words.");
        sb.AppendLine("- Answer only with the JSON object.");
        return sb.ToString();
    }

    private static JsonObject Message(string role, string content) => new() { ["role"] = role, ["content"] = content };

    /// <summary>The first request: improve the user's prompt, or write one from an idea.</summary>
    public void Begin(HelperTask task, string text, string negative, bool mayAsk)
    {
        var sb = new StringBuilder();
        sb.AppendLine(task == HelperTask.Improve ? "Improve this prompt:" : "Write a prompt from this idea:");
        sb.AppendLine(text.Trim());
        if (UsesNegative && task == HelperTask.Improve && !string.IsNullOrWhiteSpace(negative))
            sb.AppendLine().AppendLine("Current negative prompt (improve it if needed):").AppendLine(negative.Trim());
        sb.AppendLine();
        sb.AppendLine(mayAsk
            ? "If something important is unclear and a guess would likely miss what I want (for example the style, who or what exactly, the setting or the mood), " +
              "ask me 1 to 3 short questions in \"questions\" and leave prompt, negative_prompt and notes empty. If it's clear enough, don't ask: write the prompt and leave questions empty."
            : "Don't ask questions: make sensible choices yourself and write the prompt.");
        _messages.Add(Message("user", sb.ToString()));
    }

    /// <summary>After questions: the helper's questions and the user's answers, then a request for the prompt.</summary>
    public void Answer(HelperAnswer asked, IReadOnlyList<string> answers)
    {
        _messages.Add(Message("assistant", ToJson(asked)));
        var sb = new StringBuilder("My answers:\n");
        for (int i = 0; i < asked.Questions.Count; i++)
        {
            var a = i < answers.Count && !string.IsNullOrWhiteSpace(answers[i]) ? answers[i].Trim() : "(no preference, you choose)";
            sb.AppendLine($"- {asked.Questions[i]} {a}");
        }
        sb.AppendLine().Append("Now write the prompt. Don't ask more questions.");
        _messages.Add(Message("user", sb.ToString()));
    }

    /// <summary>The questions were skipped: write the prompt with the helper's own choices.</summary>
    public void SkipQuestions(HelperAnswer asked)
    {
        _messages.Add(Message("assistant", ToJson(asked)));
        _messages.Add(Message("user", "Skip the questions: choose what fits best yourself and write the prompt now."));
    }

    /// <summary>Questions are allowed only in the first reply.</summary>
    public bool MayAskNow => _messages.Count == 2;

    public JsonArray Messages => _messages;

    public JsonObject Schema(bool allowQuestions)
    {
        static JsonObject Str(int max) => new() { ["type"] = "string", ["maxLength"] = max };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["questions"] = new JsonObject { ["type"] = "array", ["items"] = Str(160), ["maxItems"] = allowQuestions ? 3 : 0 },
                ["prompt"] = Str(MaxPromptChars),
                ["negative_prompt"] = Str(UsesNegative ? 300 : 0),
                ["notes"] = Str(400),
            },
            ["required"] = new JsonArray("questions", "prompt", "negative_prompt", "notes"),
            ["additionalProperties"] = false,
        };
    }

    /// <summary>Qwen3's recommended non-thinking sampling, plus a presence penalty against repeated tags.</summary>
    public static JsonObject Sampling(int seed) => new()
    {
        ["temperature"] = 0.7,
        ["top_p"] = 0.8,
        ["top_k"] = 20,
        ["min_p"] = 0.0,
        ["presence_penalty"] = 1.0,
        ["max_tokens"] = 900,
        ["seed"] = seed,
    };

    public static HelperAnswer Parse(string json)
    {
        var o = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("The helper's answer wasn't a JSON object.");
        return new HelperAnswer
        {
            Questions = (o["questions"] as JsonArray)?.Select(q => q?.GetValue<string>()?.Trim() ?? "").Where(q => q.Length > 0).ToList() ?? new(),
            Prompt = Clean(o["prompt"]?.GetValue<string>()),
            NegativePrompt = Clean(o["negative_prompt"]?.GetValue<string>()),
            Notes = o["notes"]?.GetValue<string>()?.Trim() ?? "",
        };
    }

    /// <summary>Drops a "Prompt:" label or quotes around the whole prompt, which small models sometimes add.</summary>
    private static string Clean(string? s)
    {
        s = (s ?? "").Trim();
        foreach (var label in new[] { "Prompt:", "Negative prompt:" })
            if (s.StartsWith(label, StringComparison.OrdinalIgnoreCase)) s = s[label.Length..].Trim();
        if (s.Length > 1 && s[0] == '"' && s[^1] == '"' && s.Count(c => c == '"') == 2) s = s[1..^1].Trim();
        return s;
    }

    private static string ToJson(HelperAnswer a) => new JsonObject
    {
        ["questions"] = new JsonArray(a.Questions.Select(q => (JsonNode?)JsonValue.Create(q)).ToArray()),
        ["prompt"] = a.Prompt,
        ["negative_prompt"] = a.NegativePrompt,
        ["notes"] = a.Notes,
    }.ToJsonString();
}
