using System.Collections.Generic;
using System.Numerics;

namespace GUI.Types.ParticleRenderer.Renderers
{
    abstract class ParticleFunctionRenderer : ParticleFunction
    {
        public abstract void Render(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Matrix4x4 viewProjectionMatrix, Matrix4x4 modelViewMatrix);
        public abstract void SetRenderMode(string renderMode);
        public abstract IEnumerable<string> GetSupportedRenderModes();
        public abstract void SetWireframe(bool wireframe);
    }
}
