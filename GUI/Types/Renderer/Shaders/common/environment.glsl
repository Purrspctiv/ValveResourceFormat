// https://seblagarde.wordpress.com/2012/09/29/image-based-lighting-approaches-and-parallax-corrected-cubemap/
vec3 CubeMapBoxProjection(vec3 pos, vec3 R, vec3 mins, vec3 maxs, vec4 center)
{
    if (center.w > 0.0)
    {
        // Following is the parallax-correction code
        // Find the ray intersection with box plane
        vec3 FirstPlaneIntersect = (maxs - pos) / R;
        vec3 SecondPlaneIntersect = (mins - pos) / R;
        // Get the furthest of these intersections along the ray
        // (Ok because x/0 give +inf and -x/0 give -inf )
        vec3 FurthestPlane = max(FirstPlaneIntersect, SecondPlaneIntersect);
        // Find the closest far intersection
        float Distance = min(min(FurthestPlane.x, FurthestPlane.y), FurthestPlane.z);

        // Get the intersection position
        vec3 IntersectPositionWS = pos + R * Distance;
        // Get corrected reflection
        return IntersectPositionWS - center.xyz;
        // End parallax-correction code
    }

    return R;
}

#define MAX_ENVMAP_LOD 7
#define SCENE_ENVIRONMENT_TYPE 0

#if (SCENE_ENVIRONMENT_TYPE == 0) // None or missing environment map
    // ...
#else
#if (SCENE_ENVIRONMENT_TYPE == 1) // Per-object cube map
    uniform samplerCube g_tEnvironmentMap;
    uniform vec4 g_vEnvMapBoxMins;
    uniform vec4 g_vEnvMapBoxMaxs;
    uniform vec4 g_vEnvMapPositionWs;
#elif (SCENE_ENVIRONMENT_TYPE == 2) // Per scene cube map array
    #define MAX_ENVMAPS 144
    uniform samplerCubeArray g_tEnvironmentMap;
    uniform mat4 g_matEnvMapWorldToLocal[MAX_ENVMAPS];
    uniform vec4 g_vEnvMapPositionWs[MAX_ENVMAPS];
    uniform vec4 g_vEnvMapBoxMins[MAX_ENVMAPS];
    uniform vec4 g_vEnvMapBoxMaxs[MAX_ENVMAPS];
    uniform vec4 g_vEnvMapEdgeFadeDists[MAX_ENVMAPS];
    uniform int g_iEnvironmentMapArrayIndex[4];
#endif

float GetEnvMapLOD(float roughness, vec3 R, vec4 extraParams)
{
    #if F_CLOTH_SHADING == 1
        float lod = mix(roughness, pow(roughness, 0.125), extraParams.b);
        return lod * MAX_ENVMAP_LOD;
    #else
        return roughness * MAX_ENVMAP_LOD;
    #endif
}

// Cubemap Normalization
// Used in HLA, maybe later vr renderer games too.
// Further explanation here: https://ubm-twvideo01.s3.amazonaws.com/o1/vault/gdc2019/presentations/Hobson_Josh_The_Indirect_Lighting.pdf
#define bUseCubemapNormalization 0
const vec2 CubemapNormalizationParams = vec2(34.44445, -2.44445); // Normalization value in Alyx. Haven't checked the other games

float GetEnvMapNormalization(float rough, vec3 N, vec3 irradiance)
{
    #if (bUseCubemapNormalization == 1 && renderMode_Cubemaps == 0)
        // Cancel out lighting
        // edit: no cancellation. I don't know how to get the greyscale SH that they use here, because the radiance coefficients in the cubemap texture are rgb.
        float NormalizationTerm = GetLuma(irradiance);// / dot(vec4(N, 1.0), g_vEnvironmentMapNormalizationSH[EnvMapIndex]);

        // Reduce cancellation on glossier surfaces
        float NormalizationClamp = max(1.0, rough * CubemapNormalizationParams.x + CubemapNormalizationParams.y);
        return min(NormalizationTerm, NormalizationClamp);
    #else
        return 1.0;
    #endif
}

