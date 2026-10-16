using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ImageMango.Native;

namespace ImageMango.Core;

/// <summary>
/// Optional post-generation safety check: a ViT image classifier (Falconsai/nsfw_image_detection,
/// ONNX export from onnx-community) with labels 0 = normal, 1 = nsfw. Runs on the CPU in ~0.1 s.
/// </summary>
public sealed class NsfwClassifier : IDisposable
{
    public const string Repo = "onnx-community/nsfw_image_detection-ONNX";
    public const string RepoPath = "onnx/model_quantized.onnx";
    public const double ApproxSizeGb = 0.09;

    private const int Size = 224;
    private readonly InferenceSession _session;
    private readonly string _inputName;

    public NsfwClassifier(string onnxPath)
    {
        // ONNX Runtime's Windows build sends telemetry events through ETW by default; switch them off
        // before the first session is created.
        OrtEnv.Instance().DisableTelemetryEvents();
        var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _session = new InferenceSession(onnxPath, opts);
        _inputName = _session.InputMetadata.Keys.First();
    }

    /// <summary>Probability (0..1) that the image is NSFW.</summary>
    public float Score(RawImage img)
    {
        var input = new DenseTensor<float>(new[] { 1, 3, Size, Size });
        int ch = img.Channels;
        float sx = (float)img.Width / Size, sy = (float)img.Height / Size;

        // Bilinear resize to 224x224, rescale to 0..1, normalize with mean 0.5 / std 0.5.
        for (int y = 0; y < Size; y++)
        {
            float fy = Math.Clamp((y + 0.5f) * sy - 0.5f, 0, img.Height - 1);
            int y0 = (int)fy, y1 = Math.Min(y0 + 1, img.Height - 1);
            float wy = fy - y0;
            for (int x = 0; x < Size; x++)
            {
                float fx = Math.Clamp((x + 0.5f) * sx - 0.5f, 0, img.Width - 1);
                int x0 = (int)fx, x1 = Math.Min(x0 + 1, img.Width - 1);
                float wx = fx - x0;
                for (int c = 0; c < 3; c++)
                {
                    int cc = Math.Min(c, ch - 1);
                    float p00 = img.Pixels[(y0 * img.Width + x0) * ch + cc];
                    float p01 = img.Pixels[(y0 * img.Width + x1) * ch + cc];
                    float p10 = img.Pixels[(y1 * img.Width + x0) * ch + cc];
                    float p11 = img.Pixels[(y1 * img.Width + x1) * ch + cc];
                    float v = (p00 * (1 - wx) + p01 * wx) * (1 - wy) + (p10 * (1 - wx) + p11 * wx) * wy;
                    input[0, c, y, x] = (v / 255f - 0.5f) / 0.5f;
                }
            }
        }

        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, input) });
        var logits = results.First().AsEnumerable<float>().ToArray();
        if (logits.Length < 2) return 0;
        float max = Math.Max(logits[0], logits[1]);
        double e0 = Math.Exp(logits[0] - max), e1 = Math.Exp(logits[1] - max);
        return (float)(e1 / (e0 + e1));
    }

    public void Dispose() => _session.Dispose();
}
