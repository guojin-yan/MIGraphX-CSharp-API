using System.Buffers.Binary;
using MIGraphXSharp.Samples.Yolo26;
using Xunit;
using Codecs = JYPPX.OpenCvSharp.ImgCodecs.Cv2;
using ImreadModes = JYPPX.OpenCvSharp.ImgCodecs.ImreadModes;

namespace MIGraphXSharp.Samples.Tests;

internal static class ImageCodecFixtures
{
    internal static byte[] WithPngExifOrientation(byte[] png, ushort orientation)
    {
        // PNG eXIf contains a TIFF header, IFD0 and the SHORT orientation tag.
        byte[] tiff = [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(18), orientation);
        var chunk = new byte[tiff.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk, tiff.Length);
        "eXIf"u8.CopyTo(chunk.AsSpan(4)); tiff.CopyTo(chunk, 8);
        uint crc = uint.MaxValue;
        foreach (var b in chunk.AsSpan(4, tiff.Length + 4))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        // Insert after IHDR, before pixel chunks.
        return [.. png.AsSpan(0, 33), .. chunk, .. png.AsSpan(33)];
    }
}

public sealed class OpenCvImagingTests
{
    [Theory]
    [InlineData(".png", 0)]
    [InlineData(".bmp", 0)]
    [InlineData(".tiff", 0)]
    [InlineData(".webp", 6)]
    [InlineData(".jpg", 3)]
    public void CommonPhotoFormatsDecodeWithoutSwappingColors(string extension, int tolerance)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + extension);
        try
        {
            using var bgr = new JYPPX.OpenCvSharp.Core.Mat(32, 32, JYPPX.OpenCvSharp.Core.MatType.CV_8UC3,
                new JYPPX.OpenCvSharp.Core.Scalar(51, 102, 204));
            File.WriteAllBytes(path, Codecs.ImEncode(extension, bgr));
            using var loaded = RgbImage.Load(path);
            var pixel = loaded[16, 16];
            Assert.Equal((32, 32), (loaded.Width, loaded.Height));
            Assert.InRange(Math.Abs(pixel.R - 204), 0, tolerance);
            Assert.InRange(Math.Abs(pixel.G - 102), 0, tolerance);
            Assert.InRange(Math.Abs(pixel.B - 51), 0, tolerance);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SmallPreviewExportsMasksAndEscapesUnicodeLabels()
    {
        var directory = Path.Combine(Path.GetTempPath(), "预览-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var source = new RgbImage(8, 4, new(10, 20, 30));
            using var mask = new GrayImage(8, 4); mask[3, 2] = 255;
            using var visualization = new Visualization(source);
            visualization.Polygon([new(0, 0), new(7, 0), new(7, 3), new(0, 3)], "文本<&>");
            visualization.Mask(mask, "mask.png", directory); visualization.Save(directory);
            using var preview = RgbImage.Load(Path.Combine(directory, "preview.png"));
            Assert.Equal((8, 4), (preview.Width, preview.Height));
            var svg = File.ReadAllText(Path.Combine(directory, "annotated.svg"));
            Assert.Contains("文本&lt;&amp;&gt;", svg, StringComparison.Ordinal);
            Assert.Contains("data:image/png;base64,", svg, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(directory, "mask.png")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RgbPngAndHipUploadKeepExactChannelOrderOnUnicodePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "视觉-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "红蓝.png");
            byte[] rgb = [255, 0, 0, 0, 0, 255, 10, 20, 30, 100, 110, 120];
            using var source = RgbImage.FromPixels(rgb, 2, 2);
            source.SavePng(path);
            using var loaded = RgbImage.Load(path);
            var uploaded = new byte[rgb.Length]; loaded.CopyPixelsTo(uploaded);
            Assert.Equal(rgb, uploaded);
            using var bgr = Codecs.ImDecode(File.ReadAllBytes(path));
            Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 30, 20, 10, 120, 110, 100 }, bgr.ToArray<byte>());
            var tensor = Images.ToTensor(loaded, false);
            Assert.Equal(1f, tensor[1]); Assert.Equal(1f, tensor[8]);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(2, 3, 2, 0, 255, 0)]
    [InlineData(3, 3, 2, 255, 0, 255)]
    [InlineData(4, 3, 2, 255, 255, 0)]
    [InlineData(5, 2, 3, 255, 0, 0)]
    [InlineData(6, 2, 3, 255, 255, 0)]
    [InlineData(7, 2, 3, 255, 0, 255)]
    [InlineData(8, 2, 3, 0, 255, 0)]
    public void ExifMirrorsAndQuarterTurnsAreAppliedOnce(int orientation, int width, int height, byte red, byte green, byte blue)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using var source = RgbImage.FromPixels([255,0,0, 0,0,255, 0,255,0, 255,255,0, 0,255,255, 255,0,255], 3, 2);
            File.WriteAllBytes(path, ImageCodecFixtures.WithPngExifOrientation(source.EncodePng(), (ushort)orientation));
            using var loaded = RgbImage.Load(path);
            Assert.Equal((width, height), (loaded.Width, loaded.Height));
            Assert.Equal(new RgbPixel(red, green, blue), loaded[0, 0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CropOwnsContinuousPixelsAfterSourceDisposalAndRotationPreservesCorners()
    {
        using var source = new RgbImage(6, 4, new(1, 2, 3));
        source[3, 2] = new(90, 80, 70);
        using var crop = source.Crop(new(2, 1, 2, 2));
        source.Dispose();
        Assert.Equal(new RgbPixel(90, 80, 70), crop[1, 1]);
        using var rotated = crop.RotateClockwise(-90);
        Assert.Equal(crop[1, 1], rotated[1, 0]);
        using var resized = crop.Resize(1, 1, Sampling.Linear);
        Assert.Equal(1, resized.Width);
        Assert.Throws<ArgumentOutOfRangeException>(() => crop.Crop(new(0, 0, 3, 2)));
    }

    [Fact]
    public void ExtremeAspectRatioHasOnePixelOfContentAndCorrectLetterboxPadding()
    {
        using var source = new RgbImage(1, 10000, new(255, 0, 0));
        var result = Images.PrepareYolo(source, 32, 32, false);
        Assert.Equal(32 * 32 * 3, result.Tensor.Length);
        Assert.Equal(1f, result.Tensor[15]);
        Assert.Equal(114 / 255f, result.Tensor[0]);
    }

    [Fact]
    public void GrayMasksAndTransparentOverlaysKeepChannelsAndAlpha()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using var mask = GrayImage.FromPixels([0, 255, 255, 0], 2, 2);
            mask.SavePng(path);
            using var decoded = Codecs.ImDecode(File.ReadAllBytes(path), ImreadModes.Unchanged);
            Assert.Equal(1, decoded.Channels); Assert.Equal(new byte[] { 0, 255, 255, 0 }, decoded.ToArray<byte>());
            using var rgba = Codecs.ImDecode(RgbImage.EncodeRgbaPng([40,220,150,100, 0,0,0,0], 2, 1), ImreadModes.Unchanged);
            Assert.Equal(4, rgba.Channels);
            Assert.Equal(new byte[] { 150,220,40,100, 0,0,0,0 }, rgba.ToArray<byte>());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ClipAndDocumentPreprocessingKeepNormalizationAndZeroPadding()
    {
        using var clip = new MIGraphXSharp.Samples.Clip.RgbImage(300, 200, new(255, 0, 0));
        var tensor = MIGraphXSharp.Samples.Clip.ClipProcessing.Prepare(clip);
        Assert.Equal(3 * 224 * 224, tensor.Length);
        Assert.Equal((1 - .48145466f) / .26862954f, tensor[0], 5);
        Assert.Equal(-.40821073f / .27577711f, tensor[2 * 224 * 224], 5);
        using var document = new MIGraphXSharp.Samples.PPStructure.Ocr.RgbImage(2, 1, new(255, 0, 0));
        var values = MIGraphXSharp.Samples.PPStructure.DocumentProcessing.Tensor(document, 4, 4, imagenet: true, pad: true);
        Assert.Equal((1 - .485f) / .229f, values[0], 5);
        Assert.Equal(0f, values[12]);
        var formula = MIGraphXSharp.Samples.PPStructure.FormulaTokenizer.Prepare(document);
        Assert.Equal(384 * 384, formula.Length); Assert.All(formula, value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void InvalidImagesAndMismatchedPixelBuffersAreRejected()
    {
        Assert.Throws<ArgumentException>(() => RgbImage.FromPixels([1, 2], 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RgbImage(0, 1));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        try { File.WriteAllBytes(path, [1, 2, 3]); Assert.Throws<InvalidDataException>(() => RgbImage.Load(path)); }
        finally { File.Delete(path); }
    }
}
