using System.Globalization;
using System.Security;
using System.Text;

namespace MIGraphXSharp.Samples.PPStructure.Ocr;

public readonly record struct PointD(double X, double Y);
public static class Images
{
    public static RgbImage Load(string path)
    {
        return RgbImage.Load(path);
    }

    public static float[] ToTensor(RgbImage image, bool rgb, float[]? mean = null, float[]? std = null)
    {
        var plane = checked(image.Width * image.Height);
        var result = new float[checked(plane * 3)];
        {
            for (var y = 0; y < image.Height; ++y)
            {
                var row = image.GetRowSpan(y);
                for (var x = 0; x < row.Length; ++x)
                {
                    var pixel = row[x];
                    var offset = y * image.Width + x;
                    result[offset] = ((rgb ? pixel.R : pixel.B) / 255f - (mean?[0] ?? 0)) / (std?[0] ?? 1);
                    result[plane + offset] = (pixel.G / 255f - (mean?[1] ?? 0)) / (std?[1] ?? 1);
                    result[2 * plane + offset] = ((rgb ? pixel.B : pixel.R) / 255f - (mean?[2] ?? 0)) / (std?[2] ?? 1);
                }
            }
        }
        return result;
    }


}

/// <summary>Portable PNG geometry preview and standalone SVG with UTF-8 labels.</summary>
public sealed class Visualization : IDisposable
{
    private readonly RgbImage preview;
    private readonly string background;
    private readonly StringBuilder vectors = new();
    public Visualization(RgbImage source)
    {
        preview = source.Clone();
        background = Convert.ToBase64String(source.EncodePng());
    }
    public void Polygon(PointD[] points, string label, string color = "#ff5030")
    {
        var rgb = RgbPixel.ParseHex(color);
        for (var i = 0; i < points.Length; ++i) Line(points[i], points[(i + 1) % points.Length], rgb);
        vectors.Append(CultureInfo.InvariantCulture, $"<polygon points=\"{string.Join(" ", points.Select(point => FormattableString.Invariant($"{point.X:F2},{point.Y:F2}")))}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\"/>");
        if (points.Length != 0) Label(points[0], label, color);
    }
    public void Label(PointD position, string text, string color = "#ff5030")
        => vectors.Append(CultureInfo.InvariantCulture, $"<text x=\"{Math.Max(2, position.X):F2}\" y=\"{Math.Clamp(position.Y - 3, Math.Min(18, Math.Max(0, preview.Height - 2)), Math.Max(0, preview.Height - 2)):F2}\" fill=\"{color}\" stroke=\"white\" stroke-width=\"3\" paint-order=\"stroke\" font-family=\"sans-serif\" font-size=\"18\">{SecurityElement.Escape(text)}</text>");
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        preview.SavePng(Path.Combine(directory, "preview.png"));
        File.WriteAllText(Path.Combine(directory, "annotated.svg"), $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{preview.Width}\" height=\"{preview.Height}\" viewBox=\"0 0 {preview.Width} {preview.Height}\"><image width=\"{preview.Width}\" height=\"{preview.Height}\" href=\"data:image/png;base64,{background}\"/>{vectors}</svg>");
    }
    private void Line(PointD a, PointD b, RgbPixel color)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y))));
        steps = Math.Min(steps, 32768);
        for (var i = 0; i <= steps; ++i)
        {
            var x = (int)Math.Round(a.X + (b.X - a.X) * i / steps); var y = (int)Math.Round(a.Y + (b.Y - a.Y) * i / steps);
            for (var offset = -1; offset <= 1; ++offset) { Put(x + offset, y, color); Put(x, y + offset, color); }
        }
    }
    private void Put(int x, int y, RgbPixel color) { if ((uint)x < (uint)preview.Width && (uint)y < (uint)preview.Height) preview[x, y] = color; }
    public void Dispose() => preview.Dispose();
}
