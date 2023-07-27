using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using Channel = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace ValveResourceFormat.IO.ShaderDataProvider
{
    public interface IShaderDataProvider
    {
        public IEnumerable<(Channel Channel, string Name)> GetInputsForTexture(string textureType, Material material);
        public string GetSuffixForInputTexture(string inputName, Material material);
    }

    public class FullShaderDataProvider : IShaderDataProvider
    {
        private readonly IFileLoader fileLoader;
        private readonly IShaderDataProvider basicProvider;
        private static readonly Cache cache = new();

        public class Cache : IDisposable
        {
            public Dictionary<string, ShaderCollection> ShaderCache { get; } = new();
            private readonly object cacheLock = new();

            public ShaderCollection GetOrAddShader(string shaderName, Func<string, ShaderCollection> factory)
            {
                lock (cacheLock)
                {
                    if (ShaderCache.TryGetValue(shaderName, out var shader))
                    {
                        return shader;
                    }

                    shader = factory(shaderName);
                    ShaderCache.Add(shaderName, shader);
                    return shader;
                }
            }

            public void Clear()
            {
                lock (cacheLock)
                {
                    foreach (var shader in ShaderCache.Values)
                    {
                        shader.Dispose();
                    }

                    ShaderCache.Clear();
                }
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Clear();
                }
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        public FullShaderDataProvider(IFileLoader fileLoader, bool fallBackToBasic = true)
        {
            this.fileLoader = fileLoader;
            basicProvider = fallBackToBasic ? new BasicShaderDataProvider() : null;
        }

        public IEnumerable<(Channel Channel, string Name)> GetInputsForTexture(string textureType, Material material)
        {
            return GetInputsForTexture_Internal(textureType, material) ?? basicProvider?.GetInputsForTexture(textureType, material);
        }

        public string GetSuffixForInputTexture(string inputName, Material material)
        {
            return GetSuffixForInputTexture_Internal(inputName, material) ?? basicProvider?.GetSuffixForInputTexture(inputName, material);
        }

        public (ShaderCollection Collection, ShaderFile Shader, ZFrameFile ZFrame) GetZFrame_TEST_DO_NOT_MERGE(string textureType, Material material, string forcedStaticName = null)
        {
            var shader = cache.GetOrAddShader(material.ShaderName, (s) => fileLoader.LoadShader(s));
            if (shader?.Features == null)
            {
                return (null, null, null);
            }

            var @params = shader.Features.ParamBlocks.FindAll(p => p.Name == textureType).ToArray();
            if (@params.Length == 0)
            {
                throw new InvalidDataException($"Features file for '{shader.Features.ShaderName}' does not contain a parameter named '{textureType}'");
            }
            else
            {
                var featureState = material.IntParams.Where(p => p.Key.StartsWith("F_", StringComparison.Ordinal));

                // Pixel shader first
                var collectionOrdered = shader
                    .Where(sh => sh.VcsProgramType != VcsProgramType.Features && sh.ZframesLookup.Count > 0)
                    .OrderByDescending(sh => sh.VcsProgramType == VcsProgramType.PixelShader);

                foreach (var shaderFile in collectionOrdered)
                {
                    var fileParams = shaderFile.ParamBlocks.FindAll(p => p.Name == textureType).ToArray();
                    if (fileParams.Length == 0)
                    {
                        continue;
                    }

                    var staticConfiguration = new int[shaderFile.SfBlocks.Count];
                    var configGen = new ConfigMappingSParams(shaderFile);

                    foreach (var condition in shaderFile.SfBlocks)
                    {
                        // Dota seems to want one of S_MODE_FORWARD / S_MODE_DEFERRED enabled
                        // for textures to be referenced in the writeseq blocks.
                        if ((condition.Name == "S_MODE_FORWARD" && condition.FeatureIndex == -1)
                            || (forcedStaticName is not null && condition.Name == forcedStaticName))
                        {
                            staticConfiguration[condition.BlockIndex] = 1;
                            continue;
                        }

                        if (condition.FeatureIndex == -1)
                        {
                            continue;
                        }

                        var feature = shader.Features.SfBlocks[condition.FeatureIndex];

                        foreach (var (Name, Value) in featureState)
                        {
                            if (feature.Name == Name)
                            {
                                if (Value > feature.RangeMax || Value < feature.RangeMin)
                                {
                                    throw new InvalidDataException($"Material feature '{Name}' is out of range for '{shader.Features.ShaderName}'");
                                }

                                staticConfiguration[condition.BlockIndex] = (int)Value;
                                break;
                            }
                        }
                    }

                    var zframeId = configGen.GetZframeId(staticConfiguration);

                    // It can happen that the shader feature rules don't match static rules, producing
                    // materials with bad feature configuration. That or the material data is just bad/incompatible.
                    if (!shaderFile.ZframesLookup.ContainsKey(zframeId))
                    {
                        // Game code probably goes through the sfRules and switches off only some of the parameters.
                        // But here we just fall back to first zframe (effectively switches all off).
                        zframeId = 0;
                    }

                    lock (shaderFile)
                    {
                        return (shader, shaderFile, shaderFile.GetZFrameFile(zframeId));
                    }
                }

                throw new InvalidDataException(
                    $"Varying parameter '{textureType}' in '{shader.Features.ShaderName}' could not be resolved. "
                    + $"Features ({string.Join(", ", featureState.Select(p => $"{p.Key}={p.Value}"))})");
            }
        }

        /// <summary>
        /// Get precise texture inputs by querying the shader files. 
        /// </summary>
        private IEnumerable<(Channel Channel, string Name)>
            GetInputsForTexture_Internal(string textureType, Material material, string forcedStaticName = null)
        {
            var shader = cache.GetOrAddShader(material.ShaderName, (s) => fileLoader.LoadShader(s));
            if (shader?.Features == null)
            {
                return null;
            }

            var @params = shader.Features.ParamBlocks.FindAll(p => p.Name == textureType).ToArray();
            if (@params.Length == 0)
            {
                throw new InvalidDataException($"Features file for '{shader.Features.ShaderName}' does not contain a parameter named '{textureType}'");
            }
            else if (@params.Length == 1)
            {
                var inputs = GetParameterInputs(@params[0], shader.Features);
                return inputs;
            }
            else
            {
                var featureState = material.IntParams.Where(p => p.Key.StartsWith("F_", StringComparison.Ordinal));

                // Pixel shader first
                var collectionOrdered = shader
                    .Where(sh => sh.VcsProgramType != VcsProgramType.Features && sh.ZframesLookup.Count > 0)
                    .OrderByDescending(sh => sh.VcsProgramType == VcsProgramType.PixelShader);

                foreach (var shaderFile in collectionOrdered)
                {
                    var fileParams = shaderFile.ParamBlocks.FindAll(p => p.Name == textureType).ToArray();
                    if (fileParams.Length == 0)
                    {
                        continue;
                    }

                    var staticConfiguration = new int[shaderFile.SfBlocks.Count];
                    var configGen = new ConfigMappingSParams(shaderFile);

                    foreach (var condition in shaderFile.SfBlocks)
                    {
                        // Dota seems to want one of S_MODE_FORWARD / S_MODE_DEFERRED enabled
                        // for textures to be referenced in the writeseq blocks.
                        if ((condition.Name == "S_MODE_FORWARD" && condition.FeatureIndex == -1)
                            || (forcedStaticName is not null && condition.Name == forcedStaticName))
                        {
                            staticConfiguration[condition.BlockIndex] = 1;
                            continue;
                        }

                        if (condition.FeatureIndex == -1)
                        {
                            continue;
                        }

                        var feature = shader.Features.SfBlocks[condition.FeatureIndex];

                        foreach (var (Name, Value) in featureState)
                        {
                            if (feature.Name == Name)
                            {
                                if (Value > feature.RangeMax || Value < feature.RangeMin)
                                {
                                    throw new InvalidDataException($"Material feature '{Name}' is out of range for '{shader.Features.ShaderName}'");
                                }

                                staticConfiguration[condition.BlockIndex] = (int)Value;
                                break;
                            }
                        }
                    }

                    var zframeId = configGen.GetZframeId(staticConfiguration);

                    // It can happen that the shader feature rules don't match static rules, producing
                    // materials with bad feature configuration. That or the material data is just bad/incompatible.
                    if (!shaderFile.ZframesLookup.ContainsKey(zframeId))
                    {
                        // Game code probably goes through the sfRules and switches off only some of the parameters.
                        // But here we just fall back to first zframe (effectively switches all off).
                        zframeId = 0;
                    }

                    lock (shaderFile)
                    {
                        using var staticVariant = shaderFile.GetZFrameFile(zframeId);

                        // Should non-leading write sequences be checked too?
                        foreach (var writeSequenceField in staticVariant.LeadingData.Fields)
                        {
                            var referencedParam = fileParams.FirstOrDefault(p => p.BlockIndex == writeSequenceField.ParamId);
                            if (referencedParam != null)
                            {
                                var inputs = GetParameterInputs(referencedParam, shaderFile);
                                return inputs;
                            }
                        }
                    }

                    if (forcedStaticName is null)
                    {
                        // Try again with S_MODE_TOOLS_VIS
                        // Fixes hlvr/pak01/materials/skybox/sky_stars_01.vmat
                        // Jumps from zframe 0x230a to 0x280230a, ends up matching the 2nd g_tNormal, with Box mips.
                        return GetInputsForTexture_Internal(textureType, material, forcedStaticName: "S_MODE_TOOLS_VIS");
                    }
                }

                throw new InvalidDataException(
                    $"Varying parameter '{textureType}' in '{shader.Features.ShaderName}' could not be resolved. "
                    + $"Features ({string.Join(", ", featureState.Select(p => $"{p.Key}={p.Value}"))})");
            }

            IEnumerable<(Channel Channel, string Name)> GetParameterInputs(ParamBlock param, ShaderFile shaderFile)
            {
                for (var i = 0; i < param.ChannelCount; i++)
                {
                    var channelIndex = param.ChannelIndices[i];
                    var channel = shaderFile.ChannelBlocks[channelIndex];

                    var cutoff = Array.IndexOf(channel.InputTextureIndices, -1);
                    var textureProcessorInputs = channel.InputTextureIndices[..cutoff].Select(idx => shaderFile.ParamBlocks[idx].Name).ToArray();

                    if (channel.TexProcessorName == "HemiOctIsoRoughness_RG_B" || channel.TexProcessorName == "AnisoNormal")
                    {
                        yield return (Channel.RGB, textureProcessorInputs[0]);
                        if (textureProcessorInputs.Length == 2)
                        {
                            yield return (Channel.A, textureProcessorInputs[1]);
                        }

                        yield break;
                    }

                    yield return (channel.Channel, textureProcessorInputs[0]);
                }
            }
        }

        private string GetSuffixForInputTexture_Internal(string inputName, Material material)
        {
            var shader = cache.GetOrAddShader(material.ShaderName, (s) => fileLoader.LoadShader(s));
            if (shader?.Features != null)
            {
                foreach (var param in shader.Features.ParamBlocks)
                {
                    if (param.Name == inputName && !string.IsNullOrEmpty(param.ImageSuffix))
                    {
                        return "_" + param.ImageSuffix;
                    }
                }
            }

            return null;
        }
    }

    public class BasicShaderDataProvider : IShaderDataProvider
    {
        public static readonly Dictionary<string, Dictionary<string, (Channel Channel, string Name)[]>> TextureMappings = new()
        {
            ["global_lit_simple"] = new()
            {
                ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
                ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal") },
                ["g_tSpecular"] = new[] { (Channel.R, "TextureReflectance"), (Channel.G, "TextureSelfIllum"), (Channel.B, "TextureBloom") },
            },

            ["multiblend"] = new()
            {
                ["g_tColor0"] = new[] { (Channel.RGB, "TextureColor0") },
                ["g_tColor1"] = new[] { (Channel.RGB, "TextureColor1"), (Channel.A, "TextureRevealMask1") },
                ["g_tColor2"] = new[] { (Channel.RGB, "TextureColor2"), (Channel.A, "TextureRevealMask2") },
                ["g_tColor3"] = new[] { (Channel.RGB, "TextureColor3"), (Channel.A, "TextureRevealMask3") },
                ["g_tSpecular0"] = new[] { (Channel.R, "TextureReflectance0"), (Channel.G, "TextureSelfIllum0"), (Channel.B, "TextureBloom0") },
                ["g_tSpecular1"] = new[] { (Channel.R, "TextureReflectance1"), (Channel.G, "TextureSelfIllum1"), (Channel.B, "TextureBloom1") },
                ["g_tSpecular2"] = new[] { (Channel.R, "TextureReflectance2"), (Channel.G, "TextureSelfIllum2"), (Channel.B, "TextureBloom2") },
                ["g_tSpecular3"] = new[] { (Channel.R, "TextureReflectance3"), (Channel.G, "TextureSelfIllum3"), (Channel.B, "TextureBloom3") },
                ["g_tTintMasks"] = new[] { (Channel.R, "TextureTintMask0"), (Channel.G, "TextureTintMask1"), (Channel.B, "TextureTintMask2"), (Channel.A, "TextureTintMask3") },
                ["g_tTint2Masks"] = new[] { (Channel.R, "TextureTint2Mask0"), (Channel.G, "TextureTint2Mask1"), (Channel.B, "TextureTint2Mask2"), (Channel.A, "TextureTint2Mask3") },
            },

            ["hero"] = new()
            {
                ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
                ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal") },
                ["g_tCubeMap"] = new[] { (Channel.RGBA, "TextureCubeMap") },
                ["g_tCubeMapSeparateMask"] = new[] { (Channel.G, "TextureCubeMapSeparateMask") },
                ["g_tFresnelWarp"] = new[] { (Channel.R, "TextureFresnelWarpRim"), (Channel.G, "TextureFresnelWarpColor"), (Channel.B, "TextureFresnelWarpSpec") },
                ["g_tMasks1"] = new[] { (Channel.R, "TextureDetailMask"), (Channel.G, "TextureDiffuseWarpMask"), (Channel.B, "TextureMetalnessMask"), (Channel.A, "TextureSelfIllumMask") },
                ["g_tMasks2"] = new[] { (Channel.R, "TextureSpecularMask"), (Channel.G, "TextureRimMask"), (Channel.B, "TextureTintByBaseMask"), (Channel.A, "TextureSpecularExponent") },
                ["g_tDetail"] = new[] { (Channel.RGBA, "TextureDetail") },
                ["g_tDetail2"] = new[] { (Channel.RGBA, "TextureDetail2") },
            },

            ["grasstile_preview"] = new()
            {
                ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
                ["g_tTintMask"] = new[] { (Channel.G, "TextureTintMask") },
                ["g_tSpecular"] = new[] { (Channel.G, "TextureReflectance") },
                ["g_tSelfIllum"] = new[] { (Channel.G, "TextureSelfIllum") },
            },

            ["generic"] = new()
            {
                ["g_tColor"] = new[] { (Channel.RGB, "TextureColor") },
                ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal") },
                ["g_tMetalnessReflectanceFresnel"] = new[] { (Channel.R, "TextureMetalness"), (Channel.G, "TextureReflectance"), (Channel.B, "TextureFresnel") },
                ["g_tRoughness"] = new[] { (Channel.R, "TextureRoughness"), },
            },

            ["vr_standard"] = new()
            {
                ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
                ["g_tColor1"] = new[] { (Channel.RGB, "TextureColor") },
                ["g_tColor2"] = new[] { (Channel.RGB, "TextureColor") },
                ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal") },
                ["g_tNormal1"] = new[] { (Channel.RGB, "TextureNormal") },
                ["g_tNormal2"] = new[] { (Channel.RGB, "TextureNormal") },
            },

            ["vr_complex"] = new()
            {
                ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, string.Empty) }, // Alpha can be metal or translucency
                ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal"), (Channel.A, "TextureRoughness") }, // TODO: Figure out anisotropic gloss

                // These all work fine thanks to consistent names, but we can clean them up to save disk size.
                // E.g. RGBA -> R (Grayscale)
                ["g_tAmbientOcclusion"] = new[] { (Channel.R, "TextureAmbientOcclusion") },
                ["g_tTintMask"] = new[] { (Channel.R, "TextureTintMask") },

                ["g_tMetalness"] = new[] { (Channel.R, "TextureMetalness") },
                ["g_tSelfIllumMask"] = new[] { (Channel.R, "TextureSelfIllumMask") },
                ["g_tBentNormal"] = new[] { (Channel.RGB, "TextureBentNormal") }, // ATI2N

                ["g_tDetail"] = new[] { (Channel.RGB, "TextureDetail") },
                ["g_tDetailMask"] = new[] { (Channel.R, "TextureDetailMask") },
                ["g_tNormalDetail"] = new[] { (Channel.RGB, "TextureNormalDetail") }, // ATI2N

                ["g_tSquishColor"] = new[] { (Channel.RGB, "TextureSquishColor") },
                ["g_tStretchColor"] = new[] { (Channel.RGB, "TextureStretchColor") },
                ["g_tSquishNormal"] = new[] { (Channel.RGB, "TextureSquishNormal") },
                ["g_tStretchNormal"] = new[] { (Channel.RGB, "TextureStretchNormal") },
                ["g_tSquishAmbientOcclusion"] = new[] { (Channel.R, "TextureSquishAmbientOcclusion") },
                ["g_tStretchAmbientOcclusion"] = new[] { (Channel.R, "TextureStretchAmbientOcclusion") },

            },

            ["vr_simple"] = new()
            {
                ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, string.Empty) }, // Alpha can be ao, metal or nothing at all
                ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal"), (Channel.A, "TextureRoughness") },

                ["g_tAmbientOcclusion"] = new[] { (Channel.R, "TextureAmbientOcclusion") },
                ["g_tTintMask"] = new[] { (Channel.R, "TextureTintMask") },
            },

            ["vr_simple_2way_blend"] = new()
            {
                ["g_tColorA"] = new[] { (Channel.RGB, "TextureColorA"), (Channel.A, "TextureMetalnessA") },
                ["g_tNormalA"] = new[] { (Channel.RGB, "TextureNormalA"), (Channel.A, "TextureRoughnessA") },
                ["g_tColorB"] = new[] { (Channel.RGB, "TextureColorB"), (Channel.A, "TextureMetalnessB") },
                ["g_tNormalB"] = new[] { (Channel.RGB, "TextureNormalB"), (Channel.A, "TextureRoughnessB") },

                ["g_tMask"] = new[] { (Channel.R, "TextureMask") },
            },

            ["vr_eyeball"] = new()
            {
                ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureReflectance") },
                ["g_tIris"] = new[] { (Channel.RGB, "IrisNormal"), (Channel.A, "IrisRoughness") },
                ["g_tNormal"] = new[] { (Channel.AG, "TextureNormal") },

                ["g_tIrisMask"] = new[] { (Channel.R, "TextureIrisMask") },
                ["g_tSelfIllumMask"] = new[] { (Channel.R, "TextureSelfIllumMask") },
            },

            ["csgo_weapon"] = new()
            {
                ["g_tColor"] = new[] { (Channel.RGB, "TextureColor") },
                ["g_tMetalness"] = new[] { (Channel.R, "TextureRoughness"), (Channel.G, "TextureMetalness") },
                ["g_tAmbientOcclusion"] = new[] { (Channel.R, "TextureAmbientOcclusion") },
            },

            ["sky"] = new()
            {
                ["g_tSkyTexture"] = new[] { (Channel.RGBA, "SkyTexture") },
            }
        };

        public static readonly Dictionary<string, string> CommonTextureSuffixes = new()
        {
            { "TextureDetailMask", "_detailmask" },
            { "TextureDiffuseWarpMask", "_diffusemask" },
            { "TextureMetalnessMask", "_metalnessmask" },
            { "TextureSelfIllumMask", "_selfillummask" },

            { "TextureSpecularMask", "_specmask" },
            { "TextureRimMask", "_rimmask" },
            { "TextureTintByBaseMask", "_basetintmask" },
            { "TextureSpecularExponent", "_specexp" },
            { "TextureRevealMask", "_blend" },

            { "TextureColor", "_color" },
            { "TextureNormal", "_normal" },
            { "TextureRoughness", "_rough" },
            { "TextureMetalness", "_metal" },
            { "TextureAmbientOcclusion", "_ao" },
            { "TextureReflectance", "_refl"},
            { "TextureTranslucency", "_trans"},
        };

        /// <summary>
        /// Get hardcoded texture inputs. If no mappings are found it will be a single *guessed* RGBA input. 
        /// </summary>
        public IEnumerable<(Channel Channel, string Name)> GetInputsForTexture(string textureType, Material material)
        {
            var shaderName = Path.ChangeExtension(material.ShaderName, null); // strip '.vfx'

            if (!(TextureMappings.TryGetValue(shaderName, out var shaderSpecific) && shaderSpecific.TryGetValue(textureType, out var channelMappings)))
            {
                yield return (Channel.RGBA, textureType.Replace("g_t", "Texture", StringComparison.Ordinal));
                yield break;
            }

            foreach (var mapping in channelMappings)
            {
                var (channel, newTextureType) = mapping;
                if (newTextureType.Length == 0 && !TryFigureOutNonStaticMap(shaderName, textureType, material.IntParams, out newTextureType))
                {
                    continue;
                }

                yield return (channel, newTextureType);
            }
        }

        public string GetSuffixForInputTexture(string inputName, Material material)
        {
            foreach (var (commonType, commonSuffix) in CommonTextureSuffixes)
            {
                // Allow matching TextureColorB with TextureColor
                if (inputName.StartsWith(commonType, StringComparison.OrdinalIgnoreCase))
                {
                    return commonSuffix;
                }
            }

            return null;
        }

        private static bool TryFigureOutNonStaticMap(string shader, string textureType, Dictionary<string, long> intParams, out string newTextureType)
        {
            if (shader == "vr_simple" && textureType == "g_tColor")
            {
                if (intParams.GetValueOrDefault("F_METALNESS_TEXTURE") != 0)
                {
                    newTextureType = "TextureMetalness";
                    return true;
                }

                if (intParams.GetValueOrDefault("F_AMBIENT_OCCLUSION_TEXTURE") != 0)
                {
                    newTextureType = "TextureAmbientOcclusion";
                    return true;
                }
            }

            else if (shader == "vr_complex" && textureType == "g_tColor")
            {
                newTextureType = "TextureMetalness";

                if (intParams.GetValueOrDefault("F_TRANSLUCENT") != 0
                || intParams.GetValueOrDefault("F_ALPHA_TEST") != 0)
                {
                    newTextureType = "TextureTranslucency";
                }

                return true;
            }

            newTextureType = null;
            return false;
        }
    }
}
