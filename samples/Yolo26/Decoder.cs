using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MIGraphXSharp.Samples.Yolo26;

public sealed record Keypoint(double X, double Y, float Confidence);
public sealed record Detection(int ClassId, string Label, float Score, PointD[] Polygon, Keypoint[]? Keypoints = null, string? Mask = null, int MaskPixels = 0);
public sealed record Classification(int ClassId, string Label, float Score);

public static class Decoder
{
    private static readonly (int A, int B)[] Skeleton =
    [(15,13),(13,11),(16,14),(14,12),(11,12),(5,11),(6,12),(5,6),(5,7),(6,8),(7,9),(8,10),(1,2),(0,1),(0,2),(1,3),(2,4),(3,5),(4,6)];

    public static void DrawPose(Keypoint[] points, Visualization visualization)
    {
        foreach (var p in points) if (p.Confidence >= .5f) visualization.Dot(new PointD(p.X, p.Y));
        foreach (var (a, b) in Skeleton) if (points[a].Confidence >= .5f && points[b].Confidence >= .5f)
            visualization.Segment(new PointD(points[a].X, points[a].Y), new PointD(points[b].X, points[b].Y));
    }

    public static Classification[] Classify(Tensor output, string[] labels)
    {
        if (output.Shape.Length != 2 || output.Shape[0] != 1 || output.Shape[1] != output.Data.Length)
            throw new InvalidOperationException("Classification requires [1,classes].");
        return output.Data.Select((score, index) => new Classification(index, Label(index, labels), Probability(score)))
            .OrderByDescending(item => item.Score).Take(5).ToArray();
    }

    public static Detection[] Decode(string task, Tensor[] outputs, Letterbox transform, int sourceWidth, int sourceHeight,
        string[] labels, float threshold, int maximum, Visualization? visualization, string? directory)
    {
        var fields = task switch { "detect" => 6, "pose" => 57, "segment" => 38, "obb" => 7, _ => throw new ArgumentException("Unknown YOLO task.") };
        var packed = outputs[0];
        if (packed.Shape.Length != 3 || packed.Shape[0] != 1 || packed.Shape[2] != fields || packed.Data.Length != packed.Shape[1] * fields)
            throw new InvalidOperationException($"YOLO26 {task} requires the end-to-end [1,N,{fields}] export; raw YOLOv8 heads are not accepted.");
        if (task == "segment" && (outputs.Length != 2 || !outputs[1].Shape.SequenceEqual(new long[] { 1, 32, 160, 160 })))
            throw new InvalidOperationException("Segmentation requires [1,32,160,160] prototypes.");
        var selected = Enumerable.Range(0, checked((int)packed.Shape[1]))
            .Where(index => Probability(packed.Data[index * fields + 4]) >= threshold)
            .OrderByDescending(index => packed.Data[index * fields + 4]).Take(maximum);
        var result = new List<Detection>();
        foreach (var index in selected)
        {
            var offset = index * fields;
            var classValue = packed.Data[offset + 5];
            if (classValue < 0 || classValue != MathF.Truncate(classValue) || classValue > int.MaxValue)
                throw new InvalidOperationException("Invalid YOLO class ID.");
            var classId = (int)classValue;
            if (labels.Length > 0 && classId >= labels.Length) throw new InvalidOperationException("The labels do not match this model.");
            PointD Restore(double x, double y)
            {
                var point = transform.Restore(x, y);
                return new PointD(Math.Clamp(point.X, 0, sourceWidth - 1), Math.Clamp(point.Y, 0, sourceHeight - 1));
            }
            PointD[] polygon;
            if (task == "obb")
            {
                // End-to-end OBB fields: cx,cy,w,h,score,class,angle (radians).
                var cx = packed.Data[offset]; var cy = packed.Data[offset + 1];
                var w = packed.Data[offset + 2]; var h = packed.Data[offset + 3]; var angle = packed.Data[offset + 6];
                var cosine = Math.Cos(angle); var sine = Math.Sin(angle);
                polygon = new[] { (-.5,-.5),(.5,-.5),(.5,.5),(-.5,.5) }.Select(corner =>
                    Restore(cx + corner.Item1 * w * cosine - corner.Item2 * h * sine, cy + corner.Item1 * w * sine + corner.Item2 * h * cosine)).ToArray();
            }
            else
            {
                polygon = [Restore(packed.Data[offset], packed.Data[offset + 1]), Restore(packed.Data[offset + 2], packed.Data[offset + 1]),
                    Restore(packed.Data[offset + 2], packed.Data[offset + 3]), Restore(packed.Data[offset], packed.Data[offset + 3])];
            }
            var score = packed.Data[offset + 4]; var label = Label(classId, labels);
            visualization?.Polygon(polygon, $"{label} {score:F2}");
            Keypoint[]? keypoints = null;
            if (task == "pose")
            {
                keypoints = Enumerable.Range(0, 17).Select(keypoint =>
                {
                    var point = Restore(packed.Data[offset + 6 + keypoint * 3], packed.Data[offset + 7 + keypoint * 3]);
                    return new Keypoint(point.X, point.Y, Probability(packed.Data[offset + 8 + keypoint * 3]));
                }).ToArray();
                foreach (var point in keypoints.Where(point => point.Confidence >= .5f)) visualization?.Dot(new PointD(point.X, point.Y));
                foreach (var (a, b) in Skeleton)
                    if (keypoints[a].Confidence >= .5f && keypoints[b].Confidence >= .5f)
                        visualization?.Segment(new PointD(keypoints[a].X, keypoints[a].Y), new PointD(keypoints[b].X, keypoints[b].Y));
            }
            string? maskName = null; var maskPixels = 0;
            if (task == "segment")
            {
                using var mask = BuildMask(packed.Data, offset, outputs[1], transform, sourceWidth, sourceHeight, out maskPixels);
                maskName = $"mask-{result.Count:D3}.png";
                if (visualization is not null && directory is not null) visualization.Mask(mask, maskName, directory);
            }
            result.Add(new Detection(classId, label, score, polygon, keypoints, maskName, maskPixels));
        }
        return result.ToArray();
    }

