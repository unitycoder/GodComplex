////////////////////////////////////////////////////////////////////////////////
// Global Helpers
////////////////////////////////////////////////////////////////////////////////
#ifndef __GLOBAL
#define __GLOBAL

static const float	PI = 3.1415926535897932384626433832795;			// ...
static const float	TWOPI = 6.283185307179586476925286766559;		// 2PI
static const float	FOURPI = 12.566370614359172953850573533118;		// 4PI
static const float	HALFPI = 1.5707963267948966192313216916398;		// PI/2
static const float	INVPI = 0.31830988618379067153776752674503;		// 1/PI
static const float	INVHALFPI = 0.63661977236758134307553505349006;	// 1/(PI/2)
static const float	INVTWOPI = 0.15915494309189533576888376337251;	// 1/2PI
static const float	INVFOURPI = 0.07957747154594766788444188168626;	// 1/4PI

static const float3	LUMINANCE = float3( 0.2126, 0.7152, 0.0722 );	// D65 Illuminant and 2� observer (cf. http://wiki.nuaj.net/index.php?title=Colorimetry)

static const float	INFINITY = 1e6;


////////////////////////////////////////////////////////////////////////////////////////
// Samplers
SamplerState LinearClamp	: register( s0 );
SamplerState PointClamp		: register( s1 );
SamplerState LinearWrap		: register( s2 );
SamplerState PointWrap		: register( s3 );
SamplerState LinearMirror	: register( s4 );
SamplerState PointMirror	: register( s5 );
SamplerState LinearBorder	: register( s6 );	// Black border

SamplerComparisonState ShadowSampler	: register( s7 );	// Sampler with comparison


////////////////////////////////////////////////////////////////////////////////////////
// Constants
cbuffer	cbCamera	: register( b0 )
{
	float4		_CameraData;		// X=tan(FOV_H/2) Y=tan(FOV_V/2) Z=Near W=Far
	float4x4	_Camera2World;
	float4x4	_World2Camera;
	float4x4	_Camera2Proj;
	float4x4	_Proj2Camera;
	float4x4	_World2Proj;
	float4x4	_Proj2World;
};

// cbuffer	cbGlobal	: register( b1 )
// {
// 	float4		_Time;				// X=Time Y=DeltaTime Z=1/Time W=1/DeltaTime
// };

//Texture3D<float4>	_TexNoise3D	: register(t0);



////////////////////////////////////////////////////////////////////////////////////////
// Distort position with noise
// float3	Distort( float3 _Position, float3 _Normal, float4 _NoiseOffset )
// {
// 	float	Noise = _NoiseOffset.w * (-1.0 + _TexNoise3D.SampleLevel( LinearWrap, 0.2 * (_Position + _NoiseOffset.xyz), 0.0 ).x);
// 	return	_Position + Noise * _Normal;
// }
// 
// float3	Distort( float3 _Position, float3 _Normal, float4 _NoiseOffset )
// {
// 	return _Position + _NoiseOffset.w * _TexNoise3D.SampleLevel( LinearWrap, 0.2 * (_Position + _NoiseOffset.xyz), 0.0 ).xyz;
// }


////////////////////////////////////////////////////////////////////////////////////////
// Standard bilinear interpolation on a quad
//
//	a ---- d --> U
//	|      |
//	|      |
//	|      |
//	b ---- c
//  :
//  v V
//
#define BILERP( a, b, c, d, uv )	lerp( lerp( a, d, uv.x ), lerp( b, c, uv.x ), uv.y )


////////////////////////////////////////////////////////////////////////////////////////
// Tests with infinity
float	IsInfinity( float _Value )
{
	return step( INFINITY, _Value );
}

////////////////////////////////////////////////////////////////////////////////////////
// Rotates a vector about an axis
// float3	RotateVector( float3 v, float3 _Axis, float _Angle )
// {
//		_Axis = normalize( _Axis );
//		float3	n = _Axis * dot( _Axis, v );
// 		float2	SC;
// 		sincos( _Angle, SC.x, SC.y );
//		return n + SC.y * (v - n) + SC.x * cross( _Axis, v );
// }

float3	RotateVector( float3 _Vector, float3 _Axis, float _Angle )
{
	float2	SinCos;
	sincos( _Angle, SinCos.x, SinCos.y );

	float3	Result = _Vector * SinCos.y;
	float	temp = dot( _Vector, _Axis );
			temp *= 1.0 - SinCos.y;

	Result += _Axis * temp;

	float3	Ortho = cross( _Axis, _Vector );

	Result += Ortho * SinCos.x;

	return Result;
}

// Builds a rotation matrix from an angle and axis
float3x3	BuildRotationMatrix( float _Angle, float3 _Axis )
{
	float	c, s;
	sincos( 0.5f * _Angle, s, c );
	float4	q = float4( s * normalize( _Axis ), c );

	float	xs = 2.0 * q.x;
	float	ys = 2.0 * q.y;
	float	zs = 2.0 * q.z;

	float	wx, wy, wz, xx, xy, xz, yy, yz, zz;
	wx = q.w * xs;	wy = q.w * ys;	wz = q.w * zs;
	xx = q.x * xs;	xy = q.x * ys;	xz = q.x * zs;
	yy = q.y * ys;	yz = q.y * zs;	zz = q.z * zs;

	return float3x3( 1.0 -	yy - zz,		xy + wz,		xz - wy,
							xy - wz, 1.0 -	xx - zz,		yz + wx,
							xz + wy,		yz - wx, 1.0 -	xx - yy );
}

#endif