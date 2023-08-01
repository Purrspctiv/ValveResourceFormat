using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class ColorInterpolateRandom : ParticleFunctionOperator
    {
        private readonly Vector3 colorFadeMin = Vector3.One;
        private readonly Vector3 colorFadeMax = Vector3.One;
        private readonly float fadeStartTime;
        private readonly float fadeEndTime = 1f;
        private readonly ParticleField FieldOutput = ParticleField.Color;
        private readonly bool easeInOut;

        public ColorInterpolateRandom(ParticleDefinitionParser parse) : base(parse)
        {
            if (parse.Data.ContainsKey("m_ColorFadeMin"))
            {
                var vectorValues = parse.Data.GetIntegerArray("m_ColorFadeMin");
                colorFadeMin = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
            }

            if (parse.Data.ContainsKey("m_ColorFadeMax"))
            {
                var vectorValues = parse.Data.GetIntegerArray("m_ColorFadeMax");
                colorFadeMax = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
            }

            fadeStartTime = parse.Float("m_flFadeStartTime", fadeStartTime);
            fadeEndTime = parse.Float("m_flFadeEndTime", fadeEndTime);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            easeInOut = parse.Boolean("m_bEaseInOut", easeInOut);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                // TODO: Consistent rng
                var newColor = easeInOut
                    ? ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, colorFadeMin, colorFadeMax)
                    : ParticleCollection.RandomBetween(particle.ParticleID, colorFadeMin, colorFadeMax);

                var time = particle.NormalizedAge;

                if (time >= fadeStartTime && time <= fadeEndTime)
                {
                    var t = MathUtils.Remap(time, fadeStartTime, fadeEndTime);

                    // Interpolate from constant color to fade color
                    particle.SetVector(FieldOutput, MathUtils.Lerp(t, particle.GetInitialVector(particles, ParticleField.Color), newColor));
                }
            }
        }
    }
}