// clamp(value, 0.0, 1.0)
float saturate(float val) {
    return clamp(val, 0, 1);
}
vec2 saturate(vec2 val) {
    return clamp(val, 0.0, 1.0);
}
vec3 saturate(vec3 val) {
    return clamp(val, 0.0, 1.0);
}
vec4 saturate(vec4 val) {
    return clamp(val, 0.0, 1.0);
}

// value^2
float pow2(float val)
{
    return(val * val);
}
vec2 pow2(vec2 val)
{
    return(val * val);
}
vec3 pow2(vec3 val)
{
    return(val * val);
}
vec4 pow2(vec4 val)
{
    return(val * val);
}

float pow5(float val)
{
    return pow(val, 5.0); // Should get optimized
}

float Random2D(vec2 Seed)
{
    return fract(sin( dot(Seed, vec2(12.9898, 78.233)) ) * 43758.5469);
}



float min3( vec3 Vector )
{
	return min( min( Vector.x, Vector.y ), Vector.z );
}
float max3( vec3 Vector )
{
	return max( max( Vector.x, Vector.y ), Vector.z );
}



float length_squared(vec2 vector)
{
	return dot(vector, vector);
}
float length_squared(vec3 vector)
{
	return dot(vector, vector);
}
float length_squared(vec4 vector)
{
	return dot(vector, vector);
}


//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Color
//-------------------------------------------------------------------------------------------------------------------------------------------------------------

float GetLuma(vec3 Color) {
	return dot( Color, vec3(0.2126, 0.7152, 0.0722) );
}

float SrgbGammaToLinear(float color)
{
    float vLinearSegment = color / 12.92;
    float vExpSegment = pow((color / 1.055) + 0.0521327, 2.4);

    const float cap = 0.04045;
    float select = color > cap ? vExpSegment : vLinearSegment;

    return select;
}
vec2 SrgbGammaToLinear(vec2 color)
{
    vec2 vLinearSegment = color / vec2(12.92);
    vec2 vExpSegment = pow((color / vec2(1.055)) + vec2(0.0521327), vec2(2.4));

    const float cap = 0.04045;
    float select = color.r > cap ? vExpSegment.r : vLinearSegment.r;
    float select1 = color.g > cap ? vExpSegment.g : vLinearSegment.g;

    return vec2(select, select1);
}
vec3 SrgbGammaToLinear(vec3 color)
{
    vec3 vLinearSegment = color / vec3(12.92);
    vec3 vExpSegment = pow((color / vec3(1.055)) + vec3(0.0521327), vec3(2.4));

    const float cap = 0.04045;
    float select = color.r > cap ? vExpSegment.r : vLinearSegment.r;
    float select1 = color.g > cap ? vExpSegment.g : vLinearSegment.g;
    float select2 = color.b > cap ? vExpSegment.b : vLinearSegment.b;

    return vec3(select, select1, select2);
}
vec4 SrgbGammaToLinear(vec4 color)
{
    vec4 vLinearSegment = color / vec4(12.92);
    vec4 vExpSegment = pow((color / vec4(1.055)) + vec4(0.0521327), vec4(2.4));

    const float cap = 0.04045;
    float select = color.r > cap ? vExpSegment.r : vLinearSegment.r;
    float select1 = color.g > cap ? vExpSegment.g : vLinearSegment.g;
    float select2 = color.b > cap ? vExpSegment.b : vLinearSegment.b;
    float select3 = color.a > cap ? vExpSegment.a : vLinearSegment.a;

    return vec4(select, select1, select2, select3);
}


vec2 Resize2D(float Base, vec4 StartPos_Size)
{
	return StartPos_Size.xy + Base * StartPos_Size.zw;
}

vec2 Resize2D(vec2 Base, vec4 StartPos_Size)
{
	return StartPos_Size.xy + Base * StartPos_Size.zw;
}


