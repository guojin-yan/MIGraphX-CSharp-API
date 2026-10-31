using MIGraphXSharp.Samples.PaddleOcrV6;
using MIGraphXSharp.Samples.Yolo26;
using Tensor = MIGraphXSharp.Samples.Yolo26.Tensor;
using Letterbox = MIGraphXSharp.Samples.Yolo26.Letterbox;
using Images = MIGraphXSharp.Samples.Yolo26.Images;
using PointD = MIGraphXSharp.Samples.Yolo26.PointD;
using Visualization = MIGraphXSharp.Samples.Yolo26.Visualization;
using Benchmarking = MIGraphXSharp.Samples.Yolo26.Benchmarking;
using OcrTensor = MIGraphXSharp.Samples.PaddleOcrV6.Tensor;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using Xunit;

namespace MIGraphXSharp.Samples.Tests;

public sealed class DecoderTests
{
    [Fact]
    public void BenchmarkExcludesWarmupsAndPoolsOnlyMeasuredStageSamples()
    {
        var calls = 0;
        var measured = Benchmarking.Measure(() =>
        {
            ++calls;
            return new MIGraphXSharp.Samples.Yolo26.TimedValue<int>(calls, new Dictionary<string, double[]> { ["line"] = [calls, calls + .5] });
        }, 2, 3);
        Assert.Equal(5, calls);
        Assert.Equal(5, measured.Value);
        Assert.Equal(new[] { 3d, 3.5, 4, 4.5, 5, 5.5 }, measured.Report.StageSamples["line"]);
        Assert.Equal(3, measured.Report.Pipeline.Samples);
        var statistics = Benchmarking.Summarize(new[] { 1d, 2, 3, 4, 100 });
        Assert.Equal(3, statistics.P50);
        Assert.Equal(100, statistics.P95);
        Assert.Equal(22, statistics.Mean);
    }

    [Fact]
    public void LoadingNormalizesExifOrientationBeforeDetectionAndCropping()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vision-exif-{Guid.NewGuid():N}.png");
        try
        {
            using (var source = new Image<Rgb24>(2, 1))
            {
                source[0, 0] = new Rgb24(255, 0, 0);
                source[1, 0] = new Rgb24(0, 0, 255);
                source.Metadata.ExifProfile = new ExifProfile();
                source.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)3);
                source.SaveAsPng(path);
            }
            using var loaded = Images.Load(path);
            Assert.Equal(new Rgb24(0, 0, 255), loaded[0, 0]);
            using var crop = Images.Rectify(loaded, [new(0,0),new(1,0),new(1,0),new(0,0)]);
            Assert.Equal(loaded[0, 0], crop[0, 0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CtcRemovesRepeatsButKeepsCharactersSeparatedByBlank()
    {
        var ids = new[] { 0, 1, 1, 0, 1, 2, 2, 0 };
        var probabilities = ids.SelectMany(id => Enumerable.Range(0, 3).Select(index => index == id ? .9f : .05f)).ToArray();
        var result = Recognition.Decode(new OcrTensor([1, 8, 3], probabilities), ["", "中", "A"]);
        Assert.Equal("中中A", result.Text);
        Assert.Equal(.9f, result.Confidence, 5);
        Assert.Throws<InvalidOperationException>(() => Recognition.Decode(new OcrTensor([1, 8, 3], probabilities), ["", "A"]));
    }

    [Fact]
    public void RecognitionPaddingIsZeroAfterBgrNormalization()
    {
        using var crop = new Image<Rgb24>(1, 1, new Rgb24(255, 0, 0));
        var values = Recognition.Prepare(crop, 4, 2);
        Assert.Equal(new[] { -1f,-1,0,0,-1,-1,0,0, -1,-1,0,0,-1,-1,0,0, 1,1,0,0,1,1,0,0 }, values);
    }

    [Fact]
    public void LetterboxUsesSameScaleAndPaddingForInverseCoordinates()
    {
        using var image = new Image<Rgb24>(200, 100, new Rgb24(255, 0, 0));
        var result = Images.PrepareYolo(image, 100, 100, false);
        Assert.Equal(25, result.Top);
        Assert.Equal(new PointD(100, 50), result.Restore(50, 50));
        Assert.Equal(1f, result.Tensor[50 * 100 + 50]);
        Assert.Equal(0f, result.Tensor[10000 + 50 * 100 + 50]);
    }

    [Theory]
    [InlineData("detect", 6)]
    [InlineData("pose", 57)]
    [InlineData("obb", 7)]
    public void EndToEndTasksRestoreCoordinatesWithoutApplyingSecondNms(string task, int fields)
    {
        using var image = new Image<Rgb24>(100, 100);
        using var view = new Visualization(image);
        var values = new float[fields * 2];
        for (var row = 0; row < 2; ++row)
        {
            var offset = row * fields;
            values[offset] = task == "obb" ? 50 : 10; values[offset + 1] = task == "obb" ? 50 : 20;
            values[offset + 2] = 40; values[offset + 3] = 60; values[offset + 4] = .9f;
            if (task == "pose") for (var k = 0; k < 17; ++k) { values[offset + 6 + k * 3] = 50; values[offset + 7 + k * 3] = 60; values[offset + 8 + k * 3] = .9f; }
        }
        var result = Decoder.Decode(task, [new Tensor([1, 2, fields], values)], new Letterbox([],100,100,1,0,0),100,100,["person"],.25f,100,view,Path.GetTempPath());
        Assert.Equal(2, result.Length);
        Assert.Equal(task == "obb" ? new PointD(30,20) : new PointD(10,20), result[0].Polygon[0]);
        if (task == "pose") Assert.Equal(17, result[0].Keypoints!.Length);
    }

    [Fact]
    public void ClassificationUsesExportedProbabilitiesAndSortsTopFive()
    {
        var result = Decoder.Classify(new Tensor([1,6], [.1f,.3f,.05f,.2f,.25f,.1f]), []);
        Assert.Equal(new[] { 1,4,3,0,5 }, result.Select(item => item.ClassId));
        Assert.Equal(.3f, result[0].Score);
    }

    [Fact]
    public void DbMapProducesAQuadInOriginalImageCoordinates()
    {
        var map = new float[64 * 32];
        for (var y = 8; y < 20; ++y) for (var x = 10; x < 40; ++x) map[y * 64 + x] = .9f;
        var boxes = TextDetector.Decode(new OcrTensor([1,1,32,64],map),128,64,.2f,.4f);
        var box = Assert.Single(boxes);
        Assert.True(box.Polygon[0].X < 20 && box.Polygon[2].X > 78);
        Assert.Equal(.9f, box.Score, 5);
    }
}
