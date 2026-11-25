namespace IfcIO.Geometry;

internal static class IfcSweptDiskMesher
{
    internal const int Sides = 16;
    private static readonly (double Cos, double Sin)[] Circle = Enumerable.Range(0, Sides)
        .Select(i => (Math.Cos(i * Math.Tau / Sides), Math.Sin(i * Math.Tau / Sides))).ToArray();

    internal static IfcMesh? Build(int id, string name, List<IfcVector3> path, double radius, double innerRadius)
    {
        bool hollow = innerRadius > 0;
        int stride = hollow ? Sides * 2 : Sides;
        var vertices = new List<IfcVector3>(path.Count * stride + (hollow ? 0 : 2));
        var indices = new List<int>();
        var incoming = Direction(0);
        if (incoming == IfcVector3.Zero) return null;
        var reference = Math.Abs(incoming.Z) < 0.9 ? IfcVector3.UnitZ : IfcVector3.UnitX;
        var x = IfcVector3.Cross(reference, incoming).Normalized();
        var y = IfcVector3.Cross(incoming, x);
        var previousAdvance = new double[Sides];
        for (int ring = 0; ring < path.Count; ring++)
        {
            bool atEnd = ring == path.Count - 1;
            var outgoing = atEnd ? incoming : Direction(ring);
            if (outgoing == IfcVector3.Zero) return null;
            double dot = IfcVector3.Dot(incoming, outgoing);
            if (dot <= -1 + 1e-9) return null;

            // Project the incoming section along its axis onto the joint's half-angle plane.
            var plane = incoming + outgoing;
            double denominator = 1 + dot;
            var turn = IfcVector3.Cross(incoming, outgoing);
            double length = ring == 0 ? 0 : (path[ring] - path[ring - 1]).Length;
            for (int side = 0; side < Sides; side++)
            {
                var section = x * Circle[side].Cos + y * Circle[side].Sin;
                double advance = -IfcVector3.Dot(section, plane) / denominator;
                if (ring > 0 && length + radius * (advance - previousAdvance[side]) <= 0) return null;
                // The same joint point lies on both cylinders. Express its advance on the next leg.
                var projected = section + incoming * advance;
                previousAdvance[side] = IfcVector3.Dot(projected, outgoing);
                vertices.Add(path[ring] + projected * radius);
            }
            if (hollow)
            {
                for (int side = 0; side < Sides; side++)
                {
                    var outer = vertices[ring * stride + side];
                    vertices.Add(path[ring] + (outer - path[ring]) * (innerRadius / radius));
                }
            }

            // Transport the frame by the shortest rotation so the ring indexing does not twist at bends.
            var delta = IfcVector3.Cross(turn, x);
            x += delta + IfcVector3.Cross(turn, delta) * (1 / denominator);
            x = x.Normalized();
            y = IfcVector3.Cross(outgoing, x).Normalized();
            incoming = outgoing;
        }

        if (vertices.Exists(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z))) return null;
        for (int ring = 0; ring < path.Count - 1; ring++)
        {
            int a = ring * stride, b = a + stride;
            for (int side = 0; side < Sides; side++)
            {
                int next = (side + 1) % Sides;
                Quad(a + side, a + next, b + next, b + side);
                if (hollow) Quad(a + next + Sides, a + side + Sides, b + side + Sides, b + next + Sides);
            }
        }

        int last = (path.Count - 1) * stride;
        if (hollow)
        {
            for (int side = 0; side < Sides; side++)
            {
                int next = (side + 1) % Sides;
                Quad(next, side, side + Sides, next + Sides);
                Quad(last + side, last + next, last + next + Sides, last + side + Sides);
            }
        }
        else
        {
            int startCenter = vertices.Count;
            vertices.Add(path[0]);
            int endCenter = vertices.Count;
            vertices.Add(path[^1]);
            for (int side = 0; side < Sides; side++)
            {
                int next = (side + 1) % Sides;
                Triangle(startCenter, next, side);
                Triangle(endCenter, last + side, last + next);
            }
        }
        return IfcMesh.Create(id, name, vertices, indices);

        IfcVector3 Direction(int edge) => (path[edge + 1] - path[edge]).Normalized();
        void Triangle(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
        void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }
    }
}
