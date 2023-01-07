using System;
using System.Collections.Generic;
using System.Linq;
using GUI.Utils;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ValveResourceFormat;

namespace GUI.Types.Renderer
{
    internal class MaterialRenderer : IRenderer
    {
        private readonly RenderMaterial material;
        private readonly Shader shader;
        private readonly VertexArrayHandle quadVao;

        public AABB BoundingBox => new(-1, -1, -1, 1, 1, 1);

        public MaterialRenderer(VrfGuiContext vrfGuiContext, Resource resource)
        {
            material = vrfGuiContext.MaterialLoader.LoadMaterial(resource);
            shader = vrfGuiContext.ShaderLoader.LoadShader(material.Material.ShaderName, material.Material.GetShaderArguments());
            quadVao = SetupQuadBuffer();
        }

        private VertexArrayHandle SetupQuadBuffer()
        {
            GL.UseProgram(shader.Program);

            // Create and bind VAO
            var vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            var vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            var vertices = new[]
            {
                // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
                -1.0f, -1.0f, 0.0f,  0.0f, 0.0f, 0.0f, 1.0f,   0.0f, 1.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
                // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
                -1.0f, 1.0f, 0.0f,   0.0f, 0.0f, 0.0f, 1.0f,   0.0f, 0.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
                // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
                1.0f, -1.0f, 0.0f,   0.0f, 0.0f, 0.0f, 1.0f,   1.0f, 1.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
                // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
                1.0f, 1.0f, 0.0f,    0.0f, 0.0f, 0.0f, 1.0f,   1.0f, 0.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
            };

            GL.BufferData(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);

            GL.EnableVertexAttribArray(0);

            var attributes = new List<(string Name, int Size)>
            {
                ("vPOSITION", 3),
                ("vNORMAL", 4),
                ("vTEXCOORD", 2),
                ("vTANGENT", 4),
                ("vBLENDINDICES", 4),
                ("vBLENDWEIGHT", 4),
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

        public void Render(Camera camera, RenderPass renderPass)
        {
            GL.UseProgram(shader.Program);
            GL.BindVertexArray(quadVao);
            GL.EnableVertexAttribArray(0);

            var uniformLocation = shader.GetUniformLocation("m_vTintColorSceneObject");
            if (uniformLocation > -1)
            {
                GL.Uniform4f(uniformLocation, Vector4.One);
            }

            uniformLocation = shader.GetUniformLocation("m_vTintColorDrawCall");
            if (uniformLocation > -1)
            {
                GL.Uniform3f(uniformLocation, Vector3.One);
            }

            var identity = Matrix4.Identity;

            uniformLocation = shader.GetUniformLocation("uProjectionViewMatrix");
            if (uniformLocation > -1)
            {
                GL.UniformMatrix4f(uniformLocation, false, identity);
            }

            uniformLocation = shader.GetUniformLocation("transform");
            if (uniformLocation > -1)
            {
                GL.UniformMatrix4f(uniformLocation, false, identity);
            }

            material.Render(shader);

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            material.PostRender();

            GL.BindVertexArray(VertexArrayHandle.Zero);
            GL.UseProgram(ProgramHandle.Zero);
        }

        public void Update(float frameTime)
        {
            throw new NotImplementedException();
        }
    }
}
