using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MIGraphXSharp.Samples.PaddleOcr;

public sealed record RecognizedText(string Text, float Confidence);

public static class Recognition
{
    public static string[] LoadDictionary(string path)
    {
        // Paddle CTC: blank at zero; dictionary order is model-specific; space appended.
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0) throw new ArgumentException("Empty OCR dictionary.");
        return new[] { "" }.Concat(lines).Append(" ").ToArray();
    }
    public static RecognizedText Decode(Tensor tensor, string[] dictionary)
    {
        if (tensor.Shape.Length != 3 || tensor.Shape[0] != 1 || tensor.Shape[2] != dictionary.Length)
            throw new InvalidOperationException("Recognizer [1,time,classes] and dictionary class count must agree exactly.");
        var classes = dictionary.Length; var steps = checked((int)tensor.Shape[1]);
        if (tensor.Data.Length != checked(classes * steps)) throw new InvalidOperationException("Invalid CTC tensor length.");
        var text = new StringBuilder(); double confidence = 0; var count = 0; var previous = -1;
        for (var step = 0; step < steps; ++step)
        {
            var best = 0; var score = tensor.Data[step * classes];
            for (var index = 1; index < classes; ++index)
                if (tensor.Data[step * classes + index] > score) { score = tensor.Data[step * classes + index]; best = index; }
            if (!float.IsFinite(score) || score < 0 || score > 1) throw new InvalidOperationException("CTC output is not a probability.");
            if (best != 0 && best != previous) { text.Append(dictionary[best]); confidence += score; ++count; }
            previous = best;
        }
        return new RecognizedText(text.ToString(), count == 0 ? 0 : (float)(confidence / count));
    }
    public static float[] Prepare(Image<Rgb24> crop, int width, int height = 48)
    {
        var resizedWidth = Math.Clamp((int)Math.Ceiling((double)height * crop.Width / crop.Height), 1, width);
        using var resized = crop.Clone(context => context.Resize(resizedWidth, height, KnownResamplers.Triangle));
        var normalized = Images.ToTensor(resized, rgb: false, mean: [.5f,.5f,.5f], std: [.5f,.5f,.5f]);
        // Padding is zero AFTER normalization (not a normalized black border).
        var padded = new float[checked(3 * height * width)];
        for (var channel = 0; channel < 3; ++channel) for (var y = 0; y < height; ++y)
            Array.Copy(normalized, (channel * height + y) * resizedWidth, padded, (channel * height + y) * width, resizedWidth);
        return padded;
    }
}
