using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class ModelSceneNode : SceneNode, IRenderableMeshCollection
    {
        private Model Model { get; }
        public Vector4 Tint
        {
            get
            {
                if (meshRenderers.Count > 0)
                {
                    return meshRenderers[0].Tint;
                }

                return Vector4.One;
            }
            set
            {
                foreach (var renderer in meshRenderers)
                {
                    renderer.Tint = value;
                }
            }
        }

        public readonly AnimationController AnimationController;
        public IEnumerable<RenderableMesh> RenderableMeshes => activeMeshRenderers;

        private readonly List<RenderableMesh> meshRenderers = new();
        private readonly List<Animation> animations = new();
        private Dictionary<string, string> skinMaterials;

        private TextureHandle animationTexture = TextureHandle.Zero;

        private ICollection<string> activeMeshGroups = new HashSet<string>();
        private ICollection<RenderableMesh> activeMeshRenderers = new List<RenderableMesh>();

        private bool loadedAnimations;

        public ModelSceneNode(Scene scene, Model model, string skin = null, bool loadAnimations = true)
            : base(scene)
        {
            Model = model;
            AnimationController = new(model.Skeleton);

            if (skin != null)
            {
                SetSkin(skin);
            }

            LoadMeshes();
            UpdateBoundingBox();

            // Load required resources
            if (loadAnimations)
            {
                LoadAnimations();
            }
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (!AnimationController.Update(context.Timestep))
            {
                return;
            }

            UpdateBoundingBox(); // Reset back to the mesh bbox

            var newBoundingBox = LocalBoundingBox;

            // Update animation matrices
            var skeleton = Model.Skeleton;
            var matrices = AnimationController.GetAnimationMatrices(skeleton);
            var animationMatrices = VectorExtensions.Flatten(matrices);

            // Update animation texture
            GL.BindTexture(TextureTarget.Texture2d, animationTexture);
            GL.TexImage2D(TextureTarget.Texture2d, 0, InternalFormat.Rgba32f, 4, skeleton.Bones.Length, 0,
                PixelFormat.Rgba, PixelType.Float, animationMatrices);
            GL.BindTexture(TextureTarget.Texture2d, TextureHandle.Zero);

            var first = true;
            foreach (var matrix in matrices)
            {
                var bbox = LocalBoundingBox.Transform(matrix);
                newBoundingBox = first ? bbox : newBoundingBox.Union(bbox);
                first = false;
            }

            LocalBoundingBox = newBoundingBox;
        }

        public override void Render(Scene.RenderContext context)
        {
            // This node does not render itself; it uses the batching system via IRenderableMeshCollection
        }

        public override IEnumerable<string> GetSupportedRenderModes()
            => meshRenderers.SelectMany(renderer => renderer.GetSupportedRenderModes()).Distinct();

        public override void SetRenderMode(string renderMode)
        {
            foreach (var renderer in meshRenderers)
            {
                renderer.SetRenderMode(renderMode);
            }
        }

        public void SetSkin(string skin)
        {
            var materialGroups = Model.Data.GetArray<IKeyValueCollection>("m_materialGroups");
            string[] defaultMaterials = null;

            foreach (var materialGroup in materialGroups)
            {
                // "The first item needs to match the default materials on the model"
                defaultMaterials ??= materialGroup.GetArray<string>("m_materials");

                if (materialGroup.GetProperty<string>("m_name") == skin)
                {
                    var materials = materialGroup.GetArray<string>("m_materials");

                    skinMaterials = new Dictionary<string, string>();

                    for (var i = 0; i < defaultMaterials.Length; i++)
                    {
                        skinMaterials[defaultMaterials[i]] = materials[i];
                    }

                    break;
                }
            }

            foreach (var mesh in meshRenderers)
            {
                mesh.SetSkin(skinMaterials);
            }
        }

        public void LoadAnimations()
        {
            if (loadedAnimations)
            {
                return;
            }

            loadedAnimations = true;
            animations.AddRange(Model.GetAllAnimations(Scene.GuiContext.FileLoader));

            if (animations.Any())
            {
                SetupAnimationTextures();
            }
        }

        private void LoadMeshes()
        {
            // Get embedded meshes
            foreach (var embeddedMesh in Model.GetEmbeddedMeshesAndLoD().Where(m => (m.LoDMask & 1) != 0))
            {
                meshRenderers.Add(new RenderableMesh(embeddedMesh.Mesh, embeddedMesh.MeshIndex, Scene.GuiContext,
                    skinMaterials, Model));
            }

            // Load referred meshes from file (only load meshes with LoD 1)
            var referredMeshesAndLoDs = Model.GetReferenceMeshNamesAndLoD();
            foreach (var refMesh in referredMeshesAndLoDs.Where(m => (m.LoDMask & 1) != 0))
            {
                var newResource = Scene.GuiContext.LoadFileByAnyMeansNecessary(refMesh.MeshName + "_c");
                if (newResource == null)
                {
                    continue;
                }

                meshRenderers.Add(new RenderableMesh((Mesh)newResource.DataBlock, refMesh.MeshIndex, Scene.GuiContext,
                    skinMaterials, Model));
            }

            // Set active meshes to default
            SetActiveMeshGroups(Model.GetDefaultMeshGroups());
        }

        private void SetupAnimationTextures()
        {
            if (animationTexture == TextureHandle.Zero)
            {
                // Create animation texture
                animationTexture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2d, animationTexture);
                // Set clamping to edges
                GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                // Set nearest-neighbor sampling since we don't want to interpolate matrix rows
                GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
                //Unbind texture again
                GL.BindTexture(TextureTarget.Texture2d, TextureHandle.Zero);
            }
        }

        public IEnumerable<string> GetSupportedAnimationNames()
            => animations.Select(a => a.Name);

        public void SetAnimation(string animationName)
        {
            var activeAnimation = animations.FirstOrDefault(a => a.Name == animationName);
            AnimationController.SetAnimation(activeAnimation);
            UpdateBoundingBox();

            if (activeAnimation != default)
            {
                for (var i = 0; i < meshRenderers.Count; i++)
                {
                    meshRenderers[i].SetAnimationTexture(animationTexture, Model.Skeleton.Bones.Length);
                }
            }
            else
            {
                foreach (var renderer in meshRenderers)
                {
                    renderer.SetAnimationTexture(TextureHandle.Zero, 0);
                }
            }
        }

        public IEnumerable<string> GetMeshGroups()
            => Model.GetMeshGroups();

        public ICollection<string> GetActiveMeshGroups()
            => activeMeshGroups;

        public void SetActiveMeshGroups(IEnumerable<string> meshGroups)
        {
            activeMeshGroups = new HashSet<string>(GetMeshGroups().Intersect(meshGroups));

            var groups = GetMeshGroups();
            if (groups.Count() > 1)
            {
                activeMeshRenderers.Clear();
                foreach (var group in activeMeshGroups)
                {
                    var meshMask = Model.GetActiveMeshMaskForGroup(group).ToArray();

                    foreach (var meshRenderer in meshRenderers)
                    {
                        if (meshMask[meshRenderer.MeshIndex])
                        {
                            activeMeshRenderers.Add(meshRenderer);
                        }
                    }
                }
            }
            else
            {
                activeMeshRenderers = new List<RenderableMesh>(meshRenderers);
            }
        }

        private void UpdateBoundingBox()
        {
            var first = true;
            foreach (var mesh in meshRenderers)
            {
                LocalBoundingBox = first ? mesh.BoundingBox : BoundingBox.Union(mesh.BoundingBox);
                first = false;
            }
        }
    }
}
