using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class CreateWithinSphere : IParticleInitializer
    {
        private readonly INumberProvider radiusMin = new LiteralNumberProvider(0);
        private readonly INumberProvider radiusMax = new LiteralNumberProvider(0);
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0);
        private readonly IVectorProvider localCoordinateSystemSpeedMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider localCoordinateSystemSpeedMax = new LiteralVectorProvider(Vector3.Zero);

        public CreateWithinSphere(ParticleDefinitionParser parse)
        {
            radiusMin = parse.NumberProvider("m_fRadiusMin", radiusMin);
            radiusMax = parse.NumberProvider("m_fRadiusMax", radiusMax);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
            localCoordinateSystemSpeedMin = parse.VectorProvider("m_LocalCoordinateSystemSpeedMin", localCoordinateSystemSpeedMin);
            localCoordinateSystemSpeedMax = parse.VectorProvider("m_LocalCoordinateSystemSpeedMax", localCoordinateSystemSpeedMax);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var randomVector = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, new Vector3(-1), new Vector3(1));

            // Normalize
            var direction = Vector3.Normalize(randomVector);

            var distance = ParticleCollection.RandomBetween(
                particle.ParticleID,
                radiusMin.NextNumber(ref particle, particleSystemState),
                radiusMax.NextNumber(ref particle, particleSystemState));

            var speed = ParticleCollection.RandomBetween(
                particle.ParticleID,
                speedMin.NextNumber(ref particle, particleSystemState),
                speedMax.NextNumber(ref particle, particleSystemState));

            var localCoordinateSystemSpeed = ParticleCollection.RandomBetweenPerComponent(
                particle.ParticleID,
                localCoordinateSystemSpeedMin.NextVector(ref particle, particleSystemState),
                localCoordinateSystemSpeedMax.NextVector(ref particle, particleSystemState));

            particle.Position += direction * distance;
            particle.Velocity = (direction * speed) + localCoordinateSystemSpeed;

            return particle;
        }
    }
}
