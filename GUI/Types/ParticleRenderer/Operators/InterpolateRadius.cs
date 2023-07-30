using GUI.Utils;
using System;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class InterpolateRadius : IParticleOperator
    {
        private readonly float startTime;
        private readonly float endTime = 1;
        private readonly INumberProvider startScale = new LiteralNumberProvider(1);
        private readonly INumberProvider endScale = new LiteralNumberProvider(1);
        private readonly INumberProvider bias = new LiteralNumberProvider(0);


        public InterpolateRadius(ParticleDefinitionParser parse)
        {
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            startScale = parse.NumberProvider("m_flStartScale", startScale);
            endScale = parse.NumberProvider("m_flEndScale", endScale);
            bias = parse.NumberProvider("m_flBias", bias);
        }

        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var time = particle.NormalizedAge;

                if (time >= startTime && time <= endTime)
                {
                    var startScale = this.startScale.NextNumber(ref particle, particleSystemState);
                    var endScale = this.endScale.NextNumber(ref particle, particleSystemState);

                    var timeScale = MathUtils.Remap(time, startTime, endTime);
                    timeScale = MathF.Pow(timeScale, 1.0f - bias.NextNumber(ref particle, particleSystemState)); // apply bias to timescale
                    var radiusScale = MathUtils.Lerp(timeScale, startScale, endScale);

                    particle.Radius = particle.GetInitialScalar(particles, ParticleField.Radius) * radiusScale;
                }
            }
        }
    }
}
