using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Renderers
{
    internal class RenderSprites : IParticleRenderer
    {
        private const string ShaderName = "vrf.particle.sprite";
        private const int VertexSize = 9;

        private Shader shader;
        private readonly VrfGuiContext guiContext;
        private readonly int quadVao;
        private readonly RenderTexture texture;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
        private readonly float minSize;
        private readonly float maxSize = 5000f;

        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider alphaScale = new LiteralNumberProvider(1f);

        private readonly bool additive;
        private readonly INumberProvider overbrightFactor = new LiteralNumberProvider(1);
        private readonly ParticleOrientation orientationType;

        private float[] rawVertices;
        private readonly QuadIndexBuffer quadIndices;
        private int vertexBufferHandle;

        private static bool wireframe;


        public RenderSprites(IKeyValueCollection keyValues, VrfGuiContext vrfGuiContext)
        {
            guiContext = vrfGuiContext;
            shader = vrfGuiContext.ShaderLoader.LoadShader(ShaderName);
            quadIndices = vrfGuiContext.QuadIndices;

            // The same quad is reused for all particles
            quadVao = SetupQuadBuffer();

            string textureName = null;

            if (keyValues.ContainsKey("m_hTexture"))
            {
                textureName = keyValues.GetProperty<string>("m_hTexture");
            }
            else if (keyValues.ContainsKey("m_vecTexturesInput"))
            {
                var textures = keyValues.GetArray("m_vecTexturesInput");

                if (textures.Length > 0)
                {
                    // TODO: Support more than one texture
                    textureName = textures[0].GetProperty<string>("m_hTexture");
                }
            }

            texture = vrfGuiContext.MaterialLoader.LoadTexture(textureName);

            additive = keyValues.GetProperty<bool>("m_bAdditive");
            if (keyValues.ContainsKey("m_flOverbrightFactor"))
            {
                overbrightFactor = keyValues.GetNumberProvider("m_flOverbrightFactor");
            }

            if (keyValues.ContainsKey("m_nOrientationType"))
            {
                orientationType = keyValues.GetEnumValue<ParticleOrientation>("m_nOrientationType");
            }

            if (keyValues.ContainsKey("m_flAnimationRate"))
            {
                animationRate = keyValues.GetFloatProperty("m_flAnimationRate");
            }

            if (keyValues.ContainsKey("m_flMinSize"))
            {
                minSize = keyValues.GetFloatProperty("m_flMinSize");
            }

            if (keyValues.ContainsKey("m_flMaxSize"))
            {
                maxSize = keyValues.GetFloatProperty("m_flMaxSize");
            }

            if (keyValues.ContainsKey("m_nAnimationType"))
            {
                animationType = keyValues.GetEnumValue<ParticleAnimationType>("m_nAnimationType");
            }

            if (keyValues.ContainsKey("m_flRadiusScale"))
            {
                radiusScale = keyValues.GetNumberProvider("m_flRadiusScale");
            }

            if (keyValues.ContainsKey("m_flAlphaScale"))
            {
                alphaScale = keyValues.GetNumberProvider("m_flAlphaScale");
            }
        }

        public void SetWireframe(bool isWireframe)
        {
            wireframe = isWireframe;
            // Solid color
            shader.SetUniform1("isWireframe", isWireframe);
        }

        private int SetupQuadBuffer()
        {
            GL.UseProgram(shader.Program);

            // Create and bind VAO
            var vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            vertexBufferHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferHandle);

            var stride = sizeof(float) * VertexSize;
            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, 0);
            var colorAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexColor");
            GL.VertexAttribPointer(colorAttributeLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);
            var uvAttributeLocation = GL.GetAttribLocation(shader.Program, "aTexCoords");
            GL.VertexAttribPointer(uvAttributeLocation, 2, VertexAttribPointerType.Float, false, stride, sizeof(float) * 7);

            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.EnableVertexAttribArray(colorAttributeLocation);
            GL.EnableVertexAttribArray(uvAttributeLocation);

            GL.BindVertexArray(0);

            return vao;
        }

        private void EnsureSpaceForVertices(int count)
        {
            var numFloats = count * VertexSize;

            if (rawVertices == null)
            {
                rawVertices = new float[numFloats];
            }
            else if (rawVertices.Length < numFloats)
            {
                var nextSize = ((count / 64) + 1) * 64 * VertexSize;
                Array.Resize(ref rawVertices, nextSize);
            }
        }

        private void UpdateVertices(ParticleBag particleBag, ParticleSystemRenderState systemRenderState, Matrix4x4 modelViewMatrix)
        {
            var particles = particleBag.LiveParticles;

            // Create billboarding rotation (always facing camera)
            Matrix4x4.Decompose(modelViewMatrix, out _, out var modelViewRotation, out _);
            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            // Update vertex buffer
            EnsureSpaceForVertices(particleBag.Count * 4);
            for (var i = 0; i < particleBag.Count; ++i)
            {
                var radiusScale = this.radiusScale.NextNumber(particles[i], systemRenderState);

                // Positions
                var modelMatrix = orientationType == ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED
                    ? particles[i].GetRotationMatrix() * billboardMatrix * particles[i].GetTransformationMatrix(radiusScale)
                    : particles[i].GetRotationMatrix() * particles[i].GetTransformationMatrix(radiusScale);

                var tl = Vector4.Transform(new Vector4(-1, -1, 0, 1), modelMatrix);
                var bl = Vector4.Transform(new Vector4(-1, 1, 0, 1), modelMatrix);
                var br = Vector4.Transform(new Vector4(1, 1, 0, 1), modelMatrix);
                var tr = Vector4.Transform(new Vector4(1, -1, 0, 1), modelMatrix);

                var quadStart = i * VertexSize * 4;
                rawVertices[quadStart + 0] = tl.X;
                rawVertices[quadStart + 1] = tl.Y;
                rawVertices[quadStart + 2] = tl.Z;
                rawVertices[quadStart + (VertexSize * 1) + 0] = bl.X;
                rawVertices[quadStart + (VertexSize * 1) + 1] = bl.Y;
                rawVertices[quadStart + (VertexSize * 1) + 2] = bl.Z;
                rawVertices[quadStart + (VertexSize * 2) + 0] = br.X;
                rawVertices[quadStart + (VertexSize * 2) + 1] = br.Y;
                rawVertices[quadStart + (VertexSize * 2) + 2] = br.Z;
                rawVertices[quadStart + (VertexSize * 3) + 0] = tr.X;
                rawVertices[quadStart + (VertexSize * 3) + 1] = tr.Y;
                rawVertices[quadStart + (VertexSize * 3) + 2] = tr.Z;

                var alphaScale = this.alphaScale.NextNumber(particles[i], systemRenderState);
                // Colors
                for (var j = 0; j < 4; ++j)
                {
                    rawVertices[quadStart + (VertexSize * j) + 3] = particles[i].Color.X;
                    rawVertices[quadStart + (VertexSize * j) + 4] = particles[i].Color.Y;
                    rawVertices[quadStart + (VertexSize * j) + 5] = particles[i].Color.Z;
                    rawVertices[quadStart + (VertexSize * j) + 6] = particles[i].Alpha * alphaScale;
                }

                // UVs
                var spriteSheetData = texture.SpritesheetData;
                if (spriteSheetData != null && spriteSheetData.Sequences.Length > 0 && spriteSheetData.Sequences[0].Frames.Length > 0)
                {
                    var sequence = spriteSheetData.Sequences[particles[i].Sequence % spriteSheetData.Sequences.Length];

                    var animationTime = animationType switch
                    {
                        ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE => particles[i].Age,
                        ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => particles[i].NormalizedAge,
                        ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES => particles[i].Age, // literally dont know what to do with this one
                        _ => particles[i].Age,
                    };

                    var currentFrame = sequence.Frames[(int)Math.Floor(sequence.Frames.Length * animationRate * animationTime) % sequence.Frames.Length];
                    var currentImage = currentFrame.Images[0]; // TODO: Support more than one image per frame?

                    // Lerp frame coords and size
                    var offset = currentImage.CroppedMin;
                    var scale = currentImage.CroppedMax - currentImage.CroppedMin;

                    rawVertices[quadStart + (VertexSize * 0) + 7] = offset.X;
                    rawVertices[quadStart + (VertexSize * 0) + 8] = offset.Y + scale.Y;
                    rawVertices[quadStart + (VertexSize * 1) + 7] = offset.X;
                    rawVertices[quadStart + (VertexSize * 1) + 8] = offset.Y;
                    rawVertices[quadStart + (VertexSize * 2) + 7] = offset.X + scale.X;
                    rawVertices[quadStart + (VertexSize * 2) + 8] = offset.Y;
                    rawVertices[quadStart + (VertexSize * 3) + 7] = offset.X + scale.X;
                    rawVertices[quadStart + (VertexSize * 3) + 8] = offset.Y + scale.Y;
                }
                else
                {
                    rawVertices[quadStart + (VertexSize * 0) + 7] = 0;
                    rawVertices[quadStart + (VertexSize * 0) + 8] = 1;
                    rawVertices[quadStart + (VertexSize * 1) + 7] = 0;
                    rawVertices[quadStart + (VertexSize * 1) + 8] = 0;
                    rawVertices[quadStart + (VertexSize * 2) + 7] = 1;
                    rawVertices[quadStart + (VertexSize * 2) + 8] = 0;
                    rawVertices[quadStart + (VertexSize * 3) + 7] = 1;
                    rawVertices[quadStart + (VertexSize * 3) + 8] = 1;
                }
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, particleBag.Count * VertexSize * 4 * sizeof(float), rawVertices, BufferUsageHint.DynamicDraw);
        }

        public void Render(ParticleBag particleBag, ParticleSystemRenderState systemRenderState, Matrix4x4 viewProjectionMatrix, Matrix4x4 modelViewMatrix)
        {
            if (particleBag.Count == 0)
            {
                return;
            }

            // Update vertex buffer
            UpdateVertices(particleBag, systemRenderState, modelViewMatrix);

            // Draw it
            GL.Enable(EnableCap.Blend);
            GL.UseProgram(shader.Program);

            if (additive)
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            GL.BindVertexArray(quadVao);
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferHandle);

            // set texture unit 0 as uTexture uniform
            shader.SetTexture(0, "uTexture", texture.Handle);

            shader.SetUniform4x4("uProjectionViewMatrix", viewProjectionMatrix);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            shader.SetUniform1("uOverbrightFactor", overbrightFactor.NextNumber());

            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, quadIndices.GLHandle);

            if (wireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            }

            // DRAW
            GL.DrawElements(BeginMode.Triangles, particleBag.Count * 6, DrawElementsType.UnsignedShort, 0);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            GL.Enable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(true);

            GL.BindVertexArray(0);
            GL.UseProgram(0);

            if (additive)
            {
                GL.BlendEquation(BlendEquationMode.FuncAdd);
            }

            GL.Disable(EnableCap.Blend);

            if (wireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
        }

        public IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public void SetRenderMode(string renderMode)
        {
            var parameters = new Dictionary<string, byte>();

            if (renderMode != null && shader.RenderModes.Contains(renderMode))
            {
                parameters.Add($"renderMode_{renderMode}", 1);
            }

            shader = guiContext.ShaderLoader.LoadShader(ShaderName, parameters);
        }
    }
}
