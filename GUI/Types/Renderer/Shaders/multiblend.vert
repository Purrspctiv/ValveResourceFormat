#version 460

//Includes - resolved by VRF
#include "common/utils.glsl"
#include "compression.incl"
#include "animation.incl"
//End of includes

#define F_WORLDSPACE_UVS 0

layout (location = 0) in vec3 vPOSITION;
in vec4 vNORMAL;
in vec2 vTEXCOORD;
in vec4 vTEXCOORD1;
in vec4 vTEXCOORD2;
in vec4 vTEXCOORD3;
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    in vec4 vTANGENT;
#endif

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;

out vec4 vBlendWeights;
out vec4 vBlendAlphas;
out vec4 vVertexColor;

out vec2 vTexCoordOut;
out vec2 vTexCoord1Out;
#if (F_TWO_LAYER_BLEND == 0)
out vec2 vTexCoord2Out;
out vec2 vTexCoord3Out;
#endif

uniform mat4 uProjectionViewMatrix;
uniform mat4 transform;

uniform float g_flTime;

uniform float g_flTexCoordScale0;
uniform float g_flTexCoordScale1;
uniform float g_flTexCoordScale2;
uniform float g_flTexCoordScale3;

uniform float g_flTexCoordRotate0;
uniform float g_flTexCoordRotate1;
uniform float g_flTexCoordRotate2;
uniform float g_flTexCoordRotate3;

// TODO: reset uniforms to default when not present in the vmat. Otherwise the entire landscape will be scrolling
/*uniform vec4 g_vTexCoordOffset0 = vec4(0.0);
uniform vec4 g_vTexCoordOffset1 = vec4(0.0);
uniform vec4 g_vTexCoordOffset2 = vec4(0.0);
uniform vec4 g_vTexCoordOffset3 = vec4(0.0);

uniform vec4 g_vTexCoordScroll0 = vec4(0.0);
uniform vec4 g_vTexCoordScroll1 = vec4(0.0);
uniform vec4 g_vTexCoordScroll2 = vec4(0.0);
uniform vec4 g_vTexCoordScroll3 = vec4(0.0);*/

uniform vec4 m_vTintColorSceneObject = vec4(1.0);
uniform vec3 m_vTintColorDrawCall = vec3(1.0);


vec2 getTexCoord(float scale, float rotation) {//, vec4 offset, vec4 scroll) {

    //Transform degrees to radians
    float r = radians(rotation);

    vec2 totalOffset = vec2(0.0);//(scroll.xy * g_flTime) + offset.xy;

    //Scale texture
    vec2 coord = vTEXCOORD - vec2(0.5);

    float SinR = sin(r);
    float CosR = cos(r);

    //Rotate vector
    vec2 rotatedCoords = vec2(CosR * vTEXCOORD.x - SinR * vTEXCOORD.y,
        SinR * vTEXCOORD.x + CosR * vTEXCOORD.y);

    return (rotatedCoords / scale) + vec2(0.5) + totalOffset;
}

void main()
{
    mat4 skinTransform = transform * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = uProjectionViewMatrix * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    mat3 normalTransform = transpose(inverse(mat3(skinTransform)));

    //Unpack normals
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    vNormalOut = normalize(normalTransform * vNORMAL.xyz);
    vTangentOut = normalize(normalTransform * vTANGENT.xyz);
    vBitangentOut = cross(vNormalOut, vTangentOut);
#else
    vec4 tangent = DecompressTangent(vNORMAL);
    vNormalOut = normalize(normalTransform * DecompressNormal(vNORMAL));
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross( vNormalOut, vTangentOut );
#endif

    vTexCoordOut = getTexCoord(g_flTexCoordScale0, g_flTexCoordRotate0);//, g_vTexCoordOffset0, g_vTexCoordScroll0);
    vTexCoord1Out = getTexCoord(g_flTexCoordScale1, g_flTexCoordRotate1);//, g_vTexCoordOffset1, g_vTexCoordScroll1);
    vTexCoord2Out = getTexCoord(g_flTexCoordScale2, g_flTexCoordRotate2);//, g_vTexCoordOffset2, g_vTexCoordScroll2);
    vTexCoord3Out = getTexCoord(g_flTexCoordScale3, g_flTexCoordRotate3);//, g_vTexCoordOffset3, g_vTexCoordScroll3);


    //vTEXCOORD1 - (X,Y,Z) - tex1, 2, and 3 blend softness (not working right now), W - reserved for worldspace uvs
    //vTEXCOORD2 - Painted tint color
    //vTEXCOORD3 - X - amount of tex1, Y - amount of tex2, Z - amount of tex3, W - reserved for worldspace uvs

    vBlendWeights.xyz = vTEXCOORD3.xyz / 255.0;
    vBlendWeights.w = 0.0;
    vBlendAlphas.xyz = vec3(0.25);//max(vTEXCOORD1.xyz * 0.5, 1e-6);
    vBlendAlphas.w = 0.0;

    vVertexColor.rgb = SrgbGammaToLinear(m_vTintColorDrawCall.rgb) * m_vTintColorSceneObject.rgb * SrgbGammaToLinear(vTEXCOORD2.rgb/255);
    vVertexColor.a = m_vTintColorSceneObject.a;
}
