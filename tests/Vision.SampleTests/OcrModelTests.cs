using MIGraphXSharp.Samples.PaddleOcr;
using Xunit;

namespace MIGraphXSharp.Samples.Tests;

public sealed class OcrModelTests
{
    [Theory]
    [InlineData("v4", "mobile", "PP-OCRv4_mobile_det.onnx", "PP-OCRv4_mobile_rec.onnx", "ppocrv4_keys.txt")]
    [InlineData("v4", "server", "PP-OCRv4_server_det.onnx", "PP-OCRv4_server_rec.onnx", "ppocrv4_keys.txt")]
    [InlineData("v5", "mobile", "PP-OCRv5_mobile_det.onnx", "PP-OCRv5_mobile_rec.onnx", "ppocrv5_dict.txt")]
    [InlineData("v5", "server", "PP-OCRv5_server_det.onnx", "PP-OCRv5_server_rec.onnx", "ppocrv5_dict.txt")]
    [InlineData("v6", "tiny", "tiny/PP-OCRv6_tiny_det_inference.onnx", "tiny/PP-OCRv6_tiny_rec_inference.onnx", "tiny/PP-OCRv6_tiny_rec_dict.txt")]
    [InlineData("v6", "small", "small/PP-OCRv6_small_det_inference.onnx", "small/PP-OCRv6_small_rec_inference.onnx", "small/PP-OCRv6_small_rec_dict.txt")]
    [InlineData("v6", "medium", "medium/PP-OCRv6_medium_det_inference.onnx", "medium/PP-OCRv6_medium_rec_inference.onnx", "medium/PP-OCRv6_medium_rec_dict.txt")]
    public void SelectsMatchingModelAndDictionary(string version, string variant, string detector, string recognizer, string dictionary)
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-models-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var relative in new[] { detector, recognizer, dictionary })
            {
                var path = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "fixture");
            }
            var models = OcrModels.Resolve(new Arguments(["--ocr-version", version, "--variant", variant, "--model-dir", root]));
            Assert.Equal(Path.GetFullPath(Path.Combine(root, detector)), models.DetectorPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, recognizer)), models.RecognizerPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, dictionary)), models.DictionaryPath);
            var recognitionOnly = OcrModels.Resolve(new Arguments(["--ocr-version", version, "--variant", variant,
                "--model-dir", root, "--recognition-only", "true"]));
            Assert.Null(recognitionOnly.DetectorPath);
            File.Delete(Path.Combine(root, dictionary));
            Assert.Throws<FileNotFoundException>(() => OcrModels.Resolve(new Arguments(
                ["--ocr-version", version, "--variant", variant, "--model-dir", root])));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("v3", "mobile")]
    [InlineData("v4", "tiny")]
    [InlineData("v5", "small")]
    [InlineData("v6", "server")]
    public void RejectsInvalidFamilyBeforeLoadingNativeLibraries(string version, string variant)
        => Assert.Throws<ArgumentException>(() => OcrModels.Resolve(new Arguments(["--ocr-version", version, "--variant", variant])));

    [Fact]
    public void ExistingExplicitModelArgumentsRemainRecognitionOnly()
    {
        var file = Path.GetTempFileName();
        try
        {
            var models = OcrModels.Resolve(new Arguments(["--rec-model", file, "--dictionary", file]));
            Assert.Equal("v6", models.Version);
            Assert.Null(models.DetectorPath);
            Assert.Throws<ArgumentException>(() => OcrModels.Resolve(new Arguments(
                ["--rec-model", file, "--dictionary", file, "--det-model", file, "--recognition-only", "true"])));
        }
        finally { File.Delete(file); }
    }
}
