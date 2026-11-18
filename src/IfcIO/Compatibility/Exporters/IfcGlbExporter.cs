using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;

namespace IfcIO.Exporters;

/// <summary>
/// Exports IFC elements, 3D meshes, and materials to standardized glTF 2.0 Binary container format (.glb).
/// </summary>
public static class IfcGlbExporter
{
    private const uint GlbHeaderMagic = 0x46546C67; // "glTF"
    private const uint GlbVersion = 2;
    private const uint ChunkTypeJson = 0x4E4F534A; // "JSON"
    private const uint ChunkTypeBin = 0x004E4942;  // "BIN\0"

    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;

    private const int ComponentTypeFloat = 5126;
    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeUnsignedInt = 5125;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static void Save(IfcModel model, string path)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(path);

        using var fs = File.Create(path);
        Write(model, fs);
    }

    public static void Write(IfcModel model, Stream output)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(output);

        using var binMs = new MemoryStream();
        var bufferViews = new List<BufferViewDef>();
        var accessors = new List<AccessorDef>();
        var gltfMeshes = new List<MeshDef>();
        var gltfNodes = new List<NodeDef>();
        var gltfMaterials = new List<MaterialDef>();

        // Material map
        var matIndexMap = new Dictionary<IfcMaterial, int>();
        foreach (var mat in model.Materials)
        {
            if (!matIndexMap.ContainsKey(mat))
            {
                int matIdx = gltfMaterials.Count;
                matIndexMap[mat] = matIdx;
                gltfMaterials.Add(new MaterialDef
                {
                    Name = mat.Name,
                    Pbr = new PbrDef
                    {
                        BaseColorFactor = [(float)mat.R, (float)mat.G, (float)mat.B, (float)mat.Alpha],
                        MetallicFactor = 0.1f,
                        RoughnessFactor = 0.7f,
                    },
                    DoubleSided = true,
                });
            }
        }

        var rootChildNodeIndices = new List<int>();
        double metresPerUnit = model.LengthUnit.MetresPerUnit();

        var storeyMeshes = model.Storeys
            .Select(storey => (Storey: storey, Items: storey.Elements
                .Select(elem => (Element: elem, Mesh: elem.GetEffectiveMesh()))
                .Where(item => item.Mesh is { Vertices.Count: > 0, Indices.Count: > 0 })
                .Select(item => (item.Element, Mesh: item.Mesh!))
                .ToList()))
            .ToList();

        // Vertices are stored relative to the model centre, which becomes the root node translation:
        // float32 positions at georeferenced offsets lose millimetres (about 8 mm at 100 km).
        var origin = ComputeCentre(storeyMeshes.SelectMany(s => s.Items).Select(item => item.Mesh));

        foreach (var (storey, items) in storeyMeshes)
        {
            var storeyChildNodes = new List<int>();

            foreach (var (elem, mesh) in items)
            {

                int? matId = null;
                if (elem.Material is not null)
                {
                    if (!matIndexMap.TryGetValue(elem.Material, out int existingMat))
                    {
                        existingMat = gltfMaterials.Count;
                        matIndexMap[elem.Material] = existingMat;
                        gltfMaterials.Add(new MaterialDef
                        {
                            Name = elem.Material.Name,
                            Pbr = new PbrDef
                            {
                                BaseColorFactor = [(float)elem.Material.R, (float)elem.Material.G, (float)elem.Material.B, (float)elem.Material.Alpha],
                                MetallicFactor = 0.1f,
                                RoughnessFactor = 0.7f,
                            },
                            DoubleSided = true,
                        });
                    }
                    matId = existingMat;
                }

                int meshIndex = PackMesh(mesh, origin, metresPerUnit, matId, binMs, bufferViews, accessors, gltfMeshes);

                int elemNodeIndex = gltfNodes.Count;
                gltfNodes.Add(new NodeDef
                {
                    Name = $"{elem.Name} [{elem.IfcType}]",
                    Mesh = meshIndex,
                });
                storeyChildNodes.Add(elemNodeIndex);
            }

            if (storeyChildNodes.Count > 0)
            {
                int storeyNodeIndex = gltfNodes.Count;
                gltfNodes.Add(new NodeDef
                {
                    Name = storey.Name,
                    Children = storeyChildNodes,
                });
                rootChildNodeIndices.Add(storeyNodeIndex);
            }
        }

        // Root scene node
        int rootSceneNodeIndex = gltfNodes.Count;
        gltfNodes.Add(new NodeDef
        {
            Name = model.ProjectName,
            Children = rootChildNodeIndices,
            Translation = origin == IfcVector3.Zero ? null : ToGltf(origin, metresPerUnit),
        });

        // Align BIN chunk to 4-byte boundary
        int binLen = (int)binMs.Length;
        int binPadding = (4 - (binLen % 4)) % 4;
        for (int i = 0; i < binPadding; i++)
        {
            binMs.WriteByte(0x00);
        }
        byte[] binBytes = binMs.ToArray();

        // 2. Build JSON chunk
        var rootJson = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, object>
            {
                ["version"] = "2.0",
                ["generator"] = "IfcIO (https://github.com/IOExtend/IfcIO)"
            },
            ["scene"] = 0,
            ["scenes"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = model.ProjectName,
                    ["nodes"] = new int[] { rootSceneNodeIndex }
                }
            },
            ["nodes"] = gltfNodes.Select(n =>
            {
                var dict = new Dictionary<string, object> { ["name"] = n.Name };
                if (n.Mesh.HasValue) dict["mesh"] = n.Mesh.Value;
                if (n.Translation is not null) dict["translation"] = n.Translation;
                if (n.Children is not null && n.Children.Count > 0) dict["children"] = n.Children;
                return dict;
            }).ToArray(),
            ["meshes"] = gltfMeshes.Select(m => new Dictionary<string, object>
            {
                ["name"] = m.Name,
                ["primitives"] = m.Primitives.Select(p =>
                {
                    var pDict = new Dictionary<string, object>
                    {
                        ["attributes"] = new Dictionary<string, object>
                        {
                            ["POSITION"] = p.PositionAccessor,
                            ["NORMAL"] = p.NormalAccessor
                        },
                        ["indices"] = p.IndicesAccessor
                    };
                    if (p.Material.HasValue) pDict["material"] = p.Material.Value;
                    return pDict;
                }).ToArray()
            }).ToArray(),
            ["materials"] = gltfMaterials.Select(m => new Dictionary<string, object>
            {
                ["name"] = m.Name,
                ["pbrMetallicRoughness"] = new Dictionary<string, object>
                {
                    ["baseColorFactor"] = m.Pbr.BaseColorFactor,
                    ["metallicFactor"] = m.Pbr.MetallicFactor,
                    ["roughnessFactor"] = m.Pbr.RoughnessFactor
                },
                ["doubleSided"] = m.DoubleSided
            }).ToArray(),
            ["accessors"] = accessors.Select(a =>
            {
                var dict = new Dictionary<string, object>
                {
                    ["bufferView"] = a.BufferView,
                    ["byteOffset"] = a.ByteOffset,
                    ["componentType"] = a.ComponentType,
                    ["count"] = a.Count,
                    ["type"] = a.Type
                };
                if (a.Min is not null) dict["min"] = a.Min;
                if (a.Max is not null) dict["max"] = a.Max;
                return dict;
            }).ToArray(),
            ["bufferViews"] = bufferViews.Select(bv => new Dictionary<string, object>
            {
                ["buffer"] = 0,
                ["byteOffset"] = bv.ByteOffset,
                ["byteLength"] = bv.ByteLength,
                ["target"] = bv.Target
            }).ToArray(),
            ["buffers"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["byteLength"] = binBytes.Length
                }
            }
        };

        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(rootJson, JsonOptions);
        int jsonPadding = (4 - (jsonBytes.Length % 4)) % 4;

        uint jsonChunkLen = (uint)(jsonBytes.Length + jsonPadding);
        uint binChunkLen = (uint)binBytes.Length;
        uint totalLen = 12 + 8 + jsonChunkLen + 8 + binChunkLen;

        // 3. Write binary GLB stream
        using var bw = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        bw.Write(GlbHeaderMagic);
        bw.Write(GlbVersion);
        bw.Write(totalLen);

        // JSON Chunk
        bw.Write(jsonChunkLen);
        bw.Write(ChunkTypeJson);
        bw.Write(jsonBytes);
        for (int i = 0; i < jsonPadding; i++) bw.Write((byte)0x20); // space padding

        // BIN Chunk
        bw.Write(binChunkLen);
        bw.Write(ChunkTypeBin);
        bw.Write(binBytes);
    }

    private static int PackMesh(
        IfcMesh mesh,
        IfcVector3 origin,
        double metresPerUnit,
        int? materialIndex,
        MemoryStream binMs,
        List<BufferViewDef> bufferViews,
        List<AccessorDef> accessors,
        List<MeshDef> gltfMeshes)
    {
        var vertices = new IfcVector3[mesh.Vertices.Count];
        for (int v = 0; v < vertices.Length; v++)
        {
            var p = mesh.Vertices[v] - origin;
            vertices[v] = new IfcVector3(p.X * metresPerUnit, p.Z * metresPerUnit, -p.Y * metresPerUnit);
        }
        var indices = mesh.Indices;

        // 1. Pack Positions (VEC3 float)
        AlignStream(binMs, 4);
        int posOffset = (int)binMs.Length;

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        var normals = new IfcVector3[vertices.Length];

        // Precompute face normals
        for (int i = 0; i <= indices.Count - 3; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];
            if (i0 < vertices.Length && i1 < vertices.Length && i2 < vertices.Length)
            {
                var v0 = vertices[i0];
                var v1 = vertices[i1];
                var v2 = vertices[i2];
                var fn = IfcVector3.Cross(v1 - v0, v2 - v0).Normalized();
                normals[i0] += fn;
                normals[i1] += fn;
                normals[i2] += fn;
            }
        }

        Span<byte> floatBytes = stackalloc byte[4];
        for (int v = 0; v < vertices.Length; v++)
        {
            var p = vertices[v];
            float px = (float)p.X;
            float py = (float)p.Y;
            float pz = (float)p.Z;

            if (px < minX) minX = px; if (px > maxX) maxX = px;
            if (py < minY) minY = py; if (py > maxY) maxY = py;
            if (pz < minZ) minZ = pz; if (pz > maxZ) maxZ = pz;

            BinaryPrimitives.WriteSingleLittleEndian(floatBytes, px);
            binMs.Write(floatBytes);
            BinaryPrimitives.WriteSingleLittleEndian(floatBytes, py);
            binMs.Write(floatBytes);
            BinaryPrimitives.WriteSingleLittleEndian(floatBytes, pz);
            binMs.Write(floatBytes);
        }

        int posLen = (int)binMs.Length - posOffset;
        int posBvIdx = bufferViews.Count;
        bufferViews.Add(new BufferViewDef { ByteOffset = posOffset, ByteLength = posLen, Target = TargetArrayBuffer });

        int posAccIdx = accessors.Count;
        accessors.Add(new AccessorDef
        {
            BufferView = posBvIdx,
            ByteOffset = 0,
            ComponentType = ComponentTypeFloat,
            Count = vertices.Length,
            Type = "VEC3",
            Min = [minX, minY, minZ],
            Max = [maxX, maxY, maxZ]
        });

        // 2. Pack Normals (VEC3 float)
        AlignStream(binMs, 4);
        int normOffset = (int)binMs.Length;

        for (int v = 0; v < vertices.Length; v++)
        {
            var n = normals[v].Normalized();
            BinaryPrimitives.WriteSingleLittleEndian(floatBytes, (float)n.X);
            binMs.Write(floatBytes);
            BinaryPrimitives.WriteSingleLittleEndian(floatBytes, (float)n.Y);
            binMs.Write(floatBytes);
            BinaryPrimitives.WriteSingleLittleEndian(floatBytes, (float)n.Z);
            binMs.Write(floatBytes);
        }

        int normLen = (int)binMs.Length - normOffset;
        int normBvIdx = bufferViews.Count;
        bufferViews.Add(new BufferViewDef { ByteOffset = normOffset, ByteLength = normLen, Target = TargetArrayBuffer });

        int normAccIdx = accessors.Count;
        accessors.Add(new AccessorDef
        {
            BufferView = normBvIdx,
            ByteOffset = 0,
            ComponentType = ComponentTypeFloat,
            Count = vertices.Length,
            Type = "VEC3"
        });

        // 3. Pack Indices (uint16 or uint32)
        AlignStream(binMs, 4);
        int idxOffset = (int)binMs.Length;
        bool useShort = vertices.Length < 65535;

        if (useShort)
        {
            Span<byte> shortBytes = stackalloc byte[2];
            for (int i = 0; i < indices.Count; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(shortBytes, (ushort)indices[i]);
                binMs.Write(shortBytes);
            }
        }
        else
        {
            Span<byte> uintBytes = stackalloc byte[4];
            for (int i = 0; i < indices.Count; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(uintBytes, (uint)indices[i]);
                binMs.Write(uintBytes);
            }
        }

        int idxLen = (int)binMs.Length - idxOffset;
        int idxBvIdx = bufferViews.Count;
        bufferViews.Add(new BufferViewDef { ByteOffset = idxOffset, ByteLength = idxLen, Target = TargetElementArrayBuffer });

        int idxAccIdx = accessors.Count;
        accessors.Add(new AccessorDef
        {
            BufferView = idxBvIdx,
            ByteOffset = 0,
            ComponentType = useShort ? ComponentTypeUnsignedShort : ComponentTypeUnsignedInt,
            Count = indices.Count,
            Type = "SCALAR"
        });

        int meshIdx = gltfMeshes.Count;
        gltfMeshes.Add(new MeshDef
        {
            Name = mesh.Name,
            Primitives = [
                new PrimitiveDef
                {
                    PositionAccessor = posAccIdx,
                    NormalAccessor = normAccIdx,
                    IndicesAccessor = idxAccIdx,
                    Material = materialIndex
                }
            ]
        });

        return meshIdx;
    }

    /// <summary>IFC is Z-up in model length units; glTF is Y-up in metres: (x, y, z) -> (x, z, -y).</summary>
    private static double[] ToGltf(IfcVector3 p, double metresPerUnit) =>
        [p.X * metresPerUnit, p.Z * metresPerUnit, -p.Y * metresPerUnit];

    /// <summary>Centre of the bounding box of all vertices, in model coordinates (zero when there are none).</summary>
    private static IfcVector3 ComputeCentre(IEnumerable<IfcMesh> meshes)
    {
        var min = new IfcVector3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new IfcVector3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var mesh in meshes)
        {
            foreach (var p in mesh.Vertices)
            {
                min = new IfcVector3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new IfcVector3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
        }
        return min.X <= max.X ? (min + max) * 0.5 : IfcVector3.Zero;
    }

    private static void AlignStream(MemoryStream ms, int alignment)
    {
        int rem = (int)(ms.Length % alignment);
        if (rem != 0)
        {
            int pad = alignment - rem;
            for (int i = 0; i < pad; i++) ms.WriteByte(0x00);
        }
    }

    private sealed class BufferViewDef
    {
        public int ByteOffset { get; init; }
        public int ByteLength { get; init; }
        public int Target { get; init; }
    }

    private sealed class AccessorDef
    {
        public int BufferView { get; init; }
        public int ByteOffset { get; init; }
        public int ComponentType { get; init; }
        public int Count { get; init; }
        public required string Type { get; init; }
        public float[]? Min { get; init; }
        public float[]? Max { get; init; }
    }

    private sealed class MeshDef
    {
        public required string Name { get; init; }
        public List<PrimitiveDef> Primitives { get; init; } = [];
    }

    private sealed class PrimitiveDef
    {
        public int PositionAccessor { get; init; }
        public int NormalAccessor { get; init; }
        public int IndicesAccessor { get; init; }
        public int? Material { get; init; }
    }

    private sealed class NodeDef
    {
        public required string Name { get; init; }
        public int? Mesh { get; init; }
        public List<int>? Children { get; init; }
        public double[]? Translation { get; init; }
    }

    private sealed class MaterialDef
    {
        public required string Name { get; init; }
        public required PbrDef Pbr { get; init; }
        public bool DoubleSided { get; init; }
    }

    private sealed class PbrDef
    {
        public required float[] BaseColorFactor { get; init; }
        public float MetallicFactor { get; init; }
        public float RoughnessFactor { get; init; }
    }
}
