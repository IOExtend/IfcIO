namespace IfcIO.Geometry;

/// <summary>Triangulates the planar bounds of a faceted IFC face without adding vertices.</summary>
internal static class IfcFaceTriangulator
{
    internal readonly record struct Bound(IReadOnlyList<int> Indices, bool IsOuter);
    private readonly record struct Point(int Index, double X, double Y);
    private readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY)
    {
        internal bool Contains(Point p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
        internal bool Overlaps(Box other) => MinX <= other.MaxX && MaxX >= other.MinX && MinY <= other.MaxY && MaxY >= other.MinY;
        internal static Box Segment(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        internal static Box Bounds(List<Point> ring) => new(ring.Min(p => p.X), ring.Min(p => p.Y), ring.Max(p => p.X), ring.Max(p => p.Y));
    }

    private sealed record Ring(List<Point> Points, double Area, Box Extent, bool IsConvex)
    {
        internal int Parent { get; set; } = -1;
    }

    private const double Roundoff = 1.7763568394002505e-15; // eight ulps near one
    private const int MaxFaceVertices = 32_768;
    private sealed class WorkLimitException : Exception;

    internal static List<int>? Triangulate(IReadOnlyList<IfcVector3> vertices, IReadOnlyList<Bound> bounds)
    {
        if (bounds.Count == 0 || vertices.Count > MaxFaceVertices) return null;
        try { return TriangulateWithin(vertices, bounds); }
        catch (WorkLimitException) { return null; }
    }

    private static List<int>? TriangulateWithin(IReadOnlyList<IfcVector3> vertices, IReadOnlyList<Bound> bounds)
    {
        int workLeft = 20_000_000;
        void Spend()
        {
            if (--workLeft < 0) throw new WorkLimitException();
        }

        // Use the declared outer bound to orient the face. Use the largest nondegenerate loop if no outer was marked.
        IfcVector3 normal = default;
        double normalSize = 0;
        bool foundOuter = false;
        foreach (var bound in bounds)
        {
            if (bound.Indices.Count < 3) return null;
            var origin = vertices[bound.Indices[0]];
            IfcVector3 sum = default;
            for (int i = 1; i + 1 < bound.Indices.Count; i++)
                sum += IfcVector3.Cross(vertices[bound.Indices[i]] - origin, vertices[bound.Indices[i + 1]] - origin);
            double size = Math.Max(Math.Abs(sum.X), Math.Max(Math.Abs(sum.Y), Math.Abs(sum.Z)));
            if (double.IsFinite(size) && size > 0 && (bound.IsOuter && !foundOuter || bound.IsOuter == foundOuter && size > normalSize))
            {
                normal = sum;
                normalSize = size;
                foundOuter = bound.IsOuter;
            }
        }
        if (normalSize == 0) return null;

        if (bounds is [{ Indices.Count: 3 } triangle])
            return [triangle.Indices[0], triangle.Indices[1], triangle.Indices[2]];

        int axis = Math.Abs(normal.Z) == normalSize ? 2 : Math.Abs(normal.Y) == normalSize ? 1 : 0;
        double orientation = Math.Sign(axis == 2 ? normal.Z : axis == 1 ? normal.Y : normal.X);
        var anchor = vertices[bounds[0].Indices[0]];
        Point Project(int index)
        {
            var point = vertices[index] - anchor;
            return axis switch
            {
                0 => new Point(index, point.Y, orientation * point.Z),
                1 => new Point(index, point.Z, orientation * point.X),
                _ => new Point(index, point.X, orientation * point.Y),
            };
        }

        double scale = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            var point = Project(i);
            scale = Math.Max(scale, Math.Max(Math.Abs(point.X), Math.Abs(point.Y)));
        }
        if (!double.IsFinite(scale) || scale <= 0) return null;

        var rings = new List<Ring>(bounds.Count);
        foreach (var bound in bounds)
        {
            var points = new List<Point>(bound.Indices.Count);
            foreach (int index in bound.Indices)
            {
                var p = Project(index);
                var scaled = new Point(index, p.X / scale, p.Y / scale);
                if (points.Count == 0 || !Same(points[^1], scaled)) points.Add(scaled);
            }
            if (points.Count > 1 && Same(points[0], points[^1])) points.RemoveAt(points.Count - 1);
            double area = SignedArea(points);
            if (points.Count < 3 || area == 0) return null;
            bool convex = IsConvexInOrder(points, Math.Sign(area));
            if (!convex && !IsSimple(points)) return null;
            rings.Add(new Ring(points, area, Box.Bounds(points), convex));
        }

        // IFC data in the wild may put separate islands in IfcFaceBound entries. The smallest
        // containing ring is the parent; even levels are filled and odd levels are holes.
        for (int i = 0; i < rings.Count; i++)
        {
            double bestArea = double.PositiveInfinity;
            var inner = rings[i];
            for (int j = 0; j < rings.Count; j++)
            {
                if (i == j) continue;
                Spend();
                var other = rings[j];
                if (!inner.Extent.Overlaps(other.Extent)) continue;
                if (j > i && RingsIntersect(inner.Points, other.Points)) return null;
                double area = Math.Abs(other.Area);
                if (area <= Math.Abs(inner.Area) || area >= bestArea || !Inside(inner.Points[0], other)) continue;
                inner.Parent = j;
                bestArea = area;
            }
        }

        var output = new List<int>();
        for (int i = 0; i < rings.Count; i++)
        {
            int depth = 0;
            for (int ancestor = rings[i].Parent; ancestor >= 0; ancestor = rings[ancestor].Parent) depth++;
            if ((depth & 1) != 0) continue;
            var outerRing = rings[i];
            var outer = Oriented(outerRing, positive: true);
            var holes = rings.Where(ring => ring.Parent == i).OrderByDescending(ring => ring.Extent.MaxX).ToList();
            if (holes.Count == 0 && outerRing.IsConvex)
            {
                for (int k = 1; k + 1 < outer.Count; k++) Add(outer[0], outer[k], outer[k + 1]);
                continue;
            }

            var polygon = new List<Point>(outer);
            foreach (var holeRing in holes)
            {
                var hole = Oriented(holeRing, positive: false);
                int holeIndex = 0;
                for (int j = 1; j < hole.Count; j++)
                    if (hole[j].X > hole[holeIndex].X) holeIndex = j;

                var tip = hole[holeIndex];
                int target = -1;
                var candidates = Enumerable.Range(0, polygon.Count).OrderBy(j => DistanceSquared(polygon[j], tip));
                foreach (int j in candidates)
                {
                    Spend();
                    var p = polygon[j];
                    var extent = Box.Segment(tip, p);
                    if (!InWedge(polygon, j, tip)
                        || CrossesBoundary(tip, p, polygon, allowCoincidentEdge: false)
                        || holes.Any(ring => extent.Overlaps(ring.Extent) && CrossesBoundary(tip, p, ring.Points, allowCoincidentEdge: false))
                        || !InsideRegion(new Point(-1, (p.X + tip.X) / 2, (p.Y + tip.Y) / 2), outerRing, holes))
                        continue;
                    target = j;
                    break;
                }
                if (target < 0) return null;
                var bridge = new List<Point>(hole.Count + 2);
                for (int j = 0; j <= hole.Count; j++) bridge.Add(hole[(holeIndex + j) % hole.Count]);
                bridge.Add(polygon[target]);
                polygon.InsertRange(target + 1, bridge);
            }

            double areaCovered = 0;
            int cursor = 0;
            while (polygon.Count > 3)
            {
                int ear = -1;
                for (int offset = 0; offset < polygon.Count; offset++)
                {
                    Spend();
                    int j = (cursor + offset) % polygon.Count;
                    var a = polygon[(j + polygon.Count - 1) % polygon.Count];
                    var b = polygon[j];
                    var c = polygon[(j + 1) % polygon.Count];
                    double twiceArea = Cross(a, b, c);
                    if (Turn(a, b, c) <= 0 || TriangleHasOtherPoint(a, b, c, polygon)
                        || CrossesBoundary(a, c, polygon, allowCoincidentEdge: true)
                        || !InsideRegion(Centroid(a, b, c), outerRing, holes)) continue;
                    ear = j;
                    areaCovered += twiceArea / 2;
                    Add(a, b, c);
                    break;
                }
                if (ear < 0) return null;
                polygon.RemoveAt(ear);
                cursor = ear % polygon.Count;
            }
            double lastArea = Cross(polygon[0], polygon[1], polygon[2]) / 2;
            double expectedArea = Math.Abs(outerRing.Area) - holes.Sum(ring => Math.Abs(ring.Area));
            if (Turn(polygon[0], polygon[1], polygon[2]) <= 0 || expectedArea <= 0
                || Math.Abs(areaCovered + lastArea - expectedArea) > Math.Abs(outerRing.Area) * 1e-9)
                return null;
            Add(polygon[0], polygon[1], polygon[2]);
        }
        return output.Count == 0 ? null : output;

        void Add(Point a, Point b, Point c)
        {
            output.Add(a.Index);
            output.Add(b.Index);
            output.Add(c.Index);
        }

        bool IsSimple(List<Point> ring)
        {
            for (int i = 0; i < ring.Count; i++)
                for (int j = i + 2; j < ring.Count; j++)
                {
                    if (i == 0 && j == ring.Count - 1) continue;
                    Spend();
                    if (Intersects(ring[i], ring[i + 1], ring[j], ring[(j + 1) % ring.Count])) return false;
                }
            return true;
        }

        bool RingsIntersect(List<Point> a, List<Point> b)
        {
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                {
                    Spend();
                    if (Intersects(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;
                }
            return false;
        }

        bool Inside(Point point, Ring bound)
        {
            if (!bound.Extent.Contains(point)) return false;
            var ring = bound.Points;
            bool inside = false;
            for (int i = 0; i < ring.Count; i++)
            {
                Spend();
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X < a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y))
                    inside = !inside;
            }
            return inside;
        }

        bool InsideRegion(Point point, Ring outer, List<Ring> holes) =>
            Inside(point, outer) && !holes.Any(hole => Inside(point, hole));

        bool CrossesBoundary(Point a, Point b, List<Point> ring, bool allowCoincidentEdge)
        {
            var extent = Box.Segment(a, b);
            for (int i = 0; i < ring.Count; i++)
            {
                Spend();
                var c = ring[i];
                var d = ring[(i + 1) % ring.Count];
                if (!extent.Overlaps(Box.Segment(c, d))) continue;
                bool sharedStart = Same(a, c) || Same(a, d), sharedEnd = Same(b, c) || Same(b, d);
                if (sharedStart && sharedEnd)
                {
                    if (!allowCoincidentEdge) return true;
                    continue;
                }
                if (!sharedStart && !sharedEnd)
                {
                    if (Intersects(a, b, c, d)) return true;
                    continue;
                }
                // A common endpoint is allowed, but overlapping an edge leaving it is not.
                var other = Same(a, c) || Same(b, c) ? d : c;
                var unshared = sharedStart ? b : a;
                if (OnSegment(a, b, other) || OnSegment(c, d, unshared)) return true;
            }
            return false;
        }

        bool TriangleHasOtherPoint(Point a, Point b, Point c, List<Point> ring)
        {
            var extent = new Box(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)),
                Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            foreach (var p in ring)
            {
                Spend();
                if (!extent.Contains(p) || Same(p, a) || Same(p, b) || Same(p, c)) continue;
                if (Turn(a, b, p) >= 0 && Turn(b, c, p) >= 0 && Turn(c, a, p) >= 0)
                    return true;
            }
            return false;
        }
    }

    private static List<Point> Oriented(Ring ring, bool positive)
    {
        var points = new List<Point>(ring.Points);
        if ((ring.Area > 0) != positive) points.Reverse();
        return points;
    }

    private static bool IsConvexInOrder(List<Point> ring, int winding)
    {
        // Strictly consistent turns alone would accept a star. Its edge directions wind more
        // than once; a strictly convex boundary crosses the positive X ray exactly once.
        int crossings = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            var c = ring[(i + 2) % ring.Count];
            if (Turn(a, b, c) != winding) return false;
            bool incoming = UpperHalf(a, b);
            bool outgoing = UpperHalf(b, c);
            if (winding > 0 ? !incoming && outgoing : incoming && !outgoing) crossings++;
        }
        return crossings == 1;

        static bool UpperHalf(Point a, Point b) => b.Y > a.Y || b.Y == a.Y && b.X >= a.X;
    }

    private static bool InWedge(List<Point> ring, int index, Point target)
    {
        var a = ring[(index + ring.Count - 1) % ring.Count];
        var b = ring[index];
        var c = ring[(index + 1) % ring.Count];
        bool incoming = Turn(a, b, target) > 0;
        bool outgoing = Turn(b, c, target) > 0;
        return Turn(a, b, c) >= 0 ? incoming && outgoing : incoming || outgoing;
    }

    private static bool Same(Point a, Point b) => a.X == b.X && a.Y == b.Y;
    private static double DistanceSquared(Point a, Point b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static double Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static int Turn(Point a, Point b, Point c)
    {
        // The tolerance follows the determinant terms, not the whole face. A small hole or a
        // small region far from another contour does not become degenerate because of its size.
        double left = (b.X - a.X) * (c.Y - a.Y);
        double right = (b.Y - a.Y) * (c.X - a.X);
        double tolerance = (Math.Abs(left) + Math.Abs(right)) * Roundoff;
        double determinant = left - right;
        return determinant > tolerance ? 1 : determinant < -tolerance ? -1 : 0;
    }
    private static Point Centroid(Point a, Point b, Point c) => new(-1, (a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3);

    private static double SignedArea(List<Point> ring)
    {
        double area = 0;
        for (int i = 1; i + 1 < ring.Count; i++) area += Cross(ring[0], ring[i], ring[i + 1]);
        return area / 2;
    }

    private static bool OnSegment(Point a, Point b, Point p) => Box.Segment(a, b).Contains(p) && Turn(a, b, p) == 0;

    private static bool Intersects(Point a, Point b, Point c, Point d)
    {
        if (!Box.Segment(a, b).Overlaps(Box.Segment(c, d))) return false;
        int abc = Turn(a, b, c), abd = Turn(a, b, d), cda = Turn(c, d, a), cdb = Turn(c, d, b);
        return abc * abd < 0 && cda * cdb < 0
            || OnSegment(a, b, c) || OnSegment(a, b, d) || OnSegment(c, d, a) || OnSegment(c, d, b);
    }
}
