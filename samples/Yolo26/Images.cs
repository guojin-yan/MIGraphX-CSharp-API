using System.Globalization;
using System.Security;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MIGraphXSharp.Samples.Yolo26;

public readonly record struct PointD(double X, double Y);
public sealed record Letterbox(float[] Tensor, int Width, int Height, double Scale, int Left, int Top)
{
    public PointD Restore(double x, double y) => new((x - Left) / Scale, (y - Top) / Scale);
}

public static class Images
{
    public static Image<Rgb24> Load(string path)
    {
        var image = Image.Load<Rgb24>(path);
        image.Mutate(context => context.AutoOrient());
        return image;
    }

    public static Letterbox PrepareYolo(Image<Rgb24> source, int width, int height, bool classification)
    {
        if (classification)
        {
            // Ultralytics classification validation: resize shortest edge, then center crop.
            var size = Math.Min(source.Width, source.Height);
            using var crop = source.Clone(context => context.Crop(new Rectangle((source.Width - size) / 2, (source.Height - size) / 2, size, size))
                .Resize(width, height, KnownResamplers.Bicubic));
            return new Letterbox(ToTensor(crop, rgb: true), width, height, (double)width / size, 0, 0);
        }
        var scale = Math.Min((double)width / source.Width, (double)height / source.Height);
        var resizedWidth = (int)Math.Round(source.Width * scale);
        var resizedHeight = (int)Math.Round(source.Height * scale);
        var left = (int)Math.Round((width - resizedWidth) / 2d - 0.1);
        var top = (int)Math.Round((height - resizedHeight) / 2d - 0.1);
        using var resized = source.Clone(context => context.Resize(resizedWidth, resizedHeight, KnownResamplers.Triangle));
        using var canvas = new Image<Rgb24>(width, height, new Rgb24(114, 114, 114));
        canvas.Mutate(context => context.DrawImage(resized, new Point(left, top), 1));
        return new Letterbox(ToTensor(canvas, rgb: true), width, height, scale, left, top);
    }

    public static float[] ToTensor(Image<Rgb24> image, bool rgb, float[]? mean = null, float[]? std = null)
    {
        var plane = checked(image.Width * image.Height);
        var result = new float[checked(plane * 3)];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; ++y)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; ++x)
                {
                    var pixel = row[x];
                    var offset = y * image.Width + x;
                    result[offset] = ((rgb ? pixel.R : pixel.B) / 255f - (mean?[0] ?? 0)) / (std?[0] ?? 1);
                    result[plane + offset] = (pixel.G / 255f - (mean?[1] ?? 0)) / (std?[1] ?? 1);
                    result[2 * plane + offset] = ((rgb ? pixel.B : pixel.R) / 255f - (mean?[2] ?? 0)) / (std?[2] ?? 1);
                }
            }
        });
        return result;
    }

    public static Image<Rgb24> Rectify(Image<Rgb24> image, PointD[] quad)
    {
        if (quad.Length != 4) throw new ArgumentException("A quadrilateral needs four corners.", nameof(quad));
        var width = Math.Clamp((int)Math.Round(Math.Max(Distance(quad[0], quad[1]), Distance(quad[2], quad[3]))), 1, 8192);
        var height = Math.Clamp((int)Math.Round(Math.Max(Distance(quad[0], quad[3]), Distance(quad[1], quad[2]))), 1, 8192);
        // Unit-square -> quadrilateral projective mapping (TL, TR, BR, BL).
        var dx1 = quad[1].X - quad[2].X; var dx2 = quad[3].X - quad[2].X;
        var dy1 = quad[1].Y - quad[2].Y; var dy2 = quad[3].Y - quad[2].Y;
        var sx = quad[0].X - quad[1].X + quad[2].X - quad[3].X;
        var sy = quad[0].Y - quad[1].Y + quad[2].Y - quad[3].Y;
        var determinant = dx1 * dy2 - dx2 * dy1;
        var g = Math.Abs(determinant) < 1e-8 ? 0 : (sx * dy2 - dx2 * sy) / determinant;
        var h = Math.Abs(determinant) < 1e-8 ? 0 : (dx1 * sy - sx * dy1) / determinant;
        var a = quad[1].X - quad[0].X + g * quad[1].X;
        var b = quad[3].X - quad[0].X + h * quad[3].X;
        var d = quad[1].Y - quad[0].Y + g * quad[1].Y;
        var e = quad[3].Y - quad[0].Y + h * quad[3].Y;
        var output = new Image<Rgb24>(width, height);
        for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
        {
            var u = width == 1 ? 0 : (double)x / (width - 1);
            var v = height == 1 ? 0 : (double)y / (height - 1);
            var divisor = g * u + h * v + 1;
            output[x, y] = Sample(image, (a * u + b * v + quad[0].X) / divisor, (d * u + e * v + quad[0].Y) / divisor);
        }
        if ((double)height / width >= 1.5) output.Mutate(context => context.Rotate(RotateMode.Rotate270));
        return output;
    }

    public static Rgb24 Sample(Image<Rgb24> image, double x, double y)
    {
        x = Math.Clamp(x, 0, image.Width - 1); y = Math.Clamp(y, 0, image.Height - 1);
        var x0 = (int)x; var y0 = (int)y; var x1 = Math.Min(x0 + 1, image.Width - 1); var y1 = Math.Min(y0 + 1, image.Height - 1);
        var fx = x - x0; var fy = y - y0;
        var p00 = image[x0, y0]; var p01 = image[x1, y0]; var p10 = image[x0, y1]; var p11 = image[x1, y1];
        byte Blend(byte c00, byte c01, byte c10, byte c11) => (byte)Math.Clamp(Math.Round((c00 * (1 - fx) + c01 * fx) * (1 - fy) + (c10 * (1 - fx) + c11 * fx) * fy), 0, 255);
        return new Rgb24(Blend(p00.R, p01.R, p10.R, p11.R), Blend(p00.G, p01.G, p10.G, p11.G), Blend(p00.B, p01.B, p10.B, p11.B));
    }
    public static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>Portable PNG geometry preview and standalone SVG with UTF-8 labels.</summary>
