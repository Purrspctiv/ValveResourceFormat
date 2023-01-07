using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace GUI.Types.Renderer
{
    internal class SpriteSceneNode : SceneNode
    {
        private readonly VertexArrayHandle quadVao = VertexArrayHandle.Zero;
        private readonly RenderMaterial material;
        private readonly Shader shader;
        private readonly Vector3 position;
        private readonly float size;

        public SpriteSceneNode(Scene scene, VrfGuiContext vrfGuiContext, Resource resource, Vector3 position)
            : base(scene)
        {
            material = vrfGuiContext.MaterialLoader.LoadMaterial(resource);
            shader = vrfGuiContext.ShaderLoader.LoadShader(material.Material.ShaderName, material.Material.GetShaderArguments());

            if (quadVao == VertexArrayHandle.Zero)
            {
                quadVao = SetupQuadBuffer();
            }

            size = material.Material.FloatParams.GetValueOrDefault("g_flUniformPointSize", 16);

            this.position = position;
            var size3 = new Vector3(size);
            LocalBoundingBox = new AABB(position - size3, position + size3);
        }

        private VertexArrayHandle SetupQuadBuffer()
        {
            GL.UseProgram(shader.Program);

            var vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            var vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            var vertices = new[]
            {
                // position          ; texcoord
                -1.0f, -1.0f, 0.0f,  0.0f, 1.0f,
                -1.0f, 1.0f, 0.0f,   0.0f, 0.0f,
                1.0f, -1.0f, 0.0f,   1.0f, 1.0f,
                1.0f, 1.0f, 0.0f,    1.0f, 0.0f,
            };

            GL.BufferData(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.DynamicDraw);

            var attributes = new List<(string Name, int Size)>
            {
                ("vPOSITION", 3),
                ("vTEXCOORD", 2),
            };
            var stride = sizeof(float) * attributes.Sum(x => x.Size);
            var offset = 0;

            foreach (var (Name, Size) in attributes)
            {
                var attributeLocation = GL.GetAttribLocation(shader.Program, Name);
                GL.EnableVertexAttribArray(attributeLocation);
                GL.VertexAttribPointer(attributeLocation, Size, VertexAttribPointerType.Float, false, stride, offset);
                offset += sizeof(float) * Size;
            }

            GL.BindVertexArray(VertexArrayHandle.Zero);

            return vao;
        }

        public override void Render(Scene.RenderContext context)
        {
            GL.UseProgram(shader.Program);
            GL.BindVertexArray(quadVao);

            var viewProjectionMatrix = context.Camera.ViewProjectionMatrix.ToOpenTK();
            var cameraPosition = context.Camera.Location.ToOpenTK();

            // Create billboarding rotation (always facing camera)
            Matrix4x4.Decompose(context.Camera.CameraViewMatrix, out _, out var modelViewRotation, out _);
            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            var scaleMatrix = Matrix4x4.CreateScale(size);
            var translationMatrix = Matrix4x4.CreateTranslation(position.X, position.Y, position.Z);

            var test = billboardMatrix * scaleMatrix * translationMatrix;
            var test2 = test.ToOpenTK();

            GL.UniformMatrix4f(shader.GetUniformLocation("uProjectionViewMatrix"), false, viewProjectionMatrix);

            var transformTk = Transform.ToOpenTK();
            GL.UniformMatrix4f(shader.GetUniformLocation("transform"), false, test2);

            material.Render(shader);

            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            material.PostRender();

            GL.Enable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);

            GL.BindVertexArray(VertexArrayHandle.Zero);
            GL.UseProgram(ProgramHandle.Zero);
        }

        public override void Update(Scene.UpdateContext context)
        {
            //
        }
    }
}
