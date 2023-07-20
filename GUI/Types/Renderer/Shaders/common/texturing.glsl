#if defined(NEED_CURVATURE) && (F_USE_PER_VERTEX_CURVATURE == 0)
// Expensive, only used in skin shaders
float GetCurvature(vec3 vNormal, vec3 vPositionWS)
{
	return length(fwidth(vNormal)) / length(fwidth(vPositionWS));
}
#endif



// Geometric roughness. Essentially just Specular Anti-Aliasing
#extension GL_ARB_derivative_control : enable // enable DFDX and DFDY

float CalculateGeometricRoughnessFactor(vec3 geometricNormal)
{
	vec3 normalDerivX = dFdxCoarse(geometricNormal);
	vec3 normalDerivY = dFdyCoarse(geometricNormal);
	float geometricRoughnessFactor = pow(saturate(max(dot(normalDerivX, normalDerivX), dot(normalDerivY, normalDerivY))), 0.333);
	return geometricRoughnessFactor;
}

float AdjustRoughnessByGeometricNormal( float roughness, vec3 geometricNormal )
{
	float geometricRoughnessFactor = CalculateGeometricRoughnessFactor(geometricNormal);

	return max(roughness, geometricRoughnessFactor);
}







float applyBlendModulation(float blendFactor, float blendMask, float blendSoftness)
{
    float minb = max(0.0, blendMask - blendSoftness);
    float maxb = min(1.0, blendMask + blendSoftness);

    return smoothstep(minb, maxb, blendFactor);
}









// Struct full of everything needed for lighting, for easy access around the shader.

struct MaterialProperties
{
    vec3 PositionWS;
    vec3 GeometricNormal;
    vec3 Tangent;
    vec3 Bitangent;
    vec3 ViewDir;

    vec3 Albedo;
    float Opacity;
    float Metalness;
    vec3 Normal;
    vec3 NormalMap;

    float Roughness;
    float RoughnessTex;

    float AmbientOcclusion;
    vec3 DiffuseAO; // vec3 because  Diffuse AO can be tinted
    float SpecularAO;
    vec4 ExtraParams;

    vec3 DiffuseColor;
    vec3 SpecularColor;
    vec3 TransmissiveColor;
    vec3 IllumColor;



#if defined(NEED_CURVATURE)
    float Curvature;
#endif
    //int NumDynamicLights;
};

void InitProperties(out MaterialProperties mat, vec3 GeometricNormal)
{
    mat.PositionWS = vFragPosition;
    mat.ViewDir = normalize(vEyePosition - vFragPosition);
    mat.GeometricNormal = normalize(GeometricNormal);
    mat.Tangent = normalize(vTangentOut);
    mat.Bitangent = normalize(vBitangentOut);
    
    mat.Albedo = vec3(0.0);
    mat.Opacity = 1.0;
    mat.Metalness = 0.0;
    mat.Normal = vec3(0.0);
    mat.NormalMap = vec3(0, 0, 1);

#if 0 && defined(VEC2_ROUGHNESS)
    mat.Roughness = vec2(0.0);
    mat.RoughnessTex = vec2(0.0);
#else
    mat.Roughness = 0.0;
    mat.RoughnessTex = 0.0;
#endif
    mat.AmbientOcclusion = 1.0;
    mat.DiffuseAO = vec3(1.0);
    mat.SpecularAO = 1.0;
    // r = retro reflectivity, g = sss mask, b = cloth, a = ???
    mat.ExtraParams = vec4(0.0);


    mat.DiffuseColor = vec3(0.0);
    mat.SpecularColor = vec3(0.04);
    mat.TransmissiveColor = vec3(0.0);
    mat.IllumColor = vec3(0.0);

    mat.BentGeometricNormal = vec3(0.0); // Indirect geometric normal
    mat.BentNormal = vec3(0.0); // Indirect normal
#if defined(NEED_CURVATURE)// || renderMode_Curvature
    #if F_USE_PER_VERTEX_CURVATURE == 1
        mat.Curvature = flPerVertexCurvature;
    #else
        mat.Curvature = pow( GetCurvature(mat.GeometricNormal, mat.PositionWS), 0.333 );
    #endif
#endif
    //prop.NumDynamicLights = 0;


#if (F_RENDER_BACKFACES == 1) && (F_DONT_FLIP_BACKFACE_NORMALS == 0)
    // when rendering backfaces, they invert normal so it appears front-facing
    mat.GeometricNormal *= gl_FrontFacing ? 1.0 : -1.0;
#endif
}



