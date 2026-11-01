using MIGraphXSharp.Samples.Clip;
using MIGraphXSharp.Samples.PPStructure;
using Xunit;
using PointD = MIGraphXSharp.Samples.PPStructure.Ocr.PointD;
using ModelTensor = MIGraphXSharp.Samples.PPStructure.ModelTensor;

namespace MIGraphXSharp.Samples.Tests;

public sealed class DocumentAndClipTests
{
    [Theory]
    [InlineData(0, 30, 20)]
    [InlineData(90, 80, 30)]
    [InlineData(180, 70, 180)]
    [InlineData(270, 20, 170)]
    public void UprightCoordinatesReturnToOriginalPage(int rotation, double x, double y)
    {
        Assert.Equal(new PointD(x, y), DocumentProcessing.ToSource(new(30, 20), 100, 200, rotation));
    }

    [Fact]
    public void PaddleDetectionsRestore640CanvasExactlyOnce()
    {
        var result = DocumentProcessing.DecodeRegions([new ModelTensor([1, 6], new float[] { 0, .9f, 64, 128, 320, 384 }), new ModelTensor([1], new[] { 1 })], ["cell"], 1000, 500, .5f);
        Assert.Equal(new Box(100, 100, 400, 200), result.Single().Bounds);
    }

    [Fact]
    public void TableAlignmentRejectsUnprovenSpansAndEscapesOcrText()
    {
        var diagnostics = new List<string>();
        Region[] cells = [new("cell", 1, new(50, 0, 40, 20)), new("cell", 1, new(0, 1, 40, 20))];
        var markup = "<table><tbody><tr><td></td><td></td></tr></tbody></table>";
        var ordered = DocumentPipeline.AlignCells(markup, cells, diagnostics);
        Assert.Empty(diagnostics); Assert.Equal(0, ordered![0].Bounds.X);
        Assert.Contains("&lt;script&gt;", DocumentProcessing.FillTable(markup, ["<script>", "a&b"]));
        Assert.Throws<ArgumentException>(() => DocumentProcessing.FillTable(markup, ["one"]));
        Assert.Null(DocumentPipeline.AlignCells(markup.Replace("<td>", "<td rowspan=\"2\">"), cells, diagnostics));
        Assert.Contains("rowspan-needs-review", diagnostics);
        Assert.Null(DocumentPipeline.AlignCells(markup, cells.Take(1).ToArray(), []));
    }

    [Fact]
    public void StructureDecodingPreservesEosAndIncompleteDiagnostic()
    {
        var scores = new float[3 * 50]; scores[48] = 1; scores[50 + 49] = 1;
        var result = DocumentProcessing.DecodeTable(new([1, 3, 50], scores));
        Assert.True(result.Complete); Assert.Equal(1, result.Cells); Assert.Equal("<table><td></td></table>", result.Markup);
        Assert.False(DocumentProcessing.DecodeTable(new([1, 1, 50], scores.Take(50).ToArray())).Complete);
    }

    [Fact]
    public void ClipRanksNormalizedFeaturesAndRejectsZeroEmbeddings()
    {
        var values = new float[1024]; values[0] = 2; values[512 + 1] = 3;
        var rows = ClipProcessing.Normalize(values, 2);
        var ranked = ClipProcessing.Rank(rows[1], rows, [new("first", "one"), new("second", "two")]);
        Assert.Equal("second", ranked[0].Key); Assert.Equal(1, ranked[0].Cosine, 6);
        Assert.Equal(1, ranked.Sum(r => r.CandidateScore), 10);
        Assert.Throws<InvalidDataException>(() => ClipProcessing.Normalize(new float[512], 1));
    }
}
