using System.Globalization;
using System.Runtime.InteropServices;
using JYPPX.OpenCvSharp.Core;
using JYPPX.OpenCvSharp.ImgCodecs;
using JYPPX.OpenCvSharp.ImgProc;
using Codecs = JYPPX.OpenCvSharp.ImgCodecs.Cv2;
using Core = JYPPX.OpenCvSharp.Core.Cv2;
using Processing = JYPPX.OpenCvSharp.ImgProc.Cv2;

// Source-linked into each standalone sample; no sample references another sample assembly.
#if IMAGING_YOLO26
namespace MIGraphXSharp.Samples.Yolo26;
#elif IMAGING_PADDLEOCR
namespace MIGraphXSharp.Samples.PaddleOcr;
#elif IMAGING_PPSTRUCTURE
namespace MIGraphXSharp.Samples.PPStructure.Ocr;
#elif IMAGING_CLIP
namespace MIGraphXSharp.Samples.Clip;
#else
#error Define the sample imaging namespace.
#endif

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct RgbPixel(byte R, byte G, byte B)
{
    public static RgbPixel ParseHex(string color)
    {
        if (color.Length != 7 || color[0] != '#') throw new ArgumentException("Use #RRGGBB.", nameof(color));
        return new(byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber),
            byte.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber), byte.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber));
    }
}

public readonly record struct ImageRect(int X, int Y, int Width, int Height);
public enum Sampling { Linear, Cubic }

/// <summary>Sample-owned RGB8 matrix. OpenCV codec BGR order never escapes this boundary.</summary>
public sealed class RgbImage : IDisposable
{
    private readonly Mat pixels;
    public int Width => pixels.Width;
    public int Height => pixels.Height;

    public RgbImage(int width, int height, RgbPixel color = default)
    {
        ValidateSize(width, height);
        pixels = new Mat(height, width, MatType.CV_8UC3, new Scalar(color.R, color.G, color.B));
    }

    private RgbImage(Mat ownedPixels) => pixels = ownedPixels;

    public RgbPixel this[int x, int y]
    {
        get => GetRowSpan(y)[x];
        set => GetRowSpan(y)[x] = value;
    }

    public Span<RgbPixel> GetRowSpan(int y)
    {
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return pixels.AsSpan<RgbPixel>().Slice(checked(y * Width), Width);
    }

    public static RgbImage Load(string path)
    {
        // Read through .NET for Unicode paths. OpenCV applies EXIF orientation once.
        // The byte budget is checked before decoding; decoded dimensions are checked afterwards.
        using var stream = File.OpenRead(path);
        const int maxInput = 64 * 1024 * 1024;
        if (stream.Length is <= 0 or > maxInput) throw new InvalidDataException("Image must contain 1..64 MiB of encoded data.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        using var bgr = DecodeColor(bytes);
        if (bgr.Empty) throw new InvalidDataException("OpenCV could not decode the image.");
        ValidateSize(bgr.Width, bgr.Height);
        var rgb = new Mat();
        try { Processing.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB); return new RgbImage(rgb); }
        catch { rgb.Dispose(); throw; }
    }

    private static Mat DecodeColor(byte[] bytes)
    {
        try { return Codecs.ImDecode(bytes, ImreadModes.Color); }
        catch (JYPPX.OpenCvSharp.OpenCvException error) { throw new InvalidDataException("OpenCV could not decode the image.", error); }
    }

    public static RgbImage FromPixels(ReadOnlySpan<byte> rgb, int width, int height)
    {
        ValidateSize(width, height);
        if (rgb.Length != checked(width * height * 3)) throw new ArgumentException("Expected packed RGB8 pixels.", nameof(rgb));
        var image = new RgbImage(width, height);
        try { image.pixels.CopyFrom(rgb); return image; }
        catch { image.Dispose(); throw; }
    }

    public void CopyPixelsTo(byte[] destination) => pixels.CopyTo(destination);
    public RgbImage Clone() => new(pixels.Clone());