// AO Proxies would be merged with these
uniform float g_flAmbientOcclusionDirectDiffuse = 1.0;
uniform float g_flAmbientOcclusionDirectSpecular = 1.0;

void ApplyAmbientOcclusion(inout LightingTerms o, MaterialProperties mat)
{
    vec3 DirectAODiffuse  = mix(vec3(1.0), mat.DiffuseAO, g_flAmbientOcclusionDirectDiffuse);
	float DirectAOSpecular = mix(1.0, mat.SpecularAO, g_flAmbientOcclusionDirectSpecular);

    o.DiffuseDirect    *= DirectAODiffuse;
    o.DiffuseIndirect  *= mat.DiffuseAO;
    o.SpecularDirect   *= DirectAOSpecular;
    o.SpecularIndirect *= mat.SpecularAO;
}

float GetIsoRoughness(float Roughness)
{
    return Roughness;
}

float GetIsoRoughness(vec2 Roughness)
{
    return dot(Roughness, vec2(0.5));
}







//-------------------------------------------------------------------------
//                              NORMALS
//-------------------------------------------------------------------------

// Prevent over-interpolation of vertex normals. Introduced in The Lab renderer
vec3 SwitchCentroidNormal(vec3 vNormalWs, vec3 vCentroidNormalWs)
{
    return ( dot(vNormalWs, vNormalWs) >= 1.01 ) ? vCentroidNormalWs : vNormalWs;
}


vec3 oct_to_float32x3(vec2 e)
{
    vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
    return normalize(v);
}

