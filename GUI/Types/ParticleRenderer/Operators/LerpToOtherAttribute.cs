using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class LerpToOtherAttribute : IParticleOperator
    {
        private readonly ParticleField FieldInput = ParticleField.Color;
        private readonly ParticleField FieldOutput = ParticleField.Color;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1.0f);

        private readonly bool skip;
        public LerpToOtherAttribute(ParticleDefinitionParser parse)
        {
            FieldInput = parse.ParticleField("m_nFieldInput", FieldInput);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);

            // If the two fields are different types, the operator does nothing.
            skip = FieldInput.FieldType() != FieldOutput.FieldType();
        }

        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // We don't have to do weird stuff with this one because it doesn't have the option to set the initial.
            if (!skip)
            {
                if (FieldInput.FieldType() == "vector")
                {
                    foreach (ref var particle in particles.Current)
                    {
                        var interp = interpolation.NextNumber(ref particle, particleSystemState);
                        var blend = MathUtils.Lerp(interp, particle.GetVector(FieldOutput), particle.GetVector(FieldInput));
                        particle.SetVector(FieldOutput, blend);
                    }
                }
                else if (FieldInput.FieldType() == "float")
                {
                    foreach (ref var particle in particles.Current)
                    {
                        var interp = interpolation.NextNumber(ref particle, particleSystemState);
                        var blend = MathUtils.Lerp(interp, particle.GetScalar(FieldOutput), particle.GetScalar(FieldInput));
                        particle.SetScalar(FieldOutput, blend);
                    }
                }
            }
        }
    }
}
