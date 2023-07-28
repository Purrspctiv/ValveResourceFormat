#version 460

#include "common/utils.glsl"

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform sampler2D g_tFlow;
uniform sampler2D g_tNormal;
uniform sampler2D g_tNoise;

uniform vec3 vEyePosition;

uniform vec4 g_vWaterFogColor;
uniform vec4 g_vLowEndSurfaceColor;
uniform vec4 g_vLowEndReflectionColor;

uniform float g_flTime;
uniform float g_flFlowTimeScale;

uniform vec4 TextureFlow;

uniform float g_flNormalUvScale = 1.0;
uniform float g_flNormalFlowUvScrollDistance = 1.0;
uniform float g_flNoiseUvScale = 1.0;

uniform float g_flLowEndSurfaceMinimumColor;

vec3 calculateWorldNormal()
{
    // If this is a flowmap, this is wrong
    vec2 offset = TextureFlow.xy * g_flTime * g_flFlowTimeScale;
    vec4 textureNormal = texture(g_tNormal, (vFragPosition.xy / g_flNormalUvScale) + offset);

    //Reconstruct the bump vector from the map
    vec2 temp = vec2(textureNormal.w, textureNormal.y) * 2 - 1;
    vec3 bumpNormal = vec3(temp, sqrt(1 - temp.x * temp.x - temp.y * temp.y));

    return normalize(bumpNormal);
}

//Main entry point
void main()
{
    vec3 normal = calculateWorldNormal();

    vec3 viewDirection = normalize(vEyePosition - vFragPosition);

    vec3 lightDirection = normalize(vEyePosition - vFragPosition);

    //Calculate Blinn specular based on reflected light
    vec3 halfDir = normalize(lightDirection + viewDirection);
    float specular = ClampToPositive(dot(halfDir, normal));

    // Calculate fresnel (assuming Vertex Normal = Z up?)
    float fresnel = ClampToPositive(1.0 - pow(viewDirection.z, 0.5));

    // idk why this formula, but it looks okay
    float transparency = max(0.3, pow(fresnel, 3) * 2.0 + fresnel * specular * 3);
    vec3 specularColor = g_vLowEndReflectionColor.rgb * pow(specular, 4.0);
    vec3 fresnelColor =  mix(g_vWaterFogColor.rgb, g_vLowEndSurfaceColor.rgb * pow(fresnel, 3.0), transparency);

    outputColor = vec4(fresnelColor + specularColor, transparency);
}
