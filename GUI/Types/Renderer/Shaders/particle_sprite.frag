#version 400

#include "common/utils.glsl"

// Render modes -- Switched on/off by code
#define renderMode_Color 0

uniform sampler2D uTexture;
uniform float uOverbrightFactor;

in vec2 vTexCoordOut;
in vec4 vColor;

out vec4 fragColor;

void main(void) {
    vec4 color = texture(uTexture, vTexCoords);

    vec3 finalColor = vColor.rgb * color.rgb;
    float blendingFactor = uOverbrightFactor * GetLuma(finalColor.rgb);

    fragColor = vec4(finalColor, vColor.a * color.a * blendingFactor);

#if renderMode_Color == 1
    fragColor = vec4(finalColor, 1.0);
#endif
}
