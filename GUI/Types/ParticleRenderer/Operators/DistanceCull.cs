using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    // Cull when crossing sphere
    class DistanceCull : IParticleOperator
    {
        private readonly int cp;
        private readonly float distance;
        private readonly Vector3 PointOffset = Vector3.Zero;
        private readonly bool cullInside;
        public DistanceCull(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPoint", cp);

            PointOffset = parse.Vector3("m_vecPointOffset", PointOffset);

            distance = parse.Float("m_flDistance", distance);

            cullInside = parse.Boolean("m_bCullInside", cullInside);
        }
        private bool CulledBySphere(Vector3 position, ParticleSystemRenderState particleSystemState)
        {
            var sphereOrigin = particleSystemState.GetControlPoint(cp).Position + PointOffset;

            var distanceFromEdge = Vector3.Distance(sphereOrigin, position) - distance;

            return cullInside
                ? distanceFromEdge < 0
                : distanceFromEdge > 0;
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                if (CulledBySphere(particle.Position, particleSystemState))
                {
                    particle.Kill();
                }
            }
        }
    }
}
