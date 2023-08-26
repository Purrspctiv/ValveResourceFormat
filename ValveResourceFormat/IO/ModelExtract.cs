using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Datamodel;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using RnShapes = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;


namespace ValveResourceFormat.IO;

public class ModelExtract
{
    private readonly Resource modelResource;
    private readonly Model model;
    private readonly PhysAggregateData physAggregateData;
    private readonly IFileLoader fileLoader;
    private readonly string fileName;

    public List<(Mesh Mesh, string FileName)> RenderMeshesToExtract { get; } = new();
    public Dictionary<string, IKeyValueCollection> MaterialInputSignatures { get; } = new();

    public List<(HullDescriptor Hull, string FileName)> PhysHullsToExtract { get; } = new();
    public List<(MeshDescriptor Mesh, string FileName)> PhysMeshesToExtract { get; } = new();

    public string[] PhysicsSurfaceNames { get; private set; }
    public HashSet<string>[] PhysicsCollisionTags { get; private set; }

    public sealed record SurfaceTagCombo(string SurfacePropName, HashSet<string> InteractAsStrings)
    {
        public SurfaceTagCombo(string surfacePropName, string[] collisionTags)
            : this(surfacePropName, new HashSet<string>(collisionTags))
        { }

        public string StringMaterial => string.Join('+', InteractAsStrings) + '$' + SurfacePropName;
        public override int GetHashCode() => StringMaterial.GetHashCode(StringComparison.OrdinalIgnoreCase);
        public bool Equals(SurfaceTagCombo other) => GetHashCode() == other.GetHashCode();
    }

    public HashSet<SurfaceTagCombo> SurfaceTagCombos { get; } = new();

    public enum ModelExtractType
    {
        Default,
        Map_PhysicsToRenderMesh,
        Map_AggregateSplit,
    }

    public ModelExtractType Type { get; init; } = ModelExtractType.Default;
    public Func<SurfaceTagCombo, string> PhysicsToRenderMaterialNameProvider { get; init; }

