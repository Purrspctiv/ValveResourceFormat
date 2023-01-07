using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace GUI.Types.Renderer
{
    public class MaterialLoader
    {
        private readonly Dictionary<string, RenderMaterial> Materials = new();
        private readonly VrfGuiContext VrfGuiContext;
        private TextureHandle ErrorTextureID = TextureHandle.Zero;
        public static int MaxTextureMaxAnisotropy { get; set; }

        public MaterialLoader(VrfGuiContext guiContext)
        {
            VrfGuiContext = guiContext;
        }

        public RenderMaterial GetMaterial(string name)
        {
            // HL:VR has a world node that has a draw call with no material
            if (name == null)
            {
                return GetErrorMaterial();
            }

            if (Materials.ContainsKey(name))
            {
                return Materials[name];
            }

            var resource = VrfGuiContext.LoadFileByAnyMeansNecessary(name + "_c");
            var mat = LoadMaterial(resource);

            Materials.Add(name, mat);

            return mat;
        }

        public RenderMaterial LoadMaterial(Resource resource)
        {
            if (resource == null)
            {
                return GetErrorMaterial();
            }

            var mat = new RenderMaterial((VrfMaterial)resource.DataBlock);

            foreach (var textureReference in mat.Material.TextureParams)
            {
                var key = textureReference.Key;

                mat.Textures[key] = LoadTexture(textureReference.Value);
            }

            if (mat.Material.IntParams.ContainsKey("F_SOLID_COLOR") && mat.Material.IntParams["F_SOLID_COLOR"] == 1)
            {
                var a = mat.Material.VectorParams["g_vColorTint"];

                mat.Textures["g_tColor"] = GenerateColorTexture(1, 1, new[] { a.X, a.Y, a.Z, a.W });
            }

            if (!mat.Textures.ContainsKey("g_tColor"))
            {
                mat.Textures["g_tColor"] = GetErrorTexture();
            }

            // Since our shaders only use g_tColor, we have to find at least one texture to use here
            if (mat.Textures["g_tColor"] == GetErrorTexture())
            {
                var namesToTry = new[] { "g_tColor2", "g_tColor1", "g_tColorA", "g_tColorB", "g_tColorC" };

                foreach (var name in namesToTry)
                {
                    if (mat.Textures.ContainsKey(name))
                    {
                        mat.Textures["g_tColor"] = mat.Textures[name];
                        break;
                    }
                }
            }

            // Set default values for scale and positions
            if (!mat.Material.VectorParams.ContainsKey("g_vTexCoordScale"))
            {
                mat.Material.VectorParams["g_vTexCoordScale"] = Vector4.One;
            }

            if (!mat.Material.VectorParams.ContainsKey("g_vTexCoordOffset"))
            {
                mat.Material.VectorParams["g_vTexCoordOffset"] = Vector4.Zero;
            }

            if (!mat.Material.VectorParams.ContainsKey("g_vColorTint"))
            {
                mat.Material.VectorParams["g_vColorTint"] = Vector4.One;
            }

            return mat;
        }

        public TextureHandle LoadTexture(string name)
        {
            var textureResource = VrfGuiContext.LoadFileByAnyMeansNecessary(name + "_c");

            if (textureResource == null)
            {
                return GetErrorTexture();
            }

            return LoadTexture(textureResource);
        }

        public TextureHandle LoadTexture(Resource textureResource)
        {
            var tex = (Texture)textureResource.DataBlock;

            var id = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2d, id);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMaxLevel, tex.NumMipLevels - 1);

            var internalFormat = GetPixelInternalFormat(tex.Format);
            var format = GetInternalFormat(tex.Format);

            if (!format.HasValue && !internalFormat.HasValue)
            {
                Console.Error.WriteLine($"Don't support {tex.Format} but don't want to crash either. Using error texture!");
                return GetErrorTexture();
            }

            var buffer = ArrayPool<byte>.Shared.Rent(tex.GetBiggestBufferSize());

            try
            {
                foreach (var (i, width, height, bufferSize) in tex.GetEveryMipLevelTexture(buffer))
                {
                    if (internalFormat.HasValue)
                    {
                        var pixelFormat = GetPixelFormat(tex.Format);
                        var pixelType = GetPixelType(tex.Format);

                        GL.TexImage2D(TextureTarget.Texture2d, i, internalFormat.Value, width, height, 0, pixelFormat, pixelType, buffer);
                    }
                    else
                    {
                        GL.CompressedTexImage2D(TextureTarget.Texture2d, i, format.Value, width, height, 0, bufferSize, buffer);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // Dispose texture otherwise we run out of memory
            // TODO: This might conflict when opening multiple files due to shit caching
            textureResource.Dispose();

            if (MaxTextureMaxAnisotropy >= 4)
            {
                GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMaxAnisotropy, MaxTextureMaxAnisotropy);
                GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            }
            else
            {
                GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            }

            var clampModeS = tex.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPS)
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;
            var clampModeT = tex.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPT)
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;

            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapS, (int)clampModeS);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapT, (int)clampModeT);

            GL.BindTexture(TextureTarget.Texture2d, TextureHandle.Zero);

            return id;
        }

        private static InternalFormat? GetInternalFormat(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.DXT1 => InternalFormat.CompressedRgbaS3tcDxt1Ext,
                VTexFormat.DXT5 => InternalFormat.CompressedRgbaS3tcDxt5Ext,
                VTexFormat.ETC2 => InternalFormat.CompressedRgb8Etc2,
                VTexFormat.ETC2_EAC => InternalFormat.CompressedRgba8Etc2Eac,
                VTexFormat.ATI1N => InternalFormat.CompressedRedRgtc1,
                VTexFormat.ATI2N => InternalFormat.CompressedRgRgtc2,
                VTexFormat.BC6H => InternalFormat.CompressedRgbBptcUnsignedFloat,
                VTexFormat.BC7 => InternalFormat.CompressedRgbaBptcUnorm,
                VTexFormat.RGBA8888 => InternalFormat.Rgba8,
                VTexFormat.RGBA16161616 => InternalFormat.Rgba16f,
                VTexFormat.I8 => InternalFormat.Intensity8,
                _ => null // Unsupported texture format
            };

        private static InternalFormat? GetPixelInternalFormat(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.R16 => InternalFormat.R16,
                VTexFormat.R16F => InternalFormat.R16f,
                VTexFormat.RG1616 => InternalFormat.Rg16,
                VTexFormat.RG1616F => InternalFormat.Rg16f,
                _ => null // Unsupported texture format
            };

        private static PixelFormat GetPixelFormat(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.R16 => PixelFormat.Red,
                VTexFormat.R16F => PixelFormat.Red,
                VTexFormat.RG1616 => PixelFormat.Rg,
                VTexFormat.RG1616F => PixelFormat.Rg,
                _ => PixelFormat.Rgba
            };

        private static PixelType GetPixelType(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.R16 => PixelType.UnsignedShort,
                VTexFormat.R16F => PixelType.Float,
                VTexFormat.RG1616 => PixelType.UnsignedShort,
                VTexFormat.RG1616F => PixelType.Float,
                _ => PixelType.UnsignedByte
            };


        public RenderMaterial GetErrorMaterial()
        {
            var errorMat = new RenderMaterial(new VrfMaterial());
            errorMat.Textures["g_tColor"] = GetErrorTexture();
            errorMat.Material.ShaderName = "vrf.error";

            return errorMat;
        }


        public TextureHandle GetErrorTexture()
        {
            if (ErrorTextureID == TextureHandle.Zero)
            {
                var color = new[]
                {
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,

                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,

                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,

                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                };

                ErrorTextureID = GenerateColorTexture(4, 4, color);
            }

            return ErrorTextureID;
        }

        public static TextureHandle CreateSolidTexture(float r, float g, float b)
            => GenerateColorTexture(1, 1, new[] { r, g, b, 1f });

        private static TextureHandle GenerateColorTexture(int width, int height, float[] color)
        {
            var texture = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2d, texture);
            GL.TexImage2D(TextureTarget.Texture2d, 0, InternalFormat.Rgba32f, width, height, 0, PixelFormat.Rgba, PixelType.Float, color);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMaxLevel, 0);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            GL.BindTexture(TextureTarget.Texture2d, TextureHandle.Zero);

            return texture;
        }
    }
}