    public RgbImage Resize(int width, int height, Sampling sampling)
    {
        ValidateSize(width, height);
        var result = new Mat();
        try
        {
            Processing.Resize(pixels, result, new Size(width, height), interpolation:
                sampling == Sampling.Cubic ? InterpolationFlags.Cubic : InterpolationFlags.Linear);
            return new RgbImage(result);
        }
        catch { result.Dispose(); throw; }
    }

    public RgbImage Crop(ImageRect rectangle)
    {
        if (rectangle.X < 0 || rectangle.Y < 0 || rectangle.Width < 1 || rectangle.Height < 1 ||
            (long)rectangle.X + rectangle.Width > Width || (long)rectangle.Y + rectangle.Height > Height)
            throw new ArgumentOutOfRangeException(nameof(rectangle));
        using var roi = pixels.SubMat(new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
        return new RgbImage(roi.Clone()); // independent, continuous storage with its own lifetime
    }

    public void Paste(RgbImage source, int x, int y)
    {
        if (x < 0 || y < 0 || (long)x + source.Width > Width || (long)y + source.Height > Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        using var roi = pixels.SubMat(new Rect(x, y, source.Width, source.Height));
        source.pixels.CopyTo(roi);
    }

    public RgbImage RotateClockwise(int degrees)
    {
        var normalized = (degrees % 360 + 360) % 360;
        if (normalized == 0) return Clone();
        var rotation = normalized switch { 90 => RotateFlags.Rotate90Clockwise, 180 => RotateFlags.Rotate180,
            270 => RotateFlags.Rotate90Counterclockwise, _ => throw new ArgumentOutOfRangeException(nameof(degrees), "Use multiples of 90 degrees.") };
        return new RgbImage(Core.Rotate(pixels, rotation));
    }

    public byte[] EncodePng()
    {
        using var bgr = new Mat();
        Processing.CvtColor(pixels, bgr, ColorConversionCodes.RGB2BGR);
        return Codecs.ImEncode(".png", bgr);
    }

    public void SavePng(string path) => File.WriteAllBytes(path, EncodePng());

    public static byte[] EncodeRgbaPng(byte[] rgba, int width, int height)
    {
        ValidateSize(width, height);
        if (rgba.Length != checked(width * height * 4)) throw new ArgumentException("Expected RGBA8 pixels.", nameof(rgba));
        using var source = new Mat(height, width, MatType.CV_8UC4);
        source.CopyFrom(rgba);
        using var bgra = new Mat();
        Processing.CvtColor(source, bgra, ColorConversionCodes.RGBA2BGRA);
        return Codecs.ImEncode(".png", bgra);
    }

    internal static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > 32768 || height > 32768 || (long)width * height > 80_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions exceed the sample's 80 MP / 32768-pixel limits.");
    }

    public void Dispose() => pixels.Dispose();
}

/// <summary>Byte masks are encoded as one-channel PNGs without color conversion.</summary>
public sealed class GrayImage : IDisposable
{
    private readonly Mat pixels;
    public int Width => pixels.Width;
    public int Height => pixels.Height;
    public GrayImage(int width, int height)
    {
        RgbImage.ValidateSize(width, height);
        pixels = new Mat(height, width, MatType.CV_8UC1, new Scalar(0));
    }
    public byte this[int x, int y]
    {
        get => pixels.GetValue<byte>(y, x);
        set => pixels.SetValue(y, x, value);
    }
    public static GrayImage FromPixels(ReadOnlySpan<byte> bytes, int width, int height)
    {
        RgbImage.ValidateSize(width, height);
        if (bytes.Length != checked(width * height)) throw new ArgumentException("Expected one byte per mask pixel.", nameof(bytes));
        var image = new GrayImage(width, height);
        try { image.pixels.CopyFrom(bytes); return image; }
        catch { image.Dispose(); throw; }
    }
    public void SavePng(string path) => File.WriteAllBytes(path, Codecs.ImEncode(".png", pixels));
    public void Dispose() => pixels.Dispose();
}
