using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class PostProcessRenderer
    {
        private VrfGuiContext guiContext;
        private int vao;
        private Shader shader;

        public RenderTexture BlueNoise;
        private readonly Random random = new();

        public PostProcessRenderer(VrfGuiContext guiContext)
        {
            this.guiContext = guiContext;
        }

        public void Load()
        {
            shader = guiContext.ShaderLoader.LoadShader("vrf.post_process");
            GL.CreateVertexArrays(1, out vao);
        }

        private void SetPostProcessUniforms(Shader shader, TonemapSettings TonemapSettings)
        {
            // Randomize dither offset every frame
            var ditherOffset = new Vector2(random.NextSingle(), random.NextSingle());

            // Dither by one 255th of frame color originally. Modified to be twice that, because it looks better.
            shader.SetUniform4("g_vBlueNoiseDitherParams", new Vector4(ditherOffset, 1.0f / 256.0f, 2.0f / 255.0f));

            shader.SetUniform1("g_flExposureBiasScaleFactor", MathF.Pow(2.0f, TonemapSettings.ExposureBias));
            shader.SetUniform1("g_flShoulderStrength", TonemapSettings.ShoulderStrength);
            shader.SetUniform1("g_flLinearStrength", TonemapSettings.LinearStrength);
            shader.SetUniform1("g_flLinearAngle", TonemapSettings.LinearAngle);
            shader.SetUniform1("g_flToeStrength", TonemapSettings.ToeStrength);
            shader.SetUniform1("g_flToeNum", TonemapSettings.ToeNum);
            shader.SetUniform1("g_flToeDenom", TonemapSettings.ToeDenom);
            shader.SetUniform1("g_flWhitePointScale", 1.0f / TonemapSettings.ApplyTonemapping(TonemapSettings.WhitePoint));
        }

        // In CS2 Blue Noise is done optionally in msaa_resolve

        // we should have a shared FullscreenQuadRenderer class
        public void Render(PostProcessState postProcessState, Framebuffer colorBuffer, float tonemapScalar)
        {
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);

            GL.UseProgram(shader.Program);

            // Bind textures
            shader.SetTexture(0, "g_tColorBuffer", colorBuffer.Color);
            shader.SetTexture(1, "g_tColorCorrection", postProcessState.ColorCorrectionLUT ?? guiContext.MaterialLoader.GetErrorTexture()); // todo: error postprocess texture
            shader.SetTexture(2, "g_tBlueNoise", BlueNoise);

            shader.SetUniform1("g_flToneMapScalarLinear", tonemapScalar);
            SetPostProcessUniforms(shader, postProcessState.TonemapSettings);

            var invDimensions = 1.0f / postProcessState.ColorCorrectionLutDimensions;
            var invRange = new Vector2(1.0f - invDimensions, 0.5f * invDimensions);
            shader.SetUniform2("g_vColorCorrectionColorRange", invRange);
            shader.SetUniform1("g_flColorCorrectionDefaultWeight", (postProcessState.NumLutsActive > 0) ? postProcessState.ColorCorrectionWeight : 0f);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);

            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
        }
    }
}
