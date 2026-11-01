namespace MIGraphXSharp.Samples.PaddleOcr;

/// <summary>Matching PP-OCR model families; explicit model paths remain available for custom exports.</summary>
public sealed record OcrModels(string Version, string Variant, string? DetectorPath, string RecognizerPath, string DictionaryPath)
{
    public static OcrModels Resolve(Arguments options)
    {
        var version = options.Get("ocr-version", "v6").ToLowerInvariant();
        if (version is not ("v4" or "v5" or "v6"))
            throw new ArgumentException("--ocr-version must be v4, v5 or v6.");
        var variant = options.Get("variant", version == "v6" ? "tiny" : "mobile").ToLowerInvariant();
        if (version == "v6" ? variant is not ("tiny" or "small" or "medium") : variant is not ("mobile" or "server"))
            throw new ArgumentException($"Invalid --variant {variant} for PP-OCR{version}.");
        var recognitionOnly = options.Get("recognition-only", "false") switch
        {
            "true" => true,
            "false" => false,
            _ => throw new ArgumentException("--recognition-only must be true or false.")
        };
        if (recognitionOnly && options.Has("det-model"))
            throw new ArgumentException("Do not combine --recognition-only true with --det-model.");

        string? detector = options.Has("det-model") ? options.Required("det-model") : null;
        string? recognizer = options.Has("rec-model") ? options.Required("rec-model") : null;
        string? dictionary = options.Has("dictionary") ? options.Required("dictionary") : null;
        if (options.Has("model-dir"))
        {
            // model-dir is the family directory (PP-OCRv4/5/6), not its parent.
            var directory = Path.GetFullPath(options.Required("model-dir"));
            if (version == "v6") directory = Path.Combine(directory, variant);
            var stem = $"PP-OCR{version}_{variant}";
            var suffix = version == "v6" ? "_inference.onnx" : ".onnx";
            if (!recognitionOnly) detector ??= Path.Combine(directory, stem + "_det" + suffix);
            recognizer ??= Path.Combine(directory, stem + "_rec" + suffix);
            dictionary ??= Path.Combine(directory, version switch
            {
                "v4" => "ppocrv4_keys.txt",
                "v5" => "ppocrv5_dict.txt",
                _ => stem + "_rec_dict.txt"
            });
        }
        if (string.IsNullOrWhiteSpace(recognizer) || string.IsNullOrWhiteSpace(dictionary))
            throw new ArgumentException("Provide --model-dir or both --rec-model and --dictionary.");
        return new OcrModels(version, variant, detector is null ? null : Existing(detector), Existing(recognizer), Existing(dictionary));
    }

    private static string Existing(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("OCR model or dictionary not found.", path);
        return path;
    }
}
