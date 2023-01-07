using System;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace GUI.Types.Renderer
{
    public class GPUMeshBuffers
    {
        public struct Buffer
        {
#pragma warning disable CA1051 // Do not declare visible instance fields
            public BufferHandle Handle;
            public long Size;
#pragma warning restore CA1051 // Do not declare visible instance fields
        }

        public Buffer[] VertexBuffers { get; private set; }
        public Buffer[] IndexBuffers { get; private set; }

        public GPUMeshBuffers(VBIB vbib)
        {
            VertexBuffers = new Buffer[vbib.VertexBuffers.Count];
            IndexBuffers = new Buffer[vbib.IndexBuffers.Count];

            for (var i = 0; i < vbib.VertexBuffers.Count; i++)
            {
                VertexBuffers[i].Handle = GL.GenBuffer();
                GL.BindBuffer(BufferTargetARB.ArrayBuffer, VertexBuffers[i].Handle);
                GL.BufferData(BufferTargetARB.ArrayBuffer, vbib.VertexBuffers[i].Data, BufferUsageARB.StaticDraw);

                GL.GetBufferParameteri64(BufferTargetARB.ArrayBuffer, BufferPNameARB.BufferSize, out VertexBuffers[i].Size);
            }

            for (var i = 0; i < vbib.IndexBuffers.Count; i++)
            {
                IndexBuffers[i].Handle = GL.GenBuffer();
                GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, IndexBuffers[i].Handle);
                GL.BufferData(BufferTargetARB.ElementArrayBuffer, vbib.IndexBuffers[i].Data, BufferUsageARB.StaticDraw);

                GL.GetBufferParameteri64(BufferTargetARB.ElementArrayBuffer, BufferPNameARB.BufferSize, out IndexBuffers[i].Size);
            }
        }
    }
}