vec2 RotateVector2D(vec2 vectorInput, float rotation, vec2 scale, vec2 offset)
{
    rotation = radians(rotation);
    float sinRot = sin(rotation);
    float cosRot = cos(rotation);

    vec2 finalOffset = (scale.xy * -0.5) * vec2(cosRot - sinRot, sinRot - cosRot) + offset + vec2(0.5);
    vec4 xform = scale.xxyy * vec4(cosRot, -sinRot, sinRot, cosRot);

    return vec2(dot(xform.xy, vectorInput.xy), dot(xform.zw, vectorInput.xy)) + finalOffset;
}

vec2 RotateVector2D(vec2 vectorInput, float rotation, vec2 scale, vec2 offset, vec2 center)
{

    rotation = radians(rotation);
    float sinRot = sin(rotation);
    float cosRot = cos(rotation);

    vec4 xform = vec4(cosRot * scale.x, -sinRot * scale.y, sinRot * scale.x, cosRot* scale.y);

    vec2 finalOffset;
    finalOffset.x = (center.x + offset.x) + (sinRot * center.y) - (cosRot * center.x);
    finalOffset.y = (center.y + offset.y) - (sinRot * center.x) + (cosRot * center.y);
    // ^ all of this should be precalculated

    return vec2(dot(xform.xy, vectorInput.xy), dot(xform.zw, vectorInput.xy)) + finalOffset;
}




const vec3 gamma = vec3(2.2);
const vec3 invGamma = vec3(1.0 / gamma);





// All of this is directly ported from The Lab Renderer from the unity store.
// These are their utility functions, at least as of the lab.

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float ClampToPositive( float flValue )
{
	return max( 0.0, flValue );
}

vec2 ClampToPositive( vec2 vValue )
{
	return max( vec2(0.0), vValue.xy );
}

vec3 ClampToPositive( vec3 vValue )
{
	return max( vec3(0.0), vValue.xyz );
}

vec4 ClampToPositive( vec4 vValue )
{
	return max( vec4(0.0), vValue.xyzw );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// basically map to 0-1
float LinearRamp( float flMin, float flMax, float flInput )
{
	return saturate( ( flInput - flMin ) / ( flMax - flMin ) );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Remap a value in the range [A,B] to [C,D].
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float RemapVal( float flOldVal, float flOldMin, float flOldMax, float flNewMin, float flNewMax )
{
	// Put the old val into 0-1 range based on the old min/max
	float flValNormalized = ( flOldVal - flOldMin ) / ( flOldMax - flOldMin );

	// Map 0-1 range into new min/max
	return ( flValNormalized * ( flNewMax - flNewMin ) ) + flNewMin;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Remap a value in the range [A,B] to [C,D]. Values <A map to C, and >B maps to D.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float RemapValClamped( float flOldVal, float flOldMin, float flOldMax, float flNewMin, float flNewMax )
{
	// Put the old val into 0-1 range based on the old min/max
	float flValNormalized = saturate( ( flOldVal - flOldMin ) / ( flOldMax - flOldMin ) );

	// Map 0-1 range into new min/max
	return ( flValNormalized * ( flNewMax - flNewMin ) ) + flNewMin;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
vec4 PackToColor( vec4 vValue )
{
	return ( ( vValue.xyzw * 0.5 ) + 0.5 );
}

vec3 PackToColor( vec3 vValue )
{
	return ( ( vValue.xyz * 0.5 ) + 0.5 );
}

vec2 PackToColor( vec2 vValue )
{
	return ( ( vValue.xy * 0.5 ) + 0.5 );
}

float PackToColor( float flValue )
{
	return ( ( flValue * 0.5 ) + 0.5 );
}

vec4 UnpackFromColor( vec4 cColor )
{
	return ( ( cColor.xyzw * 2.0 ) - 1.0 );
}

vec3 UnpackFromColor( vec3 cColor )
{
	return ( ( cColor.xyz * 2.0 ) - 1.0 );
}

vec2 UnpackFromColor( vec2 cColor )
{
	return ( ( cColor.xy * 2.0 ) - 1.0 );
}

float UnpackFromColor( float flColor )
{
	return ( ( flColor * 2.0 ) - 1.0 );
}