    private static Image<L8> BuildMask(float[] detections, int offset, Tensor prototype, Letterbox transform, int width, int height, out int pixels)
    {
        var ph = (int)prototype.Shape[2]; var pw = (int)prototype.Shape[3];
        var plane = ph * pw; var logits = new float[plane];
        for (var channel = 0; channel < 32; ++channel)
        for (var pixel = 0; pixel < plane; ++pixel) logits[pixel] += detections[offset + 6 + channel] * prototype.Data[channel * plane + pixel];
        // crop in prototype space before bilinear upsampling, then remove letterbox.
        var left = detections[offset] * pw / transform.Width; var right = detections[offset + 2] * pw / transform.Width;
        var top = detections[offset + 1] * ph / transform.Height; var bottom = detections[offset + 3] * ph / transform.Height;
        for (var y = 0; y < ph; ++y) for (var x = 0; x < pw; ++x)
            if (x < left || x >= right || y < top || y >= bottom) logits[y * pw + x] = 0;
        var mask = new Image<L8>(width, height); pixels = 0;
        for (var y = 0; y < height; ++y) for (var x = 0; x < width; ++x)
        {
            var mx = Math.Clamp(((x + .5) * transform.Scale + transform.Left) * pw / transform.Width - .5, 0, pw - 1);
            var my = Math.Clamp(((y + .5) * transform.Scale + transform.Top) * ph / transform.Height - .5, 0, ph - 1);
            var x0 = (int)mx; var y0 = (int)my; var x1 = Math.Min(x0 + 1, pw - 1); var y1 = Math.Min(y0 + 1, ph - 1);
            var fx = mx - x0; var fy = my - y0;
            var value = (logits[y0 * pw + x0] * (1 - fx) + logits[y0 * pw + x1] * fx) * (1 - fy)
                + (logits[y1 * pw + x0] * (1 - fx) + logits[y1 * pw + x1] * fx) * fy;
            if (value > 0) { mask[x, y] = new L8(255); ++pixels; }
        }
        return mask;
    }
    private static string Label(int index, string[] labels) => index < labels.Length ? labels[index] : $"class-{index}";
    private static float Probability(float value) => float.IsFinite(value) && value >= 0 && value <= 1 ? value : throw new InvalidOperationException("Output score is not a probability.");
}
