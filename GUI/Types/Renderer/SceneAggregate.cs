
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    class SceneAggregate : SceneNode
    {
        private Model Model { get; }
        public RenderableMesh RenderMesh { get; }

        internal sealed class Fragment : SceneNode
        {
            public SceneNode Parent { get; init; }
            public RenderableMesh RenderMesh { get; init; }
            public DrawCall DrawCall { get; init; }

            /// <summary>
            /// In the format of 255,255,255
            /// </summary>
            public Vector3? Tint { get; set; }

            public Fragment(Scene scene, SceneNode parent, AABB bounds) : base(scene)
            {
                Parent = parent;
                LocalBoundingBox = bounds;
                Name = parent.Name;
                LayerName = parent.LayerName;
            }

            public override void Render(Scene.RenderContext context) { }
            public override void Update(Scene.UpdateContext context) { }
        }

        public SceneAggregate(Scene scene, Model model)
            : base(scene)
        {
            Model = model;

            var embeddedMeshes = Model.GetEmbeddedMeshesAndLoD().ToList();

            /// TODO: Perhaps use <see cref="ModelSceneNode.LoadMeshes">
            if (embeddedMeshes.Any())
            {
                RenderMesh = new RenderableMesh(embeddedMeshes.First().Mesh, 0, Scene.GuiContext, null, model);

                if (embeddedMeshes.Count > 1)
                {
                    throw new NotImplementedException("More than one embedded mesh");
                }
            }
            else
            {
                var refMeshes = Model.GetReferenceMeshNamesAndLoD().Where(m => (m.LoDMask & 1) != 0).ToList();
                var refMesh = refMeshes.First();

                if (refMeshes.Count > 1)
                {
                    throw new NotImplementedException("More than one referenced mesh");
                }

                var newResource = Scene.GuiContext.LoadFileByAnyMeansNecessary(refMesh.MeshName + "_c");
                if (newResource == null)
                {
                    return;
                }

                RenderMesh = new RenderableMesh((Mesh)newResource.DataBlock, refMesh.MeshIndex, Scene.GuiContext, null, model);
            }

            LocalBoundingBox = RenderMesh.BoundingBox;
        }

        public IEnumerable<Fragment> CreateFragments(IKeyValueCollection aggregateSceneObject)
        {
            var aggregateMeshes = aggregateSceneObject.GetArray("m_aggregateMeshes");

            // Aperture Desk Job goes from draw call -> aggregate mesh
            if (aggregateMeshes.Length > 0 && !aggregateMeshes[0].ContainsKey("m_nDrawCallIndex"))
            {
                foreach (var drawCall in RenderMesh.DrawCallsOpaque)
                {
                    var fragmentData = aggregateMeshes[drawCall.MeshId];
                    var worldBounds = fragmentData.GetArray("m_vWorldBounds");
                    drawCall.DrawBounds = new AABB(worldBounds[0].ToVector3(), worldBounds[1].ToVector3());
                    var fragment = new Fragment(Scene, this, drawCall.DrawBounds.Value)
                    {
                        DrawCall = drawCall,
                        RenderMesh = RenderMesh,
                        Parent = this,
                    };

                    yield return fragment;
                }

                yield break;
            }

            var transformIndex = 0;
            var fragmentTransforms = aggregateSceneObject.GetArray("m_fragmentTransforms");

            // CS2 goes from aggregate mesh -> draw call (many meshes can share one draw call)
            foreach (var fragmentData in aggregateMeshes)
            {
                var drawCallIndex = fragmentData.GetInt32Property("m_nDrawCallIndex");
                var drawCall = RenderMesh.DrawCallsOpaque[drawCallIndex];
                var drawBounds = drawCall.DrawBounds ?? RenderMesh.BoundingBox;
                var fragment = new Fragment(Scene, this, drawBounds)
                {
                    Tint = fragmentData.GetSubCollection("m_vTintColor").ToVector3(),
                    DrawCall = drawCall,
                    RenderMesh = RenderMesh,
                    Parent = this,
                };

                if (fragmentData.GetProperty<bool>("m_bHasTransform") == true)
                {
                    fragment.Transform *= fragmentTransforms[transformIndex++].ToMatrix4x4();
                }

                yield return fragment;
            }
        }

        public override void Render(Scene.RenderContext context)
        {
        }

        public override void Update(Scene.UpdateContext context)
        {
        }

        public override IEnumerable<string> GetSupportedRenderModes() => RenderMesh.GetSupportedRenderModes();

        public override void SetRenderMode(string renderMode)
        {
            RenderMesh.SetRenderMode(renderMode);
        }
    }
}
