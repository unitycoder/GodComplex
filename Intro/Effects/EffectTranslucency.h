#pragma once

template<typename> class CB;

class EffectTranslucency
{
private:	// CONSTANTS

	static const int	DIFFUSION_SIZE = 128;	// If you change this, also make sure you change the "TARGET_SIZE" macro in the m_pMatDiffusion material compilation !
	static const int	DIFFUSION_PASSES_COUNT = 64;


public:		// NESTED TYPES

	struct CBObject
	{
		float4x4	Local2World;	// Local=>World transform to rotate the object
		float4	EmissiveColor;
		float4	NoiseOffset;	// XYZ=Noise Position  W=NoiseAmplitude
	};

	struct CBDiffusion
	{
		float		BBoxSize;				// Size of the bbox (in meters)
		float		SliceThickness;			// Thickness of each slice we take (in meters)
		float		TexelSize;				// Size of a texel (in meters)
		float		__PAD0;

		float3	ExtinctionCoeff;		// Extinction through the material
		float		__PAD1;

		float3	Albedo;					// Material albedo (to determine scattering)
		float		__PAD2;

		float3	Phase0;					// The phase weights at 0, 1 and 2 pixels away from sampling position
		float		__PAD3;
		float3	Phase1;
		float		__PAD4;
		float3	Phase2;
		float		__PAD5;

		float3	ExternalLight;
		float		__PAD6;
		float3	InternalEmissive;		// The emissive color of the internal object (normally 0)
	};

	struct CBPass
	{
		float		PassIndex;
		float		CurrentZ;
		float		NextZ;
	};

private:	// FIELDS

	int					m_ErrorCode;
	Texture2D&			m_RTTarget;

	Shader*			m_pMatBuildZBuffer;	// Renders the internal & exteral objects into a single RGBA16F linear ZBuffer
	Shader*			m_pMatDiffusion;	// Performs light diffusion through the volume
	Shader*			m_pMatDisplay;		// Some material for primitive display

	Primitive*			m_pPrimTorusInternal;
	Primitive*			m_pPrimSphereExternal;

	Texture2D*			m_pRTZBuffer;
	Texture2D*			m_pDepthStencil;	// The depth stencil adapted to the ZBuffers rendering
	Texture2D*			m_ppRTDiffusion[2];

	CB<CBObject>*		m_pCB_Object;
	CB<CBDiffusion>*	m_pCB_Diffusion;
	CB<CBPass>*			m_pCB_Pass;


	// Params
public:
	float				m_EmissivePower;


public:		// PROPERTIES

	int			GetErrorCode() const	{ return m_ErrorCode; }

	Texture2D*	GetZBuffer()			{ return m_pRTZBuffer; }
	Texture2D*	GetIrradiance()			{ return m_ppRTDiffusion[0]; }

public:		// METHODS

	EffectTranslucency( Texture2D& _RTTarget );
	~EffectTranslucency();

	void	Render( float _Time, float _DeltaTime );

protected:

	float3	ComputePhase( const float3& _Anisotropy, int _PixelDistance, int _SamplesCount, float _TexelSize, float _SliceThickness );

};