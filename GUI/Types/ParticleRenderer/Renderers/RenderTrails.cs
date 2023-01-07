using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Renderers
{
    internal class RenderTrails : IParticleRenderer
    {
        private const string ShaderName = "vrf.particle.trail";

        private Shader shader;
        private readonly VrfGuiContext guiContext;
        private readonly VertexArrayHandle quadVao;
        private readonly TextureHandle glTexture;

        private readonly Texture.SpritesheetData spriteSheetData;
        private readonly float animationRate = 0.1f;

        private readonly bool additive;
        private readonly INumberProvider overbrightFactor = new LiteralNumberProvider(1);
        private readonly long orientationType;

        private readonly float finalTextureScaleU = 1f;
        private readonly float finalTextureScaleV = 1f;

        private readonly float maxLength = 2000f;
        private readonly float lengthFadeInTime;

        public RenderTrails(IKeyValueCollection keyValues, VrfGuiContext vrfGuiContext)
        {
            guiContext = vrfGuiContext;
            shader = vrfGuiContext.ShaderLoader.LoadShader(ShaderName, new Dictionary<string, bool>());

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

            if (textureName != null)
            {
                var textureSetup = LoadTexture(textureName, vrfGuiContext);
                glTexture = textureSetup.TextureIndex;
                spriteSheetData = textureSetup.TextureData?.GetSpriteSheetData();
            }
            else
            {
                glTexture = vrfGuiContext.MaterialLoader.GetErrorTexture();
            }

            additive = keyValues.GetProperty<bool>("m_bAdditive");
            if (keyValues.ContainsKey("m_flOverbrightFactor"))
            {
                overbrightFactor = keyValues.GetNumberProvider("m_flOverbrightFactor");
            }

            if (keyValues.ContainsKey("m_nOrientationType"))
            {
                orientationType = keyValues.GetIntegerProperty("m_nOrientationType");
            }

            if (keyValues.ContainsKey("m_flAnimationRate"))
            {
                animationRate = keyValues.GetFloatProperty("m_flAnimationRate");
            }

            if (keyValues.ContainsKey("m_flFinalTextureScaleU"))
            {
                finalTextureScaleU = keyValues.GetFloatProperty("m_flFinalTextureScaleU");
            }

            if (keyValues.ContainsKey("m_flFinalTextureScaleV"))
            {
                finalTextureScaleV = keyValues.GetFloatProperty("m_flFinalTextureScaleV");
            }

            if (keyValues.ContainsKey("m_flMaxLength"))
            {
                maxLength = keyValues.GetFloatProperty("m_flMaxLength");
            }

            if (keyValues.ContainsKey("m_flLengthFadeInTime"))
            {
                lengthFadeInTime = keyValues.GetFloatProperty("m_flLengthFadeInTime");
            }
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
                -1.0f, -1.0f, 0.0f,
                -1.0f, 1.0f, 0.0f,
                1.0f, -1.0f, 0.0f,
                1.0f, 1.0f, 0.0f,
            };

            GL.BufferData(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);

            GL.EnableVertexAttribArray(0);

            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, 0, 0);

            GL.BindVertexArray(VertexArrayHandle.Zero);

            return vao;
        }

        private static (TextureHandle TextureIndex, Texture TextureData) LoadTexture(string textureName, VrfGuiContext vrfGuiContext)
        {
            var textureResource = vrfGuiContext.LoadFileByAnyMeansNecessary(textureName + "_c");

            if (textureResource == null)
            {
                return (vrfGuiContext.MaterialLoader.GetErrorTexture(), null);
            }

            return (vrfGuiContext.MaterialLoader.LoadTexture(textureName), (Texture)textureResource.DataBlock);
        }

        public void Render(ParticleBag particleBag, Matrix4x4 viewProjectionMatrix, Matrix4x4 modelViewMatrix)
        {
            var particles = particleBag.LiveParticles;

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

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2d, glTexture);

            GL.Uniform1i(shader.GetUniformLocation("uTexture"), 0); // set texture unit 0 as uTexture uniform

            var otkProjection = viewProjectionMatrix.ToOpenTK();
            GL.UniformMatrix4f(shader.GetUniformLocation("uProjectionViewMatrix"), false, otkProjection);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            GL.Uniform1d(shader.GetUniformLocation("uOverbrightFactor"), overbrightFactor.NextNumber());

            var modelMatrixLocation = shader.GetUniformLocation("uModelMatrix");
            var colorLocation = shader.GetUniformLocation("uColor");
            var alphaLocation = shader.GetUniformLocation("uAlpha");
            var uvOffsetLocation = shader.GetUniformLocation("uUvOffset");
            var uvScaleLocation = shader.GetUniformLocation("uUvScale");

            // Create billboarding rotation (always facing camera)
            Matrix4x4.Decompose(modelViewMatrix, out _, out var modelViewRotation, out _);
            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            for (var i = 0; i < particles.Length; ++i)
            {
                var position = new Vector3(particles[i].Position.X, particles[i].Position.Y, particles[i].Position.Z);
                var previousPosition = new Vector3(particles[i].PositionPrevious.X, particles[i].PositionPrevious.Y, particles[i].PositionPrevious.Z);
                var difference = previousPosition - position;
                var direction = Vector3.Normalize(difference);

                var midPoint = position + (0.5f * difference);

                // Trail width = radius
                // Trail length = distance between current and previous times trail length divided by 2 (because the base particle is 2 wide)
                var length = Math.Min(maxLength, particles[i].TrailLength * difference.Length() / 2f);
                var t = 1 - (particles[i].Lifetime / particles[i].ConstantLifetime);
                var animatedLength = t >= lengthFadeInTime
                    ? length
                    : t * length / lengthFadeInTime;
                var scaleMatrix = Matrix4x4.CreateScale(particles[i].Radius, animatedLength, 1);

                // Center the particle at the midpoint between the two points
                var translationMatrix = Matrix4x4.CreateTranslation(Vector3.UnitY * animatedLength);

                // Calculate rotation matrix

                var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, direction));
                var angle = (float)Math.Acos(direction.Y);
                var rotationMatrix = Matrix4x4.CreateFromAxisAngle(axis, angle);

                var modelMatrix =
                    orientationType == 0 ? Matrix4x4.Multiply(scaleMatrix, Matrix4x4.Multiply(translationMatrix, rotationMatrix))
                    : particles[i].GetTransformationMatrix();

                // Position/Radius uniform
                var otkModelMatrix = modelMatrix.ToOpenTK();
                GL.UniformMatrix4f(modelMatrixLocation, false, otkModelMatrix);

                if (spriteSheetData != null && spriteSheetData.Sequences.Length > 0 && spriteSheetData.Sequences[0].Frames.Length > 0)
                {
                    var sequence = spriteSheetData.Sequences[0];

                    var particleTime = particles[i].ConstantLifetime - particles[i].Lifetime;
                    var frame = particleTime * sequence.FramesPerSecond * animationRate;

                    var currentFrame = sequence.Frames[(int)Math.Floor(frame) % sequence.Frames.Length];
                    var currentImage = currentFrame.Images[0]; // TODO: Support more than one image per frame?

                    // Lerp frame coords and size
                    var subFrameTime = frame % 1.0f;
                    var offset = (currentImage.CroppedMin * (1 - subFrameTime)) + (currentImage.UncroppedMin * subFrameTime);
                    var scale = ((currentImage.CroppedMax - currentImage.CroppedMin) * (1 - subFrameTime))
                            + ((currentImage.UncroppedMax - currentImage.UncroppedMin) * subFrameTime);

                    GL.Uniform2f(uvOffsetLocation, offset.X, offset.Y);
                    GL.Uniform2f(uvScaleLocation, scale.X * finalTextureScaleU, scale.Y * finalTextureScaleV);
                }
                else
                {
                    GL.Uniform2f(uvOffsetLocation, 1f, 1f);
                    GL.Uniform2f(uvScaleLocation, finalTextureScaleU, finalTextureScaleV);
                }

                // Color uniform
                GL.Uniform3f(colorLocation, particles[i].Color.X, particles[i].Color.Y, particles[i].Color.Z);

                GL.Uniform1f(alphaLocation, particles[i].Alpha * particles[i].AlphaAlternate);

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            }

            GL.BindVertexArray(VertexArrayHandle.Zero);
            GL.UseProgram(ProgramHandle.Zero);

            if (additive)
            {
                GL.BlendEquation(BlendEquationModeEXT.FuncAdd);
            }

            GL.Disable(EnableCap.Blend);
        }

        public IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public void SetRenderMode(string renderMode)
        {
            var parameters = new Dictionary<string, bool>();

            if (renderMode != null && shader.RenderModes.Contains(renderMode))
            {
                parameters.Add($"renderMode_{renderMode}", true);
            }

            shader = guiContext.ShaderLoader.LoadShader(ShaderName, parameters);
        }
    }
}
