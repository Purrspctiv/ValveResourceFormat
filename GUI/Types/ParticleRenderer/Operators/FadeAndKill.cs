using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeAndKill : IParticleOperator
    {
        private readonly float startFadeInTime;
        private readonly float endFadeInTime = 0.5f;
        private readonly float startFadeOutTime = 0.5f;
        private readonly float endFadeOutTime = 1f;

        private readonly float startAlpha = 1f;
        private readonly float endAlpha;

        public FadeAndKill(ParticleDefinitionParser parse)
        {
            startFadeInTime = parse.Float("m_flStartFadeInTime", startFadeInTime);
            endFadeInTime = parse.Float("m_flEndFadeInTime", endFadeInTime);
            startFadeOutTime = parse.Float("m_flStartFadeOutTime", startFadeOutTime);
            endFadeOutTime = parse.Float("m_flEndFadeOutTime", endFadeOutTime);
            startAlpha = parse.Float("m_flStartAlpha", startAlpha);
            endAlpha = parse.Float("m_flEndAlpha", endAlpha);
        }

        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var time = particle.NormalizedAge;

                // If fading in
                if (time >= startFadeInTime && time <= endFadeInTime)
                {
                    var blend = MathUtils.Remap(time, startFadeInTime, endFadeInTime);

                    // Interpolate from startAlpha to constantAlpha
                    particle.Alpha = MathUtils.Lerp(blend, startAlpha, particle.GetInitialScalar(particles, ParticleField.Alpha));
                }

                // If fading out
                if (time >= startFadeOutTime && time <= endFadeOutTime)
                {
                    var blend = MathUtils.Remap(time, startFadeOutTime, endFadeOutTime);

                    // Interpolate from constantAlpha to end alpha
                    particle.Alpha = MathUtils.Lerp(blend, particle.GetInitialScalar(particles, ParticleField.Alpha), endAlpha);
                }

                if (time >= endFadeOutTime)
                {
                    particle.Kill();
                }
            }
        }
    }
}
