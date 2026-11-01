namespace MIGraphXSharp.Samples.PPStructure.Ocr;

public sealed record TimedValue<T>(T Value, IReadOnlyDictionary<string, double[]> Stages);
public sealed record TextLine(int Index, PointD[] Polygon, float DetectionConfidence, string Text, float Confidence, string Crop);
public sealed record OcrResult(TextLine[] Lines, string[] AcceptedText, double DetectionMilliseconds, double RecognitionMilliseconds);
public sealed record OcrModels(string Version, string Variant, string? DetectorPath, string RecognizerPath, string DictionaryPath);
