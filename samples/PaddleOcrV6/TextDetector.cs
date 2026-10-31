using MIGraphXSharp.Samples.Vision;

namespace MIGraphXSharp.Samples.PaddleOcrV6;

public sealed record TextBox(PointD[] Polygon, float Score);

/// <summary>DB map threshold, connected contours, minimum rectangle and box unclip.</summary>
public static class TextDetector
{
    public static TextBox[] Decode(Tensor tensor, int sourceWidth, int sourceHeight, float threshold, float boxThreshold, double unclipRatio = 1.4)
    {
        if (tensor.Shape.Length != 4 || tensor.Shape[0] != 1 || tensor.Shape[1] != 1)
            throw new InvalidOperationException("DB detector requires [1,1,height,width].");
        var height = checked((int)tensor.Shape[2]); var width = checked((int)tensor.Shape[3]);
        if (tensor.Data.Length != checked(width * height)) throw new InvalidOperationException("Invalid DB map length.");
        var visited = new bool[tensor.Data.Length]; var queue = new Queue<int>(); var boxes = new List<TextBox>(); var candidates = 0;
        for (var start = 0; start < tensor.Data.Length && candidates < 3000; ++start)
        {
            if (visited[start] || tensor.Data[start] <= threshold) continue;
            ++candidates; visited[start] = true; queue.Enqueue(start);
            var boundary = new List<PointD>();
            while (queue.Count > 0)
            {
                var position = queue.Dequeue(); var x = position % width; var y = position / width; var isBoundary = false;
                for (var dy = -1; dy <= 1; ++dy) for (var dx = -1; dx <= 1; ++dx)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = x + dx; var ny = y + dy;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height || tensor.Data[ny * width + nx] <= threshold) { isBoundary = true; continue; }
                    var next = ny * width + nx;
                    if (!visited[next]) { visited[next] = true; queue.Enqueue(next); }
                }
                if (isBoundary) boundary.Add(new PointD(x, y));
            }
            if (boundary.Count < 3) continue;
            var rectangle = MinimumRectangle(boundary);
            var w = Images.Distance(rectangle[0], rectangle[1]); var h = Images.Distance(rectangle[0], rectangle[3]);
            if (Math.Min(w, h) < 3) continue;
            var left = Math.Clamp((int)Math.Floor(rectangle.Min(point => point.X)), 0, width - 1);
            var right = Math.Clamp((int)Math.Ceiling(rectangle.Max(point => point.X)), 0, width - 1);
            var top = Math.Clamp((int)Math.Floor(rectangle.Min(point => point.Y)), 0, height - 1);
            var bottom = Math.Clamp((int)Math.Ceiling(rectangle.Max(point => point.Y)), 0, height - 1);
            double sum = 0; var count = 0;
            for (var y = top; y <= bottom; ++y) for (var x = left; x <= right; ++x)
                if (Inside(rectangle, x, y)) { sum += tensor.Data[y * width + x]; ++count; }
            var score = count == 0 ? 0 : (float)(sum / count);
            if (score < boxThreshold) continue;
            var distance = w * h * unclipRatio / (2 * (w + h));
            var center = new PointD(rectangle.Average(point => point.X), rectangle.Average(point => point.Y));
            var ux = (rectangle[1].X - rectangle[0].X) / w; var uy = (rectangle[1].Y - rectangle[0].Y) / w;
            var vx = (rectangle[3].X - rectangle[0].X) / h; var vy = (rectangle[3].Y - rectangle[0].Y) / h;
            var expanded = new[] { (-1,-1),(1,-1),(1,1),(-1,1) }.Select(sign => new PointD(
                Math.Clamp((center.X + sign.Item1 * (w / 2 + distance) * ux + sign.Item2 * (h / 2 + distance) * vx) * sourceWidth / width, 0, sourceWidth - 1),
                Math.Clamp((center.Y + sign.Item1 * (w / 2 + distance) * uy + sign.Item2 * (h / 2 + distance) * vy) * sourceHeight / height, 0, sourceHeight - 1))).ToArray();
            boxes.Add(new TextBox(Order(expanded), score));
        }
        // Initial top-to-bottom order, then repair nearby boxes on the same line.
        var sorted = boxes.OrderBy(box => box.Polygon[0].Y).ThenBy(box => box.Polygon[0].X).ToList();
        for (var i = 1; i < sorted.Count; ++i)
        for (var j = i; j > 0 && Math.Abs(sorted[j].Polygon[0].Y - sorted[j - 1].Polygon[0].Y) < 10 && sorted[j].Polygon[0].X < sorted[j - 1].Polygon[0].X; --j)
            (sorted[j], sorted[j - 1]) = (sorted[j - 1], sorted[j]);
        return sorted.ToArray();
    }

    public static PointD[] MinimumRectangle(IReadOnlyList<PointD> points)
    {
        var sorted = points.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        if (sorted.Length < 3) throw new ArgumentException("At least three distinct contour points are required.");
        var hull = new List<PointD>();
        foreach (var point in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        var lowerCount = hull.Count;
        for (var index = sorted.Length - 2; index >= 0; --index)
        {
            var point = sorted[index];
            while (hull.Count > lowerCount && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        hull.RemoveAt(hull.Count - 1);
        double bestArea = double.PositiveInfinity; PointD[]? best = null;
        for (var index = 0; index < hull.Count; ++index)
        {
            var a = hull[index]; var b = hull[(index + 1) % hull.Count];
            var length = Images.Distance(a, b); if (length < 1e-8) continue;
            var ux = (b.X - a.X) / length; var uy = (b.Y - a.Y) / length;
            var xs = hull.Select(point => point.X * ux + point.Y * uy).ToArray();
            var ys = hull.Select(point => -point.X * uy + point.Y * ux).ToArray();
            var x0 = xs.Min(); var x1 = xs.Max(); var y0 = ys.Min(); var y1 = ys.Max(); var area = (x1 - x0) * (y1 - y0);
            if (area >= bestArea) continue;
            bestArea = area;
            best = new[] { (x0,y0),(x1,y0),(x1,y1),(x0,y1) }.Select(point => new PointD(point.Item1 * ux - point.Item2 * uy, point.Item1 * uy + point.Item2 * ux)).ToArray();
        }
        return best ?? throw new ArgumentException("Degenerate contour.");
    }
    private static double Cross(PointD a, PointD b, PointD c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static bool Inside(PointD[] polygon, double x, double y)
    {
        var positive = false; var negative = false;
        for (var i = 0; i < polygon.Length; ++i)
        {
            var cross = Cross(polygon[i], polygon[(i + 1) % polygon.Length], new PointD(x, y));
            positive |= cross > 1e-7; negative |= cross < -1e-7;
        }
        return !(positive && negative);
    }
    private static PointD[] Order(PointD[] points)
    {
        var sorted = points.OrderBy(point => point.X).ToArray();
        var left = sorted.Take(2).OrderBy(point => point.Y).ToArray();
        var right = sorted.Skip(2).OrderBy(point => point.Y).ToArray();
        return [left[0], right[0], right[1], left[1]];
    }
}
