using System.Numerics;

namespace GUI.Types.ParticleRenderer.Operators
{
    class MaxVelocity : IParticleOperator
    {
        private readonly float maxVelocity;
        private readonly int overrideCP = -1;
        private readonly int overrideCPField;

        public MaxVelocity(ParticleDefinitionParser parse)
        {
            maxVelocity = parse.Float("m_flMaxVelocity", maxVelocity);
            overrideCP = parse.Int32("m_nOverrideCP", overrideCP);
            overrideCPField = parse.Int32("m_nOverrideCPField", overrideCPField);
        }

        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var maxVelocity = this.maxVelocity;
            if (overrideCP > -1)
            {
                var controlPoint = particleSystemState.GetControlPoint(overrideCP);

                maxVelocity = controlPoint.Position.GetComponent(overrideCPField);
            }

            foreach (ref var particle in particles.Current)
            {
                if (particle.Velocity.Length() > maxVelocity)
                {
                    particle.Velocity = Vector3.Normalize(particle.Velocity) * maxVelocity;
                }
            }
        }
    }
}
