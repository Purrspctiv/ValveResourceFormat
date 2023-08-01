namespace GUI.Types.ParticleRenderer.Operators
{
    class Decay : ParticleFunctionOperator
    {
        public Decay()
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Age > particle.Lifetime)
                {
                    particle.Kill();
                }
            }
        }
    }
}