// BRDF
uniform sampler2D g_tBRDFLookup;

vec3 EnvBRDF(vec3 specColor, float rough, vec3 N, vec3 V)
{
    // done here because of bent normals
    float NdotV = ClampToPositive(dot(N, V));
    vec2 lutCoords = vec2(NdotV, sqrt(rough));

    vec2 GGXLut = pow2(textureLod(g_tBRDFLookup, lutCoords, 0.0).xy);
    return specColor * GGXLut.x + GGXLut.y;
}

// may be different past HLA? looks kinda wrong in cs2. they had a cloth brdf lut iirc
// We could check for the lut and then use this path if it doesn't exist
#if F_CLOTH_SHADING == 1
float EnvBRDFCloth(float roughness, vec3 N, vec3 V)
{
    float NoH = dot(normalize(N + V), N);
    return D_Cloth(roughness, NoH) / 8.0;
}
#endif

#endif

float GetEnvMapWeight(vec3 center, vec3 mins, vec3 maxs, vec3 dists)
{
    vec3 weight = min(center - mins.xyz, maxs.xyz - center) / dists;
    return clamp(min(weight.x, min(weight.y, weight.z)), 0.0, 1.0);
}

vec3 GetEnvironment(vec3 N, vec3 V, float rough, vec3 specColor, vec3 irradiance, vec4 extraParams)
{
    #if (SCENE_ENVIRONMENT_TYPE == 0)
        return vec3(0.0, 0.0, 0.0);
    #else

    // Reflection Vector
    vec3 R = normalize(reflect(-V, N));
    float lod = GetEnvMapLOD(rough, R, extraParams);

    #if (SCENE_ENVIRONMENT_TYPE == 1)
        vec3 coords = R;
        vec3 mins = g_vEnvMapBoxMins.xyz;
        vec3 maxs = g_vEnvMapBoxMaxs.xyz;
        vec3 center = g_vEnvMapPositionWs.xyz;
        vec3 envMap = textureLod(g_tEnvironmentMap, coords, lod).rgb;
    #elif (SCENE_ENVIRONMENT_TYPE == 2)
        vec3 envMap = vec3(0.0);
        float totalWeight = 0.0;

        for (int i = 0; i < 1 && totalWeight < 0.99; i++) {
            int envMapArrayIndex = g_iEnvironmentMapArrayIndex[i];
            vec3 mins = g_vEnvMapBoxMins[envMapArrayIndex].xyz;
            vec3 maxs = g_vEnvMapBoxMaxs[envMapArrayIndex].xyz;
            vec3 dists = g_vEnvMapEdgeFadeDists[envMapArrayIndex].xyz;
            vec4 center = g_vEnvMapPositionWs[envMapArrayIndex];
            vec4 coords = vec4(CubeMapBoxProjection(vFragPosition, R, mins, maxs, center), envMapArrayIndex);

            //float weight = GetEnvMapWeight(vFragPosition, mins, maxs, dists);
            //weight = min(weight, 1.0f - totalWeight);
            //totalWeight += weight;

#if renderMode_Cubemaps == 0
            // blend
            #if (F_CLOTH_SHADING == 1)
                coords.xyz = normalize(mix(coords.xyz, N, sqrt(rough)));
            #else
                coords.xyz = normalize(mix(coords.xyz, N, rough));
            #endif
#endif

            envMap += textureLod(g_tEnvironmentMap, coords, lod).rgb; // * weight;
        }
    #endif

#if renderMode_Cubemaps == 1
    return envMap;
#else
    vec3 brdf = EnvBRDF(specColor, rough, N, V);

    #if (F_CLOTH_SHADING == 1)
        vec3 clothBrdf = vec3(EnvBRDFCloth(rough, N, V));

        float clothMask = extraParams.z;

        brdf = mix(brdf, clothBrdf, clothMask);
    #endif

    float normalizationTerm = GetEnvMapNormalization(rough, N, irradiance);

    return brdf * envMap * normalizationTerm;
#endif
    #endif
}