    public ModelExtract(Resource modelResource, IFileLoader fileLoader)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);

        this.modelResource = modelResource;
        this.model = (Model)modelResource.DataBlock;
        this.fileLoader = fileLoader;

        var refPhysics = model.GetReferencedPhysNames().FirstOrDefault();
        if (refPhysics != null)
        {
            using var physResource = fileLoader.LoadFile(refPhysics + "_c");

            if (physResource != null)
            {
                physAggregateData = (PhysAggregateData)physResource.DataBlock;
            }
        }
        else
        {
            physAggregateData = model.GetEmbeddedPhys();
        }

        EnqueueMeshes();
    }

    /// <summary>
    /// Extract a single mesh to vmdl+dmx.
    /// </summary>
    /// <param name="mesh">Mesh data</param>
    /// <param name="fileName">File name of the mesh e.g "models/my_mesh.vmesh"</param>
    public ModelExtract(Mesh mesh, string fileName)
    {
        RenderMeshesToExtract.Add((mesh, GetDmxFileName_ForReferenceMesh(fileName)));
        this.fileName = Path.ChangeExtension(fileName, ".vmdl");
    }

    public ModelExtract(PhysAggregateData physAggregateData, string fileName)
    {
        this.physAggregateData = physAggregateData;
        this.fileName = fileName;
        EnqueueMeshes();
    }

    private void EnqueueMeshes()
    {
        EnqueueRenderMeshes();
        EnqueuePhysMeshes();
    }

    public ContentFile ToContentFile()
    {
        var vmdl = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveModel()),
            FileName = GetFileName(),
        };

        foreach (var renderMesh in RenderMeshesToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(renderMesh.FileName),
                () => ToDmxMesh(renderMesh.Mesh, Path.GetFileNameWithoutExtension(renderMesh.FileName), MaterialInputSignatures)
            );
        }

        foreach (var physHull in PhysHullsToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(physHull.FileName),
                () => ToDmxMesh(physHull.Hull)
            );
        }

        foreach (var physMesh in PhysMeshesToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(physMesh.FileName),
                () => ToDmxMesh(physMesh.Mesh)
            );
        }

        return vmdl;
    }

    public string ToValveModel()
    {
        var kv = new KVObject(null);

        static KVValue MakeValue(object value)
        {
            var specialType = value switch
            {
                KVValue v => v,
                Vector3 vec3 => MakeArrayValue(new[] { vec3.X, vec3.Y, vec3.Z }),
                _ => null
            };

            if (specialType != null)
            {
                return specialType;
            }

            var basicType = value switch
            {
                string => KVType.STRING,
                bool => KVType.BOOLEAN,
                int => KVType.INT32,
                float => KVType.FLOAT,
                KVObject kv => kv.IsArray ? KVType.ARRAY : KVType.OBJECT,
                _ => throw new NotImplementedException()
            };

            return new KVValue(basicType, value);
        }

        static KVValue MakeArrayValue<T>(IEnumerable<T> values)
        {
            var list = new KVObject(null, isArray: true);
            foreach (var value in values)
            {
                list.AddProperty(null, MakeValue(value));
            }

            return MakeValue(list);
        }

        static void AddItem(KVObject node, KVObject item)
        {
            Debug.Assert(node.IsArray);
            node.AddProperty(null, MakeValue(item));
        }

        static KVObject MakeNode(string className, params (string Name, object Value)[] properties)
        {
            var node = new KVObject(className);
            node.AddProperty("_class", MakeValue(className));
            foreach (var prop in properties)
            {
                node.AddProperty(prop.Name, MakeValue(prop.Value));
            }
            return node;
        }

        static (KVObject Node, KVObject Children) MakeListNode(string className)
        {
            var children = new KVObject(null, isArray: true);
            var node = MakeNode(className, ("children", children));
            return (node, children);
        }

        var root = MakeListNode("RootNode");
        kv.AddProperty("rootNode", MakeValue(root.Node));

        Lazy<KVObject> MakeLazyList(string className)
        {
            return new Lazy<KVObject>(() =>
            {
                var list = MakeListNode(className);
                AddItem(root.Children, list.Node);

                return list.Children;
            });
        }

        var materialGroupList = MakeLazyList("MaterialGroupList");
        var renderMeshList = MakeLazyList("RenderMeshList");
        var physicsShapeList = MakeLazyList("PhysicsShapeList");

        if (RenderMeshesToExtract.Count != 0)
        {
            foreach (var renderMesh in RenderMeshesToExtract)
            {
                var renderMeshFile = MakeNode(
                    "RenderMeshFile",
                    ("filename", renderMesh.FileName)
                );

                AddItem(renderMeshList.Value, renderMeshFile);
            }
        }

        if (PhysHullsToExtract.Any() || PhysMeshesToExtract.Any())
        {
            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                var globalReplace = PhysicsToRenderMaterialNameProvider == null;
                var remapTable = globalReplace ? null
                    : SurfaceTagCombos.ToDictionary(
                        combo => combo.StringMaterial,
                        combo => PhysicsToRenderMaterialNameProvider(combo)
                    );

                RemapMaterials(remapTable, globalReplace);
            }

            foreach (var (physHull, fileName) in PhysHullsToExtract)
            {
                HandlePhysMeshNode(physHull, fileName);
            }

            foreach (var (physMesh, fileName) in PhysMeshesToExtract)
            {
                HandlePhysMeshNode(physMesh, fileName);
            }
        }

        if (physAggregateData is not null)
        {
            foreach (var physicsPart in physAggregateData.Parts)
            {
                foreach (var sphere in physicsPart.Shape.Spheres)
                {
                    var physicsShapeSphere = MakeNode(
                        "PhysicsShapeSphere",
                        ("surface_prop", PhysicsSurfaceNames[sphere.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[sphere.CollisionAttributeIndex])),
                        ("radius", sphere.Shape.Radius),
                        ("center", sphere.Shape.Center)
                    );

                    AddItem(physicsShapeList.Value, physicsShapeSphere);
                }

                foreach (var capsule in physicsPart.Shape.Capsules)
                {
                    var physicsShapeCapsule = MakeNode(
                        "PhysicsShapeCapsule",
                        ("surface_prop", PhysicsSurfaceNames[capsule.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[capsule.CollisionAttributeIndex])),
                        ("radius", capsule.Shape.Radius),
                        ("point0", capsule.Shape.Center[0]),
                        ("point1", capsule.Shape.Center[1])
                    );

                    AddItem(physicsShapeList.Value, physicsShapeCapsule);
                }
            }
        }

        return new KV3File(kv, format: "modeldoc32:version{c5dcef98-b629-46ab-88e3-a17c005c935e}").ToString();

        //
        // Local functions
        //

        void HandlePhysMeshNode<TShape>(ShapeDescriptor<TShape> shapeDesc, string fileName)
            where TShape : struct
        {
            var surfacePropName = PhysicsSurfaceNames[shapeDesc.SurfacePropertyIndex];
            var collisionTags = PhysicsCollisionTags[shapeDesc.CollisionAttributeIndex];

            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                AddItem(renderMeshList.Value, MakeNode("RenderMeshFile", ("filename", fileName)));
                return;
            }

            var className = shapeDesc switch
            {
                HullDescriptor => "PhysicsHullFile",
                MeshDescriptor => "PhysicsMeshFile",
                _ => throw new NotImplementedException()
            };

            // TODO: per faceSet surface_prop
            var physicsShapeFile = MakeNode(
                className,
                ("filename", fileName),
                ("surface_prop", surfacePropName),
                ("collision_tags", string.Join(" ", collisionTags)),
                ("name", shapeDesc.UserFriendlyName ?? fileName)
            );

            AddItem(physicsShapeList.Value, physicsShapeFile);
        }

        void RemapMaterials(
            IReadOnlyDictionary<string, string> remapTable = null,
            bool globalReplace = false,
            string globalDefault = "materials/tools/toolsnodraw.vmat")
        {
            var remaps = new KVObject(null, isArray: true);
            AddItem(materialGroupList.Value,
                MakeNode(
                    "DefaultMaterialGroup",
                    ("remaps", remaps),
                    ("use_global_default", globalReplace),
                    ("global_default_material", globalDefault)
                )
            );

            if (globalReplace || remapTable == null)
            {
                return;
            }

            foreach (var (from, to) in remapTable)
            {
                var remap = new KVObject(null);
                remap.AddProperty("from", MakeValue(from));
                remap.AddProperty("to", MakeValue(to));
                AddItem(remaps, remap);
            }
        }
    }

    public string GetFileName()
        => model?.Data.GetProperty<string>("m_name")
            ?? fileName;

    string GetDmxFileName_ForEmbeddedMesh(string subString, int number = 0)
    {
        var fileName = GetFileName();
        return (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + Path.GetFileNameWithoutExtension(fileName)
            + "_"
            + subString
            + (number > 0 ? number : string.Empty)
            + ".dmx")
            .Replace('\\', '/');
    }

    static string GetDmxFileName_ForReferenceMesh(string fileName)
        => Path.ChangeExtension(fileName, ".dmx").Replace('\\', '/');

    private void EnqueueRenderMeshes()
    {
        if (model == null)
        {
            return;
        }

        GrabMaterialInputSignatures(modelResource);

        var i = 0;
        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            RenderMeshesToExtract.Add((embedded.Mesh, GetDmxFileName_ForEmbeddedMesh(embedded.Name, i++)));
        }

        foreach (var reference in model.GetReferenceMeshNamesAndLoD())
        {
            using var resource = fileLoader.LoadFile(reference.MeshName + "_c");

            if (resource is null)
            {
                continue;
            }

            GrabMaterialInputSignatures(resource);
            RenderMeshesToExtract.Add(((Mesh)resource.DataBlock, GetDmxFileName_ForReferenceMesh(reference.MeshName)));
        }

        void GrabMaterialInputSignatures(Resource resource)
        {
            var materialReferences = resource?.ExternalReferences?.ResourceRefInfoList.Where(r => r.Name[^4..] == "vmat");
            foreach (var material in materialReferences ?? Enumerable.Empty<ResourceExtRefList.ResourceReferenceInfo>())
            {
                using var materialResource = fileLoader.LoadFile(material.Name + "_c");
                MaterialInputSignatures[material.Name] = (materialResource?.DataBlock as Material)?.GetInputSignature();
            }
        }
    }

    private void EnqueuePhysMeshes()
    {
        if (physAggregateData == null)
        {
            return;
        }

        var knownKeys = StringToken.InvertedTable;

        PhysicsSurfaceNames = physAggregateData.SurfacePropertyHashes.Select(hash =>
        {
            knownKeys.TryGetValue(hash, out var name);
            return name ?? hash.ToString(CultureInfo.InvariantCulture);
        }).ToArray();


        PhysicsCollisionTags = physAggregateData.CollisionAttributes.Select(attributes =>
            (attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings")).ToHashSet()
        ).ToArray();

        var i = 0;
        foreach (var physicsPart in physAggregateData.Parts)
        {
            foreach (var hull in physicsPart.Shape.Hulls)
            {
                PhysHullsToExtract.Add((hull, GetDmxFileName_ForEmbeddedMesh("hull", i++)));

                SurfaceTagCombos.Add(new SurfaceTagCombo(
                    PhysicsSurfaceNames[hull.SurfacePropertyIndex],
                    PhysicsCollisionTags[hull.CollisionAttributeIndex]
                ));
            }

            foreach (var mesh in physicsPart.Shape.Meshes)
            {
                PhysMeshesToExtract.Add((mesh, GetDmxFileName_ForEmbeddedMesh("phys", i++)));

                SurfaceTagCombos.Add(new SurfaceTagCombo(
                    PhysicsSurfaceNames[mesh.SurfacePropertyIndex],
                    PhysicsCollisionTags[mesh.CollisionAttributeIndex]
                ));

                foreach (var surfaceIndex in mesh.Shape.Materials)
                {
                    SurfaceTagCombos.Add(new SurfaceTagCombo(
                        PhysicsSurfaceNames[surfaceIndex],
                        PhysicsCollisionTags[mesh.CollisionAttributeIndex]
                    ));
                }
            }
        }
    }

    public static byte[] ToDmxMesh(Mesh mesh, string name, Dictionary<string, IKeyValueCollection> materialInputSignatures = null)
    {
        var mdat = mesh.Data;
        var mbuf = mesh.VBIB;
        var vertexBuffer = mbuf.VertexBuffers.First();
        var ib = mbuf.IndexBuffers.First();

        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelBaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        ReadOnlySpan<int> indexBuffer = GltfModelExporter.ReadIndices(ib, 0, (int)ib.ElementCount, 0);
        IKeyValueCollection materialInputSignature = null;

        foreach (var sceneObject in mdat.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var vertexBufferInfo = drawCall.GetArray("m_vertexBuffers")[0]; // In what situation can we have more than 1 vertex buffer per draw call?
                var vertexBufferIndex = vertexBufferInfo.GetInt32Property("m_hBuffer");

                var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
                var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");

                if (vertexBufferIndex != 0 || indexBufferIndex != 0)
                {
                    continue; // Skip this TODO
                }

                var material = drawCall.GetProperty<string>("m_material");
                materialInputSignature ??= materialInputSignatures?.GetValueOrDefault(material);

                var startIndex = drawCall.GetInt32Property("m_nStartIndex");
                var indexCount = drawCall.GetInt32Property("m_nIndexCount");

                GenerateTriangleFaceSetFromIndexBuffer(
                    dag,
                    indexBuffer[startIndex..(startIndex + indexCount)],
                    material,
                    $"{startIndex}..{startIndex + indexCount}");
            }
        }

        var indices = Enumerable.Range(0, (int)ib.ElementCount).ToArray();

        foreach (var attribute in vertexBuffer.InputLayoutFields)
        {
            var attributeFormat = VBIB.GetFormatInfo(attribute);
            var semantic = attribute.SemanticName.ToLowerInvariant() + "$" + attribute.SemanticIndex;

            if (attribute.SemanticName is "NORMAL")
            {
                var (normals, tangents) = VBIB.GetNormalTangentArray(vertexBuffer, attribute);
                vertexData.AddIndexedStream(semantic, normals, indices);

                if (tangents.Length > 0)
                {
                    vertexData.AddIndexedStream("tangent$" + attribute.SemanticIndex, tangents, indices);
                }

                continue;
            }
            else if (attribute.SemanticName is "BLENDINDICES")
            {
                vertexData.JointCount = Math.Max(vertexData.JointCount, 1);

                var blendIndices = VBIB.GetBlendIndicesArray(vertexBuffer, attribute);
                vertexData.AddStream(semantic, Array.ConvertAll(blendIndices, i => (int)i));
                continue;
            }
            else if (attribute.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS")
            {
                vertexData.JointCount = 4;

                var vectorWeights = VBIB.GetBlendWeightsArray(vertexBuffer, attribute);
                var flatWeights = MemoryMarshal.Cast<Vector4, float>(vectorWeights).ToArray();

                vertexData.AddStream("blendweights$" + attribute.SemanticIndex, flatWeights);
                continue;
            }

            if (materialInputSignature is not null)
            {
                var insgElement = Material.FindD3DInputSignatureElement(materialInputSignature, attribute.SemanticName, attribute.SemanticIndex);

                // Use engine semantics for attributes that need them
                if (insgElement.Semantic is "VertexPaintBlendParams" or "VertexPaintTintColor")
                {
                    semantic = insgElement.Semantic + "$0";
                }
            }

            switch (attributeFormat.ElementCount)
            {
                case 1:
                    var scalar = VBIB.GetScalarAttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, scalar, indices);
                    break;
                case 2:
                    var vec2 = VBIB.GetVector2AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec2, indices);
                    break;
                case 3:
                    var vec3 = VBIB.GetVector3AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec3, indices);
                    break;
                case 4:
                    var vec4 = VBIB.GetVector4AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec4, indices);
                    break;
                default:
                    throw new NotImplementedException($"Stream {semantic} has an unexpected number of components: {attributeFormat.ElementCount}.");
            }
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    public byte[] ToDmxMesh(HullDescriptor hull)
    {
        var uniformSurface = PhysicsSurfaceNames[hull.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[hull.CollisionAttributeIndex];
        return ToDmxMesh(hull.Shape, hull.UserFriendlyName, uniformSurface, uniformCollisionTags);
    }

    public byte[] ToDmxMesh(MeshDescriptor mesh)
    {
        var uniformSurface = PhysicsSurfaceNames[mesh.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[mesh.CollisionAttributeIndex];
        return ToDmxMesh(mesh.Shape, mesh.UserFriendlyName, uniformSurface, uniformCollisionTags, PhysicsSurfaceNames);
    }

    public static byte[] ToDmxMesh(RnShapes.Hull hull, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelBaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        // n-gon face set
        var faceSet = new DmeFaceSet() { Name = "hull faces" };
        faceSet.Material.MaterialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
        dag.Shape.FaceSets.Add(faceSet);

        Debug.Assert(hull.Faces.Length + hull.Vertices.Length == (hull.Edges.Length / 2) + 2);

        foreach (var face in hull.Faces)
        {
            var startEdge = face.Edge;
            var currentEdge = startEdge;
            do
            {
                var e = hull.Edges[currentEdge];
                faceSet.Faces.Add(e.Origin);
                currentEdge = e.Next;
            }
            while (currentEdge != startEdge);

            faceSet.Faces.Add(-1);
        }

        var indices = Enumerable.Range(0, hull.Vertices.Length * 3).ToArray();
        vertexData.AddIndexedStream("position$0", hull.Vertices, indices);

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    public static byte[] ToDmxMesh(RnShapes.Mesh mesh, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        string[] surfaceList)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelBaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        if (mesh.Materials.Length == 0)
        {
            var materialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
            GenerateTriangleFaceSet(dag, 0, mesh.Triangles.Length, materialName);
        }
        else
        {
            Debug.Assert(mesh.Materials.Length == mesh.Triangles.Length);
            Debug.Assert(surfaceList.Length > 0);

            Span<DmeFaceSet> faceSets = new DmeFaceSet[surfaceList.Length];
            for (var t = 0; t < mesh.Materials.Length; t++)
            {
                var surfaceIndex = mesh.Materials[t];
                var faceSet = faceSets[surfaceIndex];

                if (faceSet == null)
                {
                    var surface = surfaceList[surfaceIndex];
                    faceSet = faceSets[surfaceIndex] = new DmeFaceSet()
                    {
                        Name = surface + '$' + surfaceIndex
                    };
                    faceSet.Material.MaterialName = new SurfaceTagCombo(surface, uniformCollisionTags).StringMaterial;
                    dag.Shape.FaceSets.Add(faceSet);
                }

                faceSet.Faces.Add(t * 3);
                faceSet.Faces.Add(t * 3 + 1);
                faceSet.Faces.Add(t * 3 + 2);
                faceSet.Faces.Add(-1);
            }
        }

        var indices = new int[mesh.Triangles.Length * 3];
        for (var t = 0; t < mesh.Triangles.Length; t++)
        {
            for (var i = 0; i < 3; i++)
            {
                indices[t * 3 + i] = mesh.Triangles[t].Indices[i];
            }
        }

        vertexData.AddIndexedStream("position$0", mesh.Vertices, indices);

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    private static void DmxModelBaseLayout(string name, out DmeModel dmeModel, out DmeDag dag, out DmeVertexData vertexData)
    {
        dmeModel = new DmeModel() { Name = name };
        dag = new DmeDag() { Name = name };
        dmeModel.Children.Add(dag);
        dmeModel.JointList.Add(dag);

        var transformList = new DmeTransformsList();
        transformList.Transforms.Add(new DmeTransform());
        dmeModel.BaseStates.Add(transformList);

        vertexData = new DmeVertexData { Name = "bind" };
        dag.Shape.CurrentState = vertexData;
        dag.Shape.BaseStates.Add(vertexData);
    }

    private static void TieElementRoot(Datamodel.Datamodel dmx, DmeModel dmeModel)
    {
        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeModel,
            ["model"] = dmeModel,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["source"] = $"Generated with {ValveResourceFormat.Utils.StringToken.VRF_GENERATOR}",
            }
        };
    }

    private static void GenerateTriangleFaceSet(DmeDag dag, int triangleStart, int triangleEnd, string material)
    {
        var faceSet = new DmeFaceSet() { Name = triangleStart + "-" + triangleEnd };
        dag.Shape.FaceSets.Add(faceSet);

        for (var i = triangleStart; i < triangleEnd; i++)
        {
            faceSet.Faces.Add(i * 3);
            faceSet.Faces.Add(i * 3 + 1);
            faceSet.Faces.Add(i * 3 + 2);
            faceSet.Faces.Add(-1);
        }

        faceSet.Material.MaterialName = material;
    }

    private static void GenerateTriangleFaceSetFromIndexBuffer(DmeDag dag, ReadOnlySpan<int> indices, string material, string name)
    {
        var faceSet = new DmeFaceSet() { Name = name };
        dag.Shape.FaceSets.Add(faceSet);

        for (var i = 0; i < indices.Length; i += 3)
        {
            faceSet.Faces.Add(indices[i]);
            faceSet.Faces.Add(indices[i + 1]);
            faceSet.Faces.Add(indices[i + 2]);
            faceSet.Faces.Add(-1);
        }

        faceSet.Material.MaterialName = material;
    }
}
