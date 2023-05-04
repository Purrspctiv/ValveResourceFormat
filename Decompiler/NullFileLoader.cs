using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

namespace Decompiler
{
    public class NullFileLoader : IFileLoader
    {
        public Resource LoadFile(string file) => null;
        public ShaderFile LoadShader(string shaderName) => null;
    }
}