vec3 unpackHemiOctNormal(vec4 bumpNormal)
{
    //Reconstruct the tangent vector from the map
#if (HemiOctIsoRoughness_RG_B == 1)
    vec2 temp = vec2(bumpNormal.x + bumpNormal.y - 1.003922, bumpNormal.x - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#else
    //vec2 temp = vec2(bumpNormal.w, bumpNormal.y) * 2 - 1;
    //vec3 tangentNormal = vec3(temp, sqrt(1 - temp.x * temp.x - temp.y * temp.y));
    vec2 temp = vec2(bumpNormal.w + bumpNormal.y - 1.003922, bumpNormal.w - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#endif

    // This is free, it gets compiled into the TS->WS matrix mul
    tangentNormal.y = -tangentNormal.y;

    return tangentNormal;
}

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal(vec3 normalMap, vec3 normal, vec3 tangent, vec3 bitangent)
{
    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * normalMap);
}

#if 0 && (F_USE_BENT_NORMALS == 1)
void GetBentNormal(inout MaterialProperties mat, vec2 texCoords)
{
    vec3 bentNormalTexel = unpackHemiOctNormal( texture(g_tBentNormal, texCoords) );
    prop.BentGeometricNormal = calculateWorldNormal(bentNormalTexel, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    // this is how they blend in the bent normal; by re-converting the normal map to tangent space using the bent geo normal
    prop.BentNormal = calculateWorldNormal(mat.NormalMap, mat.BentGeometricNormal, mat.Tangent, mat.Bitangent);
}
#endif





//-------------------------------------------------------------------------
//                              ALPHA TEST
//-------------------------------------------------------------------------


#if (F_ALPHA_TEST == 1)

uniform float g_flAntiAliasedEdgeStrength = 1.0;

float AlphaTestAntiAliasing(float flOpacity, vec2 UVs)
{
	float flAlphaTestAA = saturate( (flOpacity - g_flAlphaTestReference) / ClampToPositive( fwidth(flOpacity) ) + 0.5 );
	float flAlphaTestAA_Amount = min(1.0, length( fwidth(UVs) ) * 4.0);
	float flAntiAliasAlphaBlend = mix(1.0, flAlphaTestAA_Amount, g_flAntiAliasedEdgeStrength);
	return mix( flAlphaTestAA, flOpacity, flAntiAliasAlphaBlend );
}

#endif








//-------------------------------------------------------------------------
//                              DETAIL TEXTURING
//-------------------------------------------------------------------------

#if (F_DETAIL_TEXTURE > 0)

// Xen foliage detail textures always has both color and normal
#define DETAIL_COLOR_MOD2X (F_DETAIL_TEXTURE == 1) && !defined(vr_xen_foliage)
#define DETAIL_COLOR_OVERLAY ((F_DETAIL_TEXTURE == 2) || (F_DETAIL_TEXTURE == 4)) || defined(vr_xen_foliage)
#define DETAIL_NORMALS ((F_DETAIL_TEXTURE == 3) || (F_DETAIL_TEXTURE == 4)) || defined(vr_xen_foliage)

uniform float g_flDetailBlendFactor = 1.0;
uniform float g_flDetailBlendToFull = 0.0;
uniform float g_flDetailNormalStrength = 1.0;
in vec2 vDetailTexCoords;

uniform sampler2D g_tDetailMask;
#if DETAIL_COLOR_MOD2X || DETAIL_COLOR_OVERLAY
    uniform sampler2D g_tDetail;
#endif
#if DETAIL_NORMALS
    uniform sampler2D g_tNormalDetail;
#endif

#define MOD2X_MUL 1.9922
#define DETAIL_CONST 0.9961


void applyDetailTexture(inout vec3 Albedo, inout vec3 NormalMap, vec2 detailMaskCoords)
{
    float detailMask = texture(g_tDetailMask, detailMaskCoords).x;
    detailMask = g_flDetailBlendFactor * max(detailMask, g_flDetailBlendToFull);

    // MOD2X
#if (DETAIL_COLOR_MOD2X)

    vec3 DetailTexture = texture(g_tDetail, vDetailTexCoords).rgb * MOD2X_MUL;
    Albedo *= mix(vec3(1.0), DetailTexture, detailMask);

// OVERLAY
#elif (DETAIL_COLOR_OVERLAY)

    vec3 DetailTexture = DETAIL_CONST * texture(g_tDetail, vDetailTexCoords).rgb;

    // blend in linear space! this is actually in the code, so we're doing the right thing!
    vec3 linearAlbedo = pow(Albedo, invGamma);
    vec3 overlayScreen = 1.0 - (1.0 - DetailTexture) * (1.0 - linearAlbedo) * 2.0;
    vec3 overlayMul = DetailTexture * linearAlbedo * 2.0;

    vec3 linearBlendedOverlay = mix(overlayMul, overlayScreen, greaterThanEqual(linearAlbedo, vec3(0.5)));
    vec3 gammaBlendedOverlay = pow(linearBlendedOverlay, gamma);

    Albedo = mix(Albedo, gammaBlendedOverlay, detailMask);

#endif


// NORMALS
#if (DETAIL_NORMALS)
    vec3 DetailNormal = unpackHemiOctNormal(texture(g_tNormalDetail, vDetailTexCoords));
    DetailNormal = mix(vec3(0, 0, 1), DetailNormal, detailMask * g_flDetailNormalStrength);
    // literally i dont even know
    NormalMap = NormalMap * DetailNormal.z + vec3(NormalMap.z * DetailNormal.z * DetailNormal.xy, 0.0);

#endif
}

#endif