public sealed class Visualization : IDisposable
{
    private readonly Image<Rgb24> preview;
    private readonly string background;
    private readonly StringBuilder vectors = new();
    public Visualization(Image<Rgb24> source)
    {
        preview = source.Clone();
        using var stream = new MemoryStream(); source.SaveAsPng(stream);
        background = Convert.ToBase64String(stream.ToArray());
    }
    public void Polygon(PointD[] points, string label, string color = "#ff5030")
    {
        var rgb = Color.ParseHex(color).ToPixel<Rgb24>();
        for (var i = 0; i < points.Length; ++i) Line(points[i], points[(i + 1) % points.Length], rgb);
        vectors.Append(CultureInfo.InvariantCulture, $"<polygon points=\"{string.Join(" ", points.Select(point => FormattableString.Invariant($"{point.X:F2},{point.Y:F2}")))}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\"/>");
        if (points.Length != 0) Label(points[0], label, color);
    }
    public void Label(PointD position, string text, string color = "#ff5030")
        => vectors.Append(CultureInfo.InvariantCulture, $"<text x=\"{Math.Max(2, position.X):F2}\" y=\"{Math.Clamp(position.Y - 3, 18, preview.Height - 2):F2}\" fill=\"{color}\" stroke=\"white\" stroke-width=\"3\" paint-order=\"stroke\" font-family=\"sans-serif\" font-size=\"18\">{SecurityElement.Escape(text)}</text>");
    public void Segment(PointD a, PointD b, string color = "#00ff80")
    {
        Line(a, b, Color.ParseHex(color).ToPixel<Rgb24>());
        vectors.Append(CultureInfo.InvariantCulture, $"<line x1=\"{a.X:F2}\" y1=\"{a.Y:F2}\" x2=\"{b.X:F2}\" y2=\"{b.Y:F2}\" stroke=\"{color}\" stroke-width=\"3\"/>");
    }
    public void Dot(PointD point)
    {
        for (var y = -3; y <= 3; ++y) for (var x = -3; x <= 3; ++x) Put((int)point.X + x, (int)point.Y + y, new Rgb24(255, 220, 0));
        vectors.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{point.X:F2}\" cy=\"{point.Y:F2}\" r=\"3\" fill=\"#ffdc00\"/>");
    }
    public void Mask(Image<L8> mask, string name, string directory)
    {
        mask.SaveAsPng(Path.Combine(directory, name));
        using var colored = new Image<Rgba32>(mask.Width, mask.Height);
        for (var y = 0; y < mask.Height; ++y) for (var x = 0; x < mask.Width; ++x)
        {
            if (mask[x, y].PackedValue == 0) continue;
            colored[x, y] = new Rgba32(40, 220, 150, 100);
            var pixel = preview[x, y]; preview[x, y] = new Rgb24((byte)(pixel.R * .6 + 16), (byte)(pixel.G * .6 + 88), (byte)(pixel.B * .6 + 60));
        }
        using var stream = new MemoryStream(); colored.SaveAsPng(stream);
        vectors.Append($"<image width=\"{mask.Width}\" height=\"{mask.Height}\" href=\"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}\"/>");
    }
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        preview.SaveAsPng(Path.Combine(directory, "preview.png"));
        File.WriteAllText(Path.Combine(directory, "annotated.svg"), $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{preview.Width}\" height=\"{preview.Height}\" viewBox=\"0 0 {preview.Width} {preview.Height}\"><image width=\"{preview.Width}\" height=\"{preview.Height}\" href=\"data:image/png;base64,{background}\"/>{vectors}</svg>");
    }
    private void Line(PointD a, PointD b, Rgb24 color)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y))));
        steps = Math.Min(steps, 32768);
        for (var i = 0; i <= steps; ++i)
        {
            var x = (int)Math.Round(a.X + (b.X - a.X) * i / steps); var y = (int)Math.Round(a.Y + (b.Y - a.Y) * i / steps);
            for (var offset = -1; offset <= 1; ++offset) { Put(x + offset, y, color); Put(x, y + offset, color); }
        }
    }
    private void Put(int x, int y, Rgb24 color) { if ((uint)x < (uint)preview.Width && (uint)y < (uint)preview.Height) preview[x, y] = color; }
    public void Dispose() => preview.Dispose();
}
