#include "../../GodComplex.h"
#include "EffectVolumetric.h"

#define CHECK_MATERIAL( pMaterial, ErrorCode )		if ( (pMaterial)->HasErrors() ) m_ErrorCode = ErrorCode;

static const int	TERRAIN_SUBDIVISIONS_COUNT = 200;	// Don't push it over 254 or it will crash due to more than 65536 vertices!
static const float	TERRAIN_SIZE = 100.0f;

static const float	CLOUD_SIZE = 100.0f;

static const float	SCREEN_TARGET_RATIO = 0.25f;

static const float	GROUND_RADIUS_KM = 6360.0f;
static const float	ATMOSPHERE_THICKNESS_KM = 60.0f;

static const float	TRANSMITTANCE_TAN_MAX = 1.5f;	// Close to PI/2 to maximize precision at grazing angles
//#define USE_PRECISE_COS_THETA_MIN


EffectVolumetric::EffectVolumetric( Device& _Device, Texture2D& _RTHDR, Primitive& _ScreenQuad, Camera& _Camera ) : m_Device( _Device ), m_RTHDR( _RTHDR ), m_ScreenQuad( _ScreenQuad ), m_Camera( _Camera ), m_ErrorCode( 0 ), m_pTableTransmittance( NULL )
{
	//////////////////////////////////////////////////////////////////////////
	// Create the materials
 	CHECK_MATERIAL( m_pMatDownsampleDepth = CreateComputeShader( IDR_SHADER_VOLUMETRIC_DOWNSAMPLE_DEPTH, "./Resources/Shaders/VolumetricDownsampleDepth.hlsl", "CS" ), 1 );
 	CHECK_MATERIAL( m_pMatDepthWrite = CreateMaterial( IDR_SHADER_VOLUMETRIC_DEPTH_WRITE, "./Resources/Shaders/VolumetricDepthWrite.hlsl", VertexFormatP3::DESCRIPTOR, "VS", NULL, "PS" ), 2 );
 	CHECK_MATERIAL( m_pMatSplatCameraFrustum = CreateMaterial( IDR_SHADER_VOLUMETRIC_COMPUTE_TRANSMITTANCE, "./Resources/Shaders/VolumetricComputeTransmittance.hlsl", VertexFormatP3::DESCRIPTOR, "VS_SplatFrustum", NULL, "PS_SplatFrustum" ), 3 );
 	CHECK_MATERIAL( m_pMatComputeTransmittance = CreateMaterial( IDR_SHADER_VOLUMETRIC_COMPUTE_TRANSMITTANCE, "./Resources/Shaders/VolumetricComputeTransmittance.hlsl", VertexFormatPt4::DESCRIPTOR, "VS", NULL, "PS" ), 4 );

 	CHECK_MATERIAL( m_pMatDepthPrePass = CreateMaterial( IDR_SHADER_VOLUMETRIC_DEPTH_PREPASS, "./Resources/Shaders/VolumetricDepthPrePass.hlsl", VertexFormatPt4::DESCRIPTOR, "VS", NULL, "PS" ), 5 );

	D3D_SHADER_MACRO	pMacrosAboveClouds[] = {
		{ "CAMERA_ABOVE_CLOUDS", "1" },
		{ NULL,	NULL }
	};
	CHECK_MATERIAL( m_ppMatDisplay[0] = CreateMaterial( IDR_SHADER_VOLUMETRIC_DISPLAY, "./Resources/Shaders/VolumetricDisplay.hlsl", VertexFormatPt4::DESCRIPTOR, "VS", NULL, "PS" ), 6 );
	CHECK_MATERIAL( m_ppMatDisplay[1] = CreateMaterial( IDR_SHADER_VOLUMETRIC_DISPLAY, "./Resources/Shaders/VolumetricDisplay.hlsl", VertexFormatPt4::DESCRIPTOR, "VS", NULL, "PS", pMacrosAboveClouds ), 7 );
// 	CHECK_MATERIAL( m_ppMatDisplay[0] = CreateMaterial( IDR_SHADER_VOLUMETRIC_DISPLAY, "./Resources/Shaders/VolumetricDisplay_AtmosphereOnly.hlsl", VertexFormatPt4::DESCRIPTOR, "VS", NULL, "PS" ), 6 );//### DEBUG ATMOSPHERE TABLES!
// 	CHECK_MATERIAL( m_ppMatDisplay[1] = CreateMaterial( IDR_SHADER_VOLUMETRIC_DISPLAY, "./Resources/Shaders/VolumetricDisplay_AtmosphereOnly.hlsl", VertexFormatPt4::DESCRIPTOR, "VS", NULL, "PS", pMacrosAboveClouds ), 7 );

 	CHECK_MATERIAL( m_pMatCombine = CreateMaterial( IDR_SHADER_VOLUMETRIC_COMBINE, "./Resources/Shaders/VolumetricCombine.hlsl", VertexFormatPt4::DESCRIPTOR, "VS", NULL, "PS" ), 8 );

#ifdef SHOW_TERRAIN
	CHECK_MATERIAL( m_pMatTerrainShadow = CreateMaterial( IDR_SHADER_VOLUMETRIC_TERRAIN, "./Resources/Shaders/VolumetricTerrain.hlsl", VertexFormatP3::DESCRIPTOR, "VS", NULL, NULL ), 9 );
	CHECK_MATERIAL( m_pMatTerrain = CreateMaterial( IDR_SHADER_VOLUMETRIC_TERRAIN, "./Resources/Shaders/VolumetricTerrain.hlsl", VertexFormatP3::DESCRIPTOR, "VS", NULL, "PS" ), 10 );
#endif

//	const char*	pCSO = LoadCSO( "./Resources/Shaders/CSO/VolumetricCombine.cso" );
//	CHECK_MATERIAL( m_pMatCombine = CreateMaterial( IDR_SHADER_VOLUMETRIC_COMBINE, VertexFormatPt4::DESCRIPTOR, "VS", NULL, pCSO ), 4 );
//	delete[] pCSO;


	//////////////////////////////////////////////////////////////////////////
	// Pre-Compute multiple-scattering tables

// I think it would be better to allocate [1] targets with the D3D11_BIND_UNORDERED_ACCESS flag and COPY the resource once it's been computed,
// 	rather than exchanging pointers. I'm not sure the device won't consider targets with the UAV and RT and SRV flags slower??
//
// After testing: Performance doesn't seem to be affected at all...

#define UAV	true
//#define UAV	false

	m_ppRTTransmittance[0] = new Texture2D( m_Device, TRANSMITTANCE_W, TRANSMITTANCE_H, 1, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );			// transmittance (final)
	m_ppRTTransmittance[1] = new Texture2D( m_Device, TRANSMITTANCE_W, TRANSMITTANCE_H, 1, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );
	m_ppRTTransmittanceLimited[0] = new Texture3D( m_Device, TRANSMITTANCE_LIMITED_W, TRANSMITTANCE_LIMITED_H, TRANSMITTANCE_LIMITED_D, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );	// transmittance with limited distance (final)
	m_ppRTTransmittanceLimited[1] = new Texture3D( m_Device, TRANSMITTANCE_LIMITED_W, TRANSMITTANCE_LIMITED_H, TRANSMITTANCE_LIMITED_D, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );
	m_ppRTIrradiance[0] = new Texture2D( m_Device, IRRADIANCE_W, IRRADIANCE_H, 1, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );					// irradiance (final)
	m_ppRTIrradiance[1] = new Texture2D( m_Device, IRRADIANCE_W, IRRADIANCE_H, 1, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );
	m_ppRTIrradiance[2] = new Texture2D( m_Device, IRRADIANCE_W, IRRADIANCE_H, 1, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );
	m_ppRTScattering[0] = new Texture3D( m_Device, RES_3D_U, RES_3D_COS_THETA_VIEW, RES_3D_ALTITUDE, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );	// inscatter (final)
	m_ppRTScattering[1] = new Texture3D( m_Device, RES_3D_U, RES_3D_COS_THETA_VIEW, RES_3D_ALTITUDE, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );
	m_ppRTScattering[2] = new Texture3D( m_Device, RES_3D_U, RES_3D_COS_THETA_VIEW, RES_3D_ALTITUDE, PixelFormatRGBA32F::DESCRIPTOR, 1, NULL, false, UAV );

	// Setup to their target slots, even though they're not computed yet... That's just to avoid annoying warnings in the console.
	m_ppRTTransmittance[0]->Set( 6, true );
	m_ppRTTransmittanceLimited[0]->Set( 7, true );
	m_ppRTScattering[0]->Set( 8, true );
	m_ppRTIrradiance[0]->Set( 9, true );

	InitSkyTables();


	//////////////////////////////////////////////////////////////////////////
	// Build the primitives
	{
		m_pPrimBox = new Primitive( m_Device, VertexFormatP3::DESCRIPTOR );
		GeometryBuilder::BuildCube( 1, 1, 1, *m_pPrimBox );
	}

// 	{
// 		float		TanFovH = m_Camera.GetCB().Params.x;
// 		float		TanFovV = m_Camera.GetCB().Params.y;
// 		float		FarClip = m_Camera.GetCB().Params.w;
// 
// 		// Build the 5 vertices of the frustum pyramid, in camera space
// 		NjFloat3	pVertices[5];
// 		pVertices[0] = NjFloat3( 0, 0, 0 );
// 		pVertices[1] = FarClip * NjFloat3( -TanFovH, +TanFovV, 1 );
// 		pVertices[2] = FarClip * NjFloat3( -TanFovH, -TanFovV, 1 );
// 		pVertices[3] = FarClip * NjFloat3( +TanFovH, -TanFovV, 1 );
// 		pVertices[4] = FarClip * NjFloat3( +TanFovH, +TanFovV, 1 );
// 
// 		U16			pIndices[18];
// 		pIndices[3*0+0] = 0;	// Left face
// 		pIndices[3*0+1] = 1;
// 		pIndices[3*0+2] = 2;
// 		pIndices[3*1+0] = 0;	// Bottom face
// 		pIndices[3*1+1] = 2;
// 		pIndices[3*1+2] = 3;
// 		pIndices[3*2+0] = 0;	// Right face
// 		pIndices[3*2+1] = 3;
// 		pIndices[3*2+2] = 4;
// 		pIndices[3*3+0] = 0;	// Top face
// 		pIndices[3*3+1] = 4;
// 		pIndices[3*3+2] = 1;
// 		pIndices[3*4+0] = 1;	// Back faces
// 		pIndices[3*4+1] = 3;
// 		pIndices[3*4+2] = 2;
// 		pIndices[3*5+0] = 1;
// 		pIndices[3*5+1] = 4;
// 		pIndices[3*5+2] = 3;
// 
// 		m_pPrimFrustum = new Primitive( m_Device, 5, pVertices, 18, pIndices, D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST, VertexFormatP3::DESCRIPTOR );
// 	}

#ifdef SHOW_TERRAIN
	{
		m_pPrimTerrain = new Primitive( m_Device, VertexFormatP3::DESCRIPTOR );
		GeometryBuilder::BuildPlane( 200, 200, float3::UnitX, -float3::UnitZ, *m_pPrimTerrain );
	}
#endif

	//////////////////////////////////////////////////////////////////////////
	// Build textures & render targets
	m_pRTCameraFrustumSplat = new Texture2D( m_Device, SHADOW_MAP_SIZE, SHADOW_MAP_SIZE, 1, PixelFormatR8::DESCRIPTOR, 1, NULL );
	m_pRTTransmittanceMap = new Texture2D( m_Device, SHADOW_MAP_SIZE, SHADOW_MAP_SIZE, 2, PixelFormatRGBA16F::DESCRIPTOR, 1, NULL );

	int	W = m_Device.DefaultRenderTarget().GetWidth();
	int	H = m_Device.DefaultRenderTarget().GetHeight();
	m_RenderWidth = int( ceilf( W * SCREEN_TARGET_RATIO ) );
	m_RenderHeight = int( ceilf( H * SCREEN_TARGET_RATIO ) );

	m_pRTDownsampledDepth = new Texture2D( m_Device, W >> 1, H >> 1, 1, PixelFormatRGBA16F::DESCRIPTOR, 3, NULL, false, true );

	m_pRTRenderZ = new Texture2D( m_Device, m_RenderWidth, m_RenderHeight, 1, PixelFormatRG16F::DESCRIPTOR, 1, NULL );
	m_pRTRender = new Texture2D( m_Device, m_RenderWidth, m_RenderHeight, 2, PixelFormatRGBA16F::DESCRIPTOR, 1, NULL );

	int	DepthPassWidth = m_RenderWidth / 2;
	int	DepthPassHeight	= m_RenderHeight / 2;
	m_pRTVolumeDepth = new Texture2D( m_Device, DepthPassWidth, DepthPassHeight, 1, PixelFormatRG16F::DESCRIPTOR, 1, NULL );

//	m_pTexFractal0 = BuildFractalTexture( true );
	m_pTexFractal1 = BuildFractalTexture( false );

#ifdef SHOW_TERRAIN
	m_pRTTerrainShadow = new Texture2D( m_Device, TERRAIN_SHADOW_MAP_SIZE, TERRAIN_SHADOW_MAP_SIZE, DepthStencilFormatD32F::DESCRIPTOR );
#endif

	//////////////////////////////////////////////////////////////////////////
	// Create the constant buffers
	m_pCB_Object = new CB<CBObject>( m_Device, 10 );
	m_pCB_Splat = new CB<CBSplat>( m_Device, 10 );
	m_pCB_Atmosphere = new CB<CBAtmosphere>( m_Device, 7, true );
	m_pCB_Shadow = new CB<CBShadow>( m_Device, 8, true );
	m_pCB_Volume = new CB<CBVolume>( m_Device, 9, true );

	//////////////////////////////////////////////////////////////////////////
	// Setup our volume & light
	m_Position = float3( 0.0f, 2.0f, 0.0f );
	m_Rotation = float4::QuatFromAngleAxis( 0.0f, float3::UnitY );
	m_Scale = float3( 1.0f, 2.0f, 1.0f );

	m_CloudAnimSpeedLoFreq = 1.0f;
	m_CloudAnimSpeedHiFreq = 1.0f;

	{
		float	SunPhi = 0.0f;
		float	SunTheta = 0.25f * PI;
		m_pCB_Atmosphere->m.LightDirection.Set( sinf(SunPhi)*sinf(SunTheta), cosf(SunTheta), -cosf(SunPhi)*sinf(SunTheta) );
	}

#ifdef _DEBUG
	m_pMMF = new MMF<ParametersBlock>( "BisouTest" );
	ParametersBlock	Params = {
		1, // WILL BE MARKED AS CHANGED!			// U32		Checksum;

		// // Atmosphere Params
		0.25f,		// float	SunTheta;
		0.0f,		// float	SunPhi;
		100.0f,		// float	SunIntensity;
		1.0f,		// float	AirAmount;		// Simply a multiplier of the default value
		0.004f,		// float	FogScattering;
		0.004f / 0.9f,		// float	FogExtinction;
		8.0f,		// float	AirReferenceAltitudeKm;
		1.2f,		// float	FogReferenceAltitudeKm;
		0.76f,		// float	FogAnisotropy;
		0.1f,		// float	AverageGroundReflectance;
		1.0f,		// float	GodraysStrength Rayleigh;
		4.0f,		// float	GodraysStrength Mie;
		-1.0f,		// float	AltitudeOffset;

		// // Volumetrics Params
		4.0f,		// float	CloudBaseAltitude;
		2.0f,		// float	CloudThickness;
		8.0f,		// float	CloudExtinction;
		8.0f,		// float	CloudScattering;
		0.1f,		// float	CloudAnisotropyIso;
		0.85f,		// float	CloudAnisotropyForward;
		0.9f,		// float	CloudShadowStrength;
				// 
		0.05f,		// float	CloudIsotropicScattering;	// Sigma_s for isotropic lighting
		0.5f,		// float	CloudIsoSkyRadianceFactor;
		0.02f,		// float	CloudIsoSunRadianceFactor;
		0.2f,		// float	CloudIsoTerrainReflectanceFactor;

		// // Noise Params
				// 	// Low frequency noise
		7.5f,		// float	NoiseLoFrequency;		// Horizontal frequency
		1.0f,		// float	NoiseLoVerticalLooping;	// Vertical frequency in amount of noise pixels
		1.0f,		// float	NoiseLoAnimSpeed;		// Animation speed
				// 	// High frequency noise
		0.12f,		// float	NoiseHiFrequency;
		0.01f,		// float	NoiseHiOffset;			// Second noise is added to first noise using NoiseHiStrength * (HiFreqNoise + NoiseHiOffset)
		-0.707f,	// float	NoiseHiStrength;
		1.0f,		// float	NoiseHiAnimSpeed;
				// 	// Combined noise params
		-0.16f,		// float	NoiseOffsetBottom;		// The noise offset to add when at the bottom altitude in the cloud
		0.01f,		// float	NoiseOffsetMiddle;		// The noise offset to add when at the middle altitude in the cloud
		-0.16f,		// float	NoiseOffsetTop;			// The noise offset to add when at the top altitude in the cloud
		1.0f,		// float	NoiseContrast;			// Final noise value is Noise' = pow( Contrast*(Noise+Offset), Gamma )
		0.5f,		// float	NoiseGamma;
				// 	// Final shaping params
		0.01f,		// float	NoiseShapingPower;		// Final noise value is shaped (multiplied) by pow( 1-abs(2*y-1), NoiseShapingPower ) to avoid flat plateaus at top or bottom

		// // Terrain Params
		1,			// int		TerrainEnabled;
		10.0f,		// float	TerrainHeight;
		2.0f,		// float	TerrainAlbedoMultiplier;
		0.9f,		// float	TerrainCloudShadowStrength;

	};
	EffectVolumetric::ParametersBlock&	MappedParams = m_pMMF->GetMappedMemory();

	// Copy our default params only if the checksum is 0 (meaning the control panel isn't loaded and hasn't set any valu yet)
	if ( MappedParams.Checksum == 0 )
		MappedParams = Params;

	m_CloudAnimSpeedLoFreq = Params.NoiseLoAnimSpeed;
	m_CloudAnimSpeedHiFreq = Params.NoiseHiAnimSpeed;

#endif

	m_pCB_Volume->m._CloudLoFreqPositionOffset.Set( 0, 0 );
	m_pCB_Volume->m._CloudHiFreqPositionOffset.Set( 0, 0 );
}

EffectVolumetric::~EffectVolumetric()
{
#ifdef _DEBUG
	delete m_pMMF;
#endif

	delete m_pCB_Volume;
	delete m_pCB_Shadow;
	delete m_pCB_Atmosphere;
	delete m_pCB_Splat;
	delete m_pCB_Object;

	delete m_pTexFractal1;
	delete m_pTexFractal0;
	delete m_pRTVolumeDepth;
	delete m_pRTRender;
	delete m_pRTRenderZ;
	delete m_pRTDownsampledDepth;
	delete m_pRTTransmittanceMap;
	delete m_pRTCameraFrustumSplat;

#ifdef SHOW_TERRAIN
	delete m_pRTTerrainShadow;
	delete m_pMatTerrainShadow;
	delete m_pMatTerrain;
	delete m_pPrimTerrain;
#endif
	delete m_pPrimFrustum;
	delete m_pPrimBox;

	ExitUpdateSkyTables();

	FreeSkyTables();

 	delete m_pMatCombine;
	delete m_ppMatDisplay[1];
	delete m_ppMatDisplay[0];
	delete m_pMatDepthPrePass;
 	delete m_pMatComputeTransmittance;
 	delete m_pMatSplatCameraFrustum;
	delete m_pMatDepthWrite;
	delete m_pMatDownsampleDepth;
}

#ifndef NDEBUG
#define PERF_BEGIN_EVENT( Color, Text )	D3DPERF_BeginEvent( Color, Text )
#define PERF_END_EVENT()				D3DPERF_EndEvent()
#define PERF_MARKER( Color, Text )		D3DPERF_SetMarker( Color, Text )
#else
#define PERF_BEGIN_EVENT( Color, Text )
#define PERF_END_EVENT()
#define PERF_MARKER( Color, Text )
#endif

void	EffectVolumetric::Render( float _Time, float _DeltaTime )
{
// DEBUG
float	t = 2*0.25f * _Time;
//m_LightDirection.Set( 0, 1, -1 );
//m_LightDirection.Set( 1, 2.0, -5 );
//m_LightDirection.Set( cosf(_Time), 2.0f * sinf( 0.324f * _Time ), sinf( _Time ) );
//m_LightDirection.Set( cosf(_Time), 1.0f, sinf( _Time ) );


	// Animate cloud position
	m_pCB_Volume->m._CloudLoFreqPositionOffset = m_pCB_Volume->m._CloudLoFreqPositionOffset + (_DeltaTime * m_CloudAnimSpeedLoFreq) * float2( 0.22f, -0.55f );
	m_pCB_Volume->m._CloudHiFreqPositionOffset = m_pCB_Volume->m._CloudHiFreqPositionOffset + (_DeltaTime * m_CloudAnimSpeedHiFreq) * float2( 0.0f, -0.08f );


#ifdef _DEBUG
	if ( m_pMMF->CheckForChange() )
	{
		ParametersBlock&	Params = m_pMMF->GetMappedMemory();

		//////////////////////////////////////////////////////////////////////////
		// Check if any change in params requires a sky table rebuild
		bool	bRequireSkyUpdate = false;

		bRequireSkyUpdate |= !ALMOST( m_pCB_Atmosphere->m.AirParams.x, Params.AirAmount );
		bRequireSkyUpdate |= !ALMOST( m_pCB_Atmosphere->m.AirParams.y, Params.AirReferenceAltitudeKm );
		bRequireSkyUpdate |= !ALMOST( m_pCB_Atmosphere->m.FogParams.x, Params.FogScattering );
		bRequireSkyUpdate |= !ALMOST( m_pCB_Atmosphere->m.FogParams.y, Params.FogExtinction );
		bRequireSkyUpdate |= !ALMOST( m_pCB_Atmosphere->m.FogParams.z, Params.FogReferenceAltitudeKm );
		bRequireSkyUpdate |= !ALMOST( m_pCB_Atmosphere->m.FogParams.w, Params.FogAnisotropy );
		bRequireSkyUpdate |= !ALMOST( m_pCB_PreComputeSky->m._AverageGroundReflectance, Params.AverageGroundReflectance );


		//////////////////////////////////////////////////////////////////////////
		// Atmosphere Params
		m_pCB_Atmosphere->m.LightDirection.Set( sinf(Params.SunPhi)*sinf(Params.SunTheta), cosf(Params.SunTheta), -cosf(Params.SunPhi)*sinf(Params.SunTheta) );
		m_pCB_Atmosphere->m.SunIntensity = Params.SunIntensity;

		m_pCB_Atmosphere->m.AirParams.Set( Params.AirAmount, Params.AirReferenceAltitudeKm );
		m_pCB_Atmosphere->m.GodraysStrengthRayleigh = Params.GodraysStrengthRayleigh;
		m_pCB_Atmosphere->m.GodraysStrengthMie = Params.GodraysStrengthMie;
		m_pCB_Atmosphere->m.AltitudeOffset = Params.AltitudeOffset;

		m_pCB_Atmosphere->m.FogParams.Set( Params.FogScattering, Params.FogExtinction, Params.FogReferenceAltitudeKm, Params.FogAnisotropy );

		m_pCB_Atmosphere->UpdateData();

		m_pCB_PreComputeSky->m._AverageGroundReflectance = Params.AverageGroundReflectance;

		if ( bRequireSkyUpdate )
			TriggerSkyTablesUpdate();	// Rebuild tables if change in atmosphere params!


		//////////////////////////////////////////////////////////////////////////
		// Volumetric Params
		m_CloudAltitude = Params.CloudBaseAltitude;
		m_CloudThickness = Params.CloudThickness;
// 		m_Position.Set( 0, Params.CloudBaseAltitude + 0.5f * Params.CloudThickness, -100 );
 		m_Scale.Set( 0.5f * CLOUD_SIZE, 0.5f * Params.CloudThickness, 0.5f * CLOUD_SIZE );

		m_pCB_Volume->m._CloudAltitudeThickness.Set( Params.CloudBaseAltitude, Params.CloudThickness );
		m_pCB_Volume->m._CloudExtinctionScattering.Set( Params.CloudExtinction, Params.CloudScattering );
		m_pCB_Volume->m._CloudPhases.Set( Params.CloudAnisotropyIso, Params.CloudAnisotropyForward );
		m_pCB_Volume->m._CloudShadowStrength = Params.CloudShadowStrength;

		// Isotropic lighting
		m_pCB_Volume->m._CloudIsotropicScattering = Params.CloudIsotropicScattering;
		m_pCB_Volume->m._CloudIsotropicFactors.Set( Params.CloudIsoSkyRadianceFactor, Params.CloudIsoSunRadianceFactor, Params.CloudIsoTerrainReflectanceFactor );

		// Noise
		m_pCB_Volume->m._CloudLoFreqParams.Set( Params.NoiseLoFrequency, Params.NoiseLoVerticalLooping );
		m_pCB_Volume->m._CloudHiFreqParams.Set( Params.NoiseHiFrequency, Params.NoiseHiOffset, Params.NoiseHiStrength );

		m_CloudAnimSpeedLoFreq = Params.NoiseLoAnimSpeed;
		m_CloudAnimSpeedHiFreq = Params.NoiseHiAnimSpeed;

		float	HalfMiddleOffset = Params.NoiseOffsetMiddle;
		m_pCB_Volume->m._CloudOffsets.Set( Params.NoiseOffsetBottom - HalfMiddleOffset, HalfMiddleOffset, Params.NoiseOffsetTop - HalfMiddleOffset );

		m_pCB_Volume->m._CloudContrastGamma.Set( Params.NoiseContrast, Params.NoiseGamma );
		m_pCB_Volume->m._CloudShapingPower = Params.NoiseShapingPower;

		m_pCB_Volume->UpdateData();


		//////////////////////////////////////////////////////////////////////////
		// Terrain Params
		m_bShowTerrain = Params.TerrainEnabled == 1;
		m_pCB_Object->m.TerrainHeight = Params.TerrainHeight;
		m_pCB_Object->m.AlbedoMultiplier = Params.TerrainAlbedoMultiplier;
		m_pCB_Object->m.CloudShadowStrength = Params.TerrainCloudShadowStrength;
	}
#endif

// float	SunAngle = LERP( -0.01f * PI, 0.499f * PI, 0.5f * (1.0f + sinf( t )) );		// Oscillating between slightly below horizon to zenith
// //float	SunAngle = 0.021f * PI;
// //float	SunAngle = -0.0001f * PI;
// 
// // SunAngle = _TV( 0.12f );
// //SunAngle = -0.015f * PI;	// Sexy Sunset
// //SunAngle = 0.15f * PI;	// Sexy Sunset
// 
// float	SunPhi = 0.5923f * t;
// m_LightDirection.Set( sinf( SunPhi ), sinf( SunAngle ), -cosf( SunPhi ) );
// //m_LightDirection.Set( 0.0, sinf( SunAngle ), -cosf( SunAngle ) );

// DEBUG


#ifdef _DEBUG
// 	if ( gs_WindowInfos.pKeys[VK_NUMPAD1] )
// 		m_pCB_Volume->m.Params.x -= 0.5f * _DeltaTime;
// 	if ( gs_WindowInfos.pKeys[VK_NUMPAD7] )
// 		m_pCB_Volume->m.Params.x += 0.5f * _DeltaTime;
// 
// 	m_pCB_Volume->m.Params.y = gs_WindowInfos.pKeys[VK_RETURN];
#endif

	m_pCB_Volume->UpdateData();

	if ( m_pTexFractal0 != NULL )
		m_pTexFractal0->SetPS( 16 );
	if ( m_pTexFractal1 != NULL )
		m_pTexFractal1->SetPS( 17 );


	//////////////////////////////////////////////////////////////////////////
	// Perform time-sliced update of the sky table if needed
	UpdateSkyTables();


//return;//###
#if 1	//### DRIVER PROBLEM


	//////////////////////////////////////////////////////////////////////////
	// Compute transforms
	PERF_BEGIN_EVENT( D3DCOLOR( 0xFF00FF00 ), L"Compute Transforms" );

	float3	TerrainPosition = float3::Zero;

	// Snap terrain position to match camera position so it follows around without shitty altitude scrolling
	{
		float3	CameraPosition = m_Camera.GetCB().Camera2World.GetRow( 3 );
		float3	CameraAt = m_Camera.GetCB().Camera2World.GetRow( 2 );

		float3	TerrainCenter = CameraPosition + 0.45f * TERRAIN_SIZE * CameraAt;	// The center will be in front of us always

		float	VertexSnap = TERRAIN_SIZE / TERRAIN_SUBDIVISIONS_COUNT;	// World length between 2 vertices
		int		VertexX = int( floorf( TerrainCenter.x / VertexSnap ) );
		int		VertexZ = int( floorf( TerrainCenter.z / VertexSnap ) );

		TerrainPosition.x = VertexX * VertexSnap;
		TerrainPosition.z = VertexZ * VertexSnap;
	}

	m_Terrain2World.PRS( TerrainPosition, float4::QuatFromAngleAxis( 0.0f, float3::UnitY ), float3( 0.5f * TERRAIN_SIZE, 1, 0.5f * TERRAIN_SIZE) );

	// Set cloud slab center so it follows the camera around as well...
	{
		float3	CameraPosition = m_Camera.GetCB().Camera2World.GetRow( 3 );
		float3	CameraAt = m_Camera.GetCB().Camera2World.GetRow( 2 );

		float3	CloudCenter = CameraPosition + 0.45f * CLOUD_SIZE * CameraAt;	// The center will be in front of us always

 		m_Position.Set( CloudCenter.x, m_CloudAltitude + 0.5f * m_CloudThickness, CloudCenter.z );
	}

	m_Cloud2World.PRS( m_Position, m_Rotation, m_Scale );

	ComputeShadowTransform();

	m_pCB_Shadow->m.World2TerrainShadow = ComputeTerrainShadowTransform();

	m_pCB_Shadow->UpdateData();

	PERF_END_EVENT();

	//////////////////////////////////////////////////////////////////////////
	// 1] Compute the transmittance function map
	m_Device.SetStates( m_Device.m_pRS_CullNone, m_Device.m_pDS_Disabled, m_Device.m_pBS_Disabled );

// Actually, we lose some perf!
// 	// 1.1] Splat the camera frustum that will help us isolate the pixels we actually need to compute
// 	m_Device.ClearRenderTarget( *m_pRTCameraFrustumSplat, NjFloat4( 0.0f, 0.0f, 0.0f, 0.0f ) );
// 	m_Device.SetRenderTarget( *m_pRTCameraFrustumSplat );
// 
// 	USING_MATERIAL_START( *m_pMatSplatCameraFrustum )
// 
// 		m_pCB_Splat->m.dUV = m_pRTCameraFrustumSplat->GetdUV();
// 		m_pCB_Splat->UpdateData();
// 
// 		m_pPrimFrustum->Render( M );
// 
// 	USING_MATERIAL_END

	// 1.2] Compute transmittance map
	PERF_BEGIN_EVENT( D3DCOLOR( 0xFF400000 ), L"Render TFM" );

	m_Device.ClearRenderTarget( *m_pRTTransmittanceMap, float4( 0.0f, 0.0f, 0.0f, 0.0f ) );

	D3D11_VIEWPORT	Viewport = {
		0.0f,
		0.0f,
		float(m_ViewportWidth),
		float(m_ViewportHeight),
		0.0f,	// MinDepth
		1.0f,	// MaxDepth
	};

	m_pRTTransmittanceMap->RemoveFromLastAssignedSlots();

	ID3D11RenderTargetView*	ppViews[2] = {
		m_pRTTransmittanceMap->GetRTV( 0, 0, 1 ),
		m_pRTTransmittanceMap->GetRTV( 0, 1, 1 ),
	};
	m_Device.SetRenderTargets( m_pRTTransmittanceMap->GetWidth(), m_pRTTransmittanceMap->GetHeight(), 2, ppViews, NULL, &Viewport );

	USING_MATERIAL_START( *m_pMatComputeTransmittance )

//		m_pRTCameraFrustumSplat->SetPS( 11 );

		m_pCB_Splat->m.dUV = m_pRTTransmittanceMap->GetdUV();
		m_pCB_Splat->UpdateData();

		m_ScreenQuad.Render( M );

	USING_MATERIAL_END

	// Remove contention on that Transmittance Z we don't need for the next pass...
	m_Device.RemoveShaderResources( 10 );
	m_Device.RemoveRenderTargets();

	PERF_END_EVENT();


	//////////////////////////////////////////////////////////////////////////
	// 2] Terrain shadow map
#ifdef SHOW_TERRAIN
	if ( m_bShowTerrain )
	{
		PERF_BEGIN_EVENT( D3DCOLOR( 0xFFFF8000 ), L"Compute Terrain Shadow" );

		m_pRTTerrainShadow->RemoveFromLastAssignedSlots();

		USING_MATERIAL_START( *m_pMatTerrainShadow )

			m_Device.ClearDepthStencil( *m_pRTTerrainShadow, 1, 0 );

			m_Device.SetRenderTargets( TERRAIN_SHADOW_MAP_SIZE, TERRAIN_SHADOW_MAP_SIZE, 0, NULL, m_pRTTerrainShadow->GetDSV() );
	 		m_Device.SetStates( m_Device.m_pRS_CullNone, m_Device.m_pDS_ReadWriteLess, m_Device.m_pBS_Disabled );

			m_pCB_Object->m.Local2View = m_Terrain2World;
			m_pCB_Object->m.View2Proj = m_pCB_Shadow->m.World2TerrainShadow;
			m_pCB_Object->m.dUV = m_pRTTerrainShadow->GetdUV();
			m_pCB_Object->UpdateData();

			m_pPrimTerrain->Render( M );

		USING_MATERIAL_END

//#define	INCLUDE_TERRAIN_SHADOWING_IN_TFM	// The resolution is poor anyway, and that adds a dependency on this map for all DrawCalls...

		m_Device.RemoveRenderTargets();
#ifdef INCLUDE_TERRAIN_SHADOWING_IN_TFM
		m_pRTTerrainShadow->SetPS( 5, true );
#endif

		PERF_END_EVENT();

		//////////////////////////////////////////////////////////////////////////
		// 3] Show terrain
		m_pRTTransmittanceMap->SetPS( 4, true );	// Now we need the TFM!

 		PERF_BEGIN_EVENT( D3DCOLOR( 0xFFFFFF00 ), L"Render Terrain" );

		USING_MATERIAL_START( *m_pMatTerrain )

			m_Device.SetRenderTarget( m_RTHDR, &m_Device.DefaultDepthStencil() );
	 		m_Device.SetStates( m_Device.m_pRS_CullBack, m_Device.m_pDS_ReadWriteLess, m_Device.m_pBS_Disabled );

			m_pCB_Object->m.Local2View = m_Terrain2World;
			m_pCB_Object->m.View2Proj = m_Camera.GetCB().World2Proj;
			m_pCB_Object->m.dUV = m_RTHDR.GetdUV();
			m_pCB_Object->UpdateData();

			m_pPrimTerrain->Render( M );

		USING_MATERIAL_END

		PERF_END_EVENT();
	}

	m_Device.RemoveRenderTargets();	// So we can now access the depth buffer...

#endif

	m_pRTTransmittanceMap->SetPS( 4, true );	// Now we need the TFM!


	//////////////////////////////////////////////////////////////////////////
	// 4] Downsample Depth Buffer
	PERF_BEGIN_EVENT( D3DCOLOR( 0xFF200000 ), L"Downsample Depth Buffer" );

	USING_COMPUTESHADER_START( *m_pMatDownsampleDepth )

		m_Device.DefaultDepthStencil().SetCS( 10 );

		m_pRTDownsampledDepth->SetCSUAV( 0, m_pRTDownsampledDepth->GetUAV( 0 ) );
		m_pRTDownsampledDepth->SetCSUAV( 1, m_pRTDownsampledDepth->GetUAV( 1 ) );
		m_pRTDownsampledDepth->SetCSUAV( 2, m_pRTDownsampledDepth->GetUAV( 2 ) );

		int	W = m_Device.DefaultDepthStencil().GetWidth();
		int	H = m_Device.DefaultDepthStencil().GetHeight();
		M.Dispatch( W >> 3, H >> 3, 1 );

	USING_COMPUTE_SHADER_END

	m_Device.RemoveShaderResources( 0, 3, Device::SSF_COMPUTE_SHADER_UAV );	// Remove contention on downsampled depth

	PERF_END_EVENT();


	//////////////////////////////////////////////////////////////////////////
	// 5] Render the cloud box's front & back
	PERF_BEGIN_EVENT( D3DCOLOR( 0xFF800000 ), L"Render Volume Front&Back" );

	m_Device.ClearRenderTarget( *m_pRTRenderZ, float4( 0.0f, -1e4f, 0.0f, 0.0f ) );

	USING_MATERIAL_START( *m_pMatDepthWrite )

		m_Device.SetRenderTarget( *m_pRTRenderZ );

		m_pCB_Object->m.Local2View = m_Cloud2World * m_Camera.GetCB().World2Camera;
		m_pCB_Object->m.View2Proj = m_Camera.GetCB().Camera2Proj;
		m_pCB_Object->m.dUV = m_pRTRenderZ->GetdUV();
		m_pCB_Object->UpdateData();

		PERF_MARKER(  D3DCOLOR( 0x00FF00FF ), L"Render Front Faces" );

	 	m_Device.SetStates( m_Device.m_pRS_CullBack, m_Device.m_pDS_Disabled, m_Device.m_pBS_Disabled_RedOnly );
		m_pPrimBox->Render( M );

		PERF_MARKER(  D3DCOLOR( 0xFFFF00FF ), L"Render Back Faces" );

	 	m_Device.SetStates( m_Device.m_pRS_CullFront, m_Device.m_pDS_Disabled, m_Device.m_pBS_Disabled_GreenOnly );
		m_pPrimBox->Render( M );

	USING_MATERIAL_END

	PERF_END_EVENT();

	m_Device.SetStates( m_Device.m_pRS_CullNone, m_Device.m_pDS_Disabled, m_Device.m_pBS_Disabled );

	//////////////////////////////////////////////////////////////////////////
	// 6] Render the cloud's super low resolution depth pass
	PERF_BEGIN_EVENT( D3DCOLOR( 0xFFC00000 ), L"Render Volume Depth Pass" );

//	m_Device.ClearRenderTarget( *m_pRTVolumeDepth, NjFloat4( 0.0f, -1e4f, 0.0f, 0.0f ) );

	USING_MATERIAL_START( *m_pMatDepthPrePass )

		m_Device.SetRenderTarget( *m_pRTVolumeDepth );

		m_pRTRenderZ->SetPS( 10 );
		m_pRTDownsampledDepth->SetPS( 12 );

		m_pCB_Splat->m.dUV = m_pRTVolumeDepth->GetdUV();
		m_pCB_Splat->UpdateData();

		m_ScreenQuad.Render( M );

	USING_MATERIAL_END

	PERF_END_EVENT();


	//////////////////////////////////////////////////////////////////////////
	// 7] Render the actual volume
	PERF_BEGIN_EVENT( D3DCOLOR( 0xFFFF0000 ), L"Render Volume" );

//	Material*	pMat = m_Camera.GetCB().Camera2World.GetRow(2).y > m_CloudAltitude+m_CloudThickness ? m_ppMatDisplay[1] : m_ppMatDisplay[0];
	Shader*	pMat = m_ppMatDisplay[0];
	USING_MATERIAL_START( *pMat )

		m_Device.ClearRenderTarget( *m_pRTRender, float4( 0.0f, 0.0f, 0.0f, 1.0f ) );

		ID3D11RenderTargetView*	ppViews[] = {
			m_pRTRender->GetRTV( 0, 0, 1 ),
			m_pRTRender->GetRTV( 0, 1, 1 )
		};
		m_Device.SetRenderTargets( m_pRTRender->GetWidth(), m_pRTRender->GetHeight(), 2, ppViews );

//		m_pRTRenderZ->SetPS( 10 );
		m_Device.DefaultDepthStencil().SetPS( 11 );
//		m_pRTDownsampledDepth->SetPS( 12 );
		m_pRTVolumeDepth->SetPS( 13 );


#ifdef SHOW_TERRAIN
#ifndef INCLUDE_TERRAIN_SHADOWING_IN_TFM
	if ( m_bShowTerrain )
		m_pRTTerrainShadow->SetPS( 5, true );	// We need it now for godrays
#endif
#endif

		m_pCB_Splat->m.dUV = m_pRTRender->GetdUV();
		m_pCB_Splat->m.bSampleTerrainShadow = m_bShowTerrain ? 1 : 0;
		m_pCB_Splat->UpdateData();

		m_ScreenQuad.Render( M );

	USING_MATERIAL_END

	PERF_END_EVENT();


#endif//### DRIVER PROBLEM

	//////////////////////////////////////////////////////////////////////////
	// 8] Combine with screen
	PERF_BEGIN_EVENT( D3DCOLOR( 0xFF0000FF ), L"Combine" );

	m_Device.SetRenderTarget( m_Device.DefaultRenderTarget(), NULL );
	m_Device.SetStates( m_Device.m_pRS_CullNone, m_Device.m_pDS_Disabled, m_Device.m_pBS_Disabled );

	USING_MATERIAL_START( *m_pMatCombine )

		m_pCB_Splat->m.dUV = m_Device.DefaultRenderTarget().GetdUV();
		m_pCB_Splat->UpdateData();

		m_pRTRender->SetPS( 10 );	// Cloud rendering, with scattering and extinction
		m_RTHDR.SetPS( 13 );		// Background scene

// DEBUG
//m_Device.DefaultDepthStencil().SetPS( 12 );
//m_pRTRenderZ->SetPS( 11 );
// DEBUG

		m_ScreenQuad.Render( M );

	USING_MATERIAL_END

	PERF_END_EVENT();

	// Remove contention on SRVs for next pass...
	m_Device.RemoveShaderResources( 11 );
	m_Device.RemoveShaderResources( 12 );
	m_Device.RemoveShaderResources( 13 );
}

//#define	SPLAT_TO_BOX
#define USE_NUAJ_SHADOW
#ifdef	SPLAT_TO_BOX
//////////////////////////////////////////////////////////////////////////
// Here, shadow map is simply made to fit the top/bottom of the box
void	EffectVolumetric::ComputeShadowTransform()
{
	// Build basis for directional light
	m_LightDirection.Normalize();

	bool		Above = m_LightDirection.y > 0.0f;

	float3	X = float3::UnitX;
	float3	Y = float3::UnitZ;

	float3	Center = m_Position + float3( 0, (Above ? +1 : -1) * m_Scale.y, 0 );

	float3	SizeLight = float3( 2.0f * m_Scale.x, 2.0f * m_Scale.z, 2.0f * m_Scale.y / fabs(m_LightDirection.y) );

	// Build LIGHT => WORLD transform & inverse
	float4x4	Light2World;
	Light2World.SetRow( 0, X, 0 );
	Light2World.SetRow( 1, Y, 0 );
	Light2World.SetRow( 2, -m_LightDirection, 0 );
	Light2World.SetRow( 3, Center, 1 );

	m_World2Light = Light2World.Inverse();

	// Build projection transforms
	float4x4	Shadow2Light;
	Shadow2Light.SetRow( 0, float4( 0.5f * SizeLight.x, 0, 0, 0 ) );
	Shadow2Light.SetRow( 1, float4( 0, 0.5f * SizeLight.y, 0, 0 ) );
	Shadow2Light.SetRow( 2, float4( 0, 0, 1, 0 ) );
	Shadow2Light.SetRow( 3, float4( 0, 0, 0, 1 ) );

	float4x4	Light2Shadow = Shadow2Light.Inverse();

	m_pCB_Shadow->m.LightDirection = float4( m_LightDirection, 0 );
	m_pCB_Shadow->m.World2Shadow = m_World2Light * Light2Shadow;
	m_pCB_Shadow->m.Shadow2World = Shadow2Light * Light2World;
	m_pCB_Shadow->m.ZMax.Set( SizeLight.z, 1.0f / SizeLight.z );


	// Create an alternate projection matrix that doesn't keep the World Z but instead projects it in [0,1]
	Shadow2Light.SetRow( 2, float4( 0, 0, SizeLight.z, 0 ) );
	Shadow2Light.SetRow( 3, float4( 0, 0, 0, 1 ) );

	m_Light2ShadowNormalized = Shadow2Light.Inverse();

// CHECK
float3	CheckMin( FLOAT32_MAX, FLOAT32_MAX, FLOAT32_MAX ), CheckMax( -FLOAT32_MAX, -FLOAT32_MAX, -FLOAT32_MAX );
float3	CornerLocal, CornerWorld, CornerShadow;
float4x4	World2ShadowNormalized = m_World2Light * m_Light2ShadowNormalized;
for ( int CornerIndex=0; CornerIndex < 8; CornerIndex++ )
{
	CornerLocal.x = 2.0f * (CornerIndex & 1) - 1.0f;
	CornerLocal.y = 2.0f * ((CornerIndex >> 1) & 1) - 1.0f;
	CornerLocal.z = 2.0f * ((CornerIndex >> 2) & 1) - 1.0f;

	CornerWorld = float4( CornerLocal, 1 ) * m_Cloud2World;

// 	CornerShadow = NjFloat4( CornerWorld, 1 ) * m_pCB_Shadow->m.World2Shadow;
	CornerShadow = float4( CornerWorld, 1 ) * World2ShadowNormalized;

	float4	CornerWorldAgain = float4( CornerShadow, 1 ) * m_pCB_Shadow->m.Shadow2World;

	CheckMin = CheckMin.Min( CornerShadow );
	CheckMax = CheckMax.Max( CornerShadow );
}
// CHECK

// CHECK
float3	Test0 = float4( 0.0f, 0.0f, 0.5f * SizeLight.z, 1.0f ) * m_pCB_Shadow->m.Shadow2World;
float3	Test1 = float4( 0.0f, 0.0f, 0.0f, 1.0f ) * m_pCB_Shadow->m.Shadow2World;
float3	DeltaTest0 = Test1 - Test0;
float3	Test2 = float4( 0.0f, 0.0f, SizeLight.z, 1.0f ) * m_pCB_Shadow->m.Shadow2World;
float3	DeltaTest1 = Test2 - Test0;
// CHECK
}

#elif !defined(USE_NUAJ_SHADOW)
//////////////////////////////////////////////////////////////////////////
// Here, shadow is fit to bounding volume as seen from light
void	EffectVolumetric::ComputeShadowTransform()
{
	// Build basis for directional light
	m_LightDirection.Normalize();

	float3	X, Y;
#if 0
	// Use a ground vector
	if ( fabs( m_LightDirection.x - 1.0f ) < 1e-3f )
	{	// Special case
		X = NjFloat3::UnitZ;
		Y = NjFloat3::UnitY;
	}
	else
	{
		X = NjFloat3::UnitX ^ m_LightDirection;
		X.Normalize();
		Y = X ^ m_LightDirection;
	}
#elif 1
	// Force both X,Y vectors to the ground
	float	fFactor = (m_LightDirection.y > 0.0f ? 1.0f : -1.0f);
	X = fFactor * float3::UnitX;
	Y = float3::UnitZ;
#else
	if ( fabs( m_LightDirection.y - 1.0f ) < 1e-3f )
	{	// Special case
		X = float3::UnitX;
		Y = float3::UnitZ;
	}
	else
	{
		X = m_LightDirection ^ float3::UnitY;
		X.Normalize();
		Y = X ^ m_LightDirection;
	}
#endif

	// Project the box's corners to the light plane
	float3	Center = float3::Zero;
	float3	CornerLocal, CornerWorld, CornerLight;
	float3	Min( FLOAT32_MAX, FLOAT32_MAX, FLOAT32_MAX ), Max( -FLOAT32_MAX, -FLOAT32_MAX, -FLOAT32_MAX );
	for ( int CornerIndex=0; CornerIndex < 8; CornerIndex++ )
	{
		CornerLocal.x = 2.0f * (CornerIndex & 1) - 1.0f;
		CornerLocal.y = 2.0f * ((CornerIndex >> 1) & 1) - 1.0f;
		CornerLocal.z = 2.0f * ((CornerIndex >> 2) & 1) - 1.0f;

		CornerWorld = float4( CornerLocal, 1 ) * m_Cloud2World;

		Center = Center + CornerWorld;

		CornerLight.x = CornerWorld | X;
		CornerLight.y = CornerWorld | Y;
		CornerLight.z = CornerWorld | m_LightDirection;

		Min = Min.Min( CornerLight );
		Max = Max.Max( CornerLight );
	}
	Center = Center / 8.0f;

	float3	SizeLight = Max - Min;

	// Offset center to Z start of box
	float	CenterZ = Center | m_LightDirection;	// How much the center is already in light direction?
	Center = Center + (Max.z-CenterZ) * m_LightDirection;

	// Build LIGHT => WORLD transform & inverse
	float4x4	Light2World;
	Light2World.SetRow( 0, X, 0 );
	Light2World.SetRow( 1, Y, 0 );
	Light2World.SetRow( 2, -m_LightDirection, 0 );
	Light2World.SetRow( 3, Center, 1 );

	m_World2Light = Light2World.Inverse();

	// Build projection transforms
	float4x4	Shadow2Light;
	Shadow2Light.SetRow( 0, float4( 0.5f * SizeLight.x, 0, 0, 0 ) );
	Shadow2Light.SetRow( 1, float4( 0, 0.5f * SizeLight.y, 0, 0 ) );
	Shadow2Light.SetRow( 2, float4( 0, 0, 1, 0 ) );
//	Shadow2Light.SetRow( 3, NjFloat4( 0, 0, -0.5f * SizeLight.z, 1 ) );
	Shadow2Light.SetRow( 3, float4( 0, 0, 0, 1 ) );

	float4x4	Light2Shadow = Shadow2Light.Inverse();

	m_pCB_Shadow->m.LightDirection = float4( m_LightDirection, 0 );
	m_pCB_Shadow->m.World2Shadow = m_World2Light * Light2Shadow;
	m_pCB_Shadow->m.Shadow2World = Shadow2Light * Light2World;
	m_pCB_Shadow->m.ZMax.Set( SizeLight.z, 1.0f / SizeLight.z );


	// Create an alternate projection matrix that doesn't keep the World Z but instead projects it in [0,1]
	Shadow2Light.SetRow( 2, float4( 0, 0, SizeLight.z, 0 ) );
//	Shadow2Light.SetRow( 3, NjFloat4( 0, 0, -0.5f * SizeLight.z, 1 ) );
	Shadow2Light.SetRow( 3, float4( 0, 0, 0, 1 ) );

	m_Light2ShadowNormalized = Shadow2Light.Inverse();


// CHECK
float3	CheckMin( FLOAT32_MAX, FLOAT32_MAX, FLOAT32_MAX ), CheckMax( -FLOAT32_MAX, -FLOAT32_MAX, -FLOAT32_MAX );
float3	CornerShadow;
for ( int CornerIndex=0; CornerIndex < 8; CornerIndex++ )
{
	CornerLocal.x = 2.0f * (CornerIndex & 1) - 1.0f;
	CornerLocal.y = 2.0f * ((CornerIndex >> 1) & 1) - 1.0f;
	CornerLocal.z = 2.0f * ((CornerIndex >> 2) & 1) - 1.0f;

	CornerWorld = float4( CornerLocal, 1 ) * m_Cloud2World;

 	CornerShadow = float4( CornerWorld, 1 ) * m_pCB_Shadow->m.World2Shadow;
//	CornerShadow = NjFloat4( CornerWorld, 1 ) * m_World2ShadowNormalized;

	float4	CornerWorldAgain = float4( CornerShadow, 1 ) * m_pCB_Shadow->m.Shadow2World;

	CheckMin = CheckMin.Min( CornerShadow );
	CheckMax = CheckMax.Max( CornerShadow );
}
// CHECK

// CHECK
float3	Test0 = float4( 0.0f, 0.0f, 0.5f * SizeLight.z, 1.0f ) * m_pCB_Shadow->m.Shadow2World;
float3	Test1 = float4( 0.0f, 0.0f, 0.0f, 1.0f ) * m_pCB_Shadow->m.Shadow2World;
float3	DeltaTest0 = Test1 - Test0;
float3	Test2 = float4( 0.0f, 0.0f, SizeLight.z, 1.0f ) * m_pCB_Shadow->m.Shadow2World;
float3	DeltaTest1 = Test2 - Test0;
// CHECK
}

#else

// Projects a world position in kilometers into the shadow plane
float3	EffectVolumetric::Project2ShadowPlane( const float3& _PositionKm, float& _Distance2PlaneKm )
{
// 	NjFloat3	Center2PositionKm = _PositionKm - m_ShadowPlaneCenterKm;
// 	_Distance2PlaneKm = Center2PositionKm | m_ShadowPlaneNormal;
// 	return _PositionKm + _Distance2PlaneKm * m_ShadowPlaneNormal;

	// We're now assuming the plane normal is always Y up and we need to project the position to the plane following m_ShadowPlaneNormal, which is the light's direction
	float	VerticalDistanceToPlane = _PositionKm.y - m_ShadowPlaneCenterKm.y;
	_Distance2PlaneKm = VerticalDistanceToPlane / m_ShadowPlaneNormal.y;
	return _PositionKm - _Distance2PlaneKm * m_ShadowPlaneNormal;
}

// Projects a world position in kilometers into the shadow quad
float2	EffectVolumetric::World2ShadowQuad( const float3& _PositionKm, float& _Distance2PlaneKm )
{
	float3	ProjectedPositionKm = Project2ShadowPlane( _PositionKm, _Distance2PlaneKm );
	float3	Center2ProjPositionKm = ProjectedPositionKm - m_ShadowPlaneCenterKm;
	return float2( Center2ProjPositionKm.Dot( m_ShadowPlaneX ), Center2ProjPositionKm.Dot( m_ShadowPlaneY ) );
}

//////////////////////////////////////////////////////////////////////////
// Update shadow parameters
// The idea is this:
//
//        ..           /
//  top       ..      / Light direction
//  cloud ------ ..  /
//               ---x..
//                     --..
//        -------          -- ..
//        ///////-----         -  .. Tangent plane to the top cloud sphere
//        /////////////--        -
//        //Earth/////////-
//
// 1) We compute the tangent plane to the top cloud sphere by projecting the Earth's center to the cloud sphere's surface following the Sun's direction.
// 2) We project the camera frustum onto that plane
// 3) We compute the bounding quadrilateral to that frustum
// 4) We compute the data necessary to transform a world position into a shadow map position, and the reverse
//
void	EffectVolumetric::ComputeShadowTransform()
{
	static const float3	PLANET_CENTER_KM = float3( 0, -GROUND_RADIUS_KM, 0 );

	float3	LightDirection = m_pCB_Atmosphere->m.LightDirection;

	float		TanFovH = m_Camera.GetCB().Params.x;
	float		TanFovV = m_Camera.GetCB().Params.y;
	float4x4&	Camera2World = m_Camera.GetCB().Camera2World;
	float3	CameraPositionKm = Camera2World.GetRow( 3 );

//###	static const float	SHADOW_FAR_CLIP_DISTANCE = 250.0f;
	static const float	SHADOW_FAR_CLIP_DISTANCE = 70.0f;
	static const float	SHADOW_SCALE = 1.1f;

	//////////////////////////////////////////////////////////////////////////
	// Compute shadow plane tangent space
	float3	ClippedSunDirection = -LightDirection;
// 	if ( ClippedSunDirection.y > 0.0f )
// 		ClippedSunDirection = -ClippedSunDirection;	// We always require a vector facing down

	const float	MIN_THRESHOLD = 1e-2f;
	if ( ClippedSunDirection.y >= 0.0 && ClippedSunDirection.y < MIN_THRESHOLD )
	{	// If the ray is too horizontal, the matrix becomes full of zeroes and is not inversible...
		ClippedSunDirection.y = MIN_THRESHOLD;
		ClippedSunDirection.Normalize();
	}
	else if ( ClippedSunDirection.y < 0.0 && ClippedSunDirection.y > -MIN_THRESHOLD )
	{	// If the ray is too horizontal, the matrix becomes full of zeroes and is not inversible...
		ClippedSunDirection.y = -MIN_THRESHOLD;
		ClippedSunDirection.Normalize();
	}

// TODO: Fix this !
//			 // Clip Sun's direction to avoid grazing angles
//			 if ( ClippedSunDirection.y < (float) Math.Cos( m_shadowSunSetMaxAngle ) )
//			 {
// //				ClippedSunDirection.y = Math.Max( ClippedSunDirection.y, (float) Math.Cos( m_shadowSunSetMaxAngle ) );
// //				ClippedSunDirection /= Math.Max( 1e-3f, 1.0f-ClippedSunDirection.y );		// Project to unit circle
//				 ClippedSunDirection /= (float) Math.Sqrt( ClippedSunDirection.x*ClippedSunDirection.x + ClippedSunDirection.Z*ClippedSunDirection.Z );		// Project to unit circle
//				 ClippedSunDirection.y = 1.0f / (float) Math.Tan( m_shadowSunSetMaxAngle );	// Replace Y by tangent
//				 ClippedSunDirection.Normalize();
//			 }

	// Force both X,Y vectors to the ground, normal is light's direction alwaus pointing toward the ground
	float	fFactor = 1;//(m_LightDirection.y > 0.0f ? 1.0f : -1.0f);
	m_ShadowPlaneX = fFactor * float3::UnitX;
	m_ShadowPlaneY = float3::UnitZ;
 	m_ShadowPlaneNormal = ClippedSunDirection;
//	m_ShadowPlaneCenterKm = NjFloat3( CameraPositionKm.x, 0, CameraPositionKm.z ) + (BOX_BASE + (m_LightDirection.y > 0.0f ? 1 : 0) * MAX( 1e-3f, BOX_HEIGHT )) * NjFloat3::UnitY;	// Center on camera for a start...
	m_ShadowPlaneCenterKm = float4( 0, LightDirection.y > 0 ? 1.0f : -1.0f, 0, 1 ) * m_Cloud2World;

	float	ZSize = m_pCB_Volume->m._CloudAltitudeThickness.y / abs(ClippedSunDirection.y);	// Since we're blocking the XY axes on the plane, Z changes with the light's vertical component
																// Slanter rays will yield longer Z's

	//////////////////////////////////////////////////////////////////////////
	// Build camera frustum
	float3  pCameraFrustumKm[5];
	pCameraFrustumKm[0] = float3::Zero;
	pCameraFrustumKm[1] = SHADOW_FAR_CLIP_DISTANCE * float3( -TanFovH, -TanFovV, 1.0f );
	pCameraFrustumKm[2] = SHADOW_FAR_CLIP_DISTANCE * float3( +TanFovH, -TanFovV, 1.0f );
	pCameraFrustumKm[3] = SHADOW_FAR_CLIP_DISTANCE * float3( +TanFovH, +TanFovV, 1.0f );
	pCameraFrustumKm[4] = SHADOW_FAR_CLIP_DISTANCE * float3( -TanFovH, +TanFovV, 1.0f );

	// Transform into WORLD space
	for ( int i=0; i < 5; i++ )
		pCameraFrustumKm[i] = float4( pCameraFrustumKm[i], 1.0f ) * Camera2World;


	//////////////////////////////////////////////////////////////////////////
	// Build WORLD => LIGHT transform & inverse
	//
	//	^ N                     ^
	//	|                      / -Z, pointing toward the light
	//	|                    /
	// C|                P'/
	//  *-----*----------*-------------> X  <== Cloud plane
	//	 \    |        /
	//	  \   |h     /
	//	   \  |    / d
	//	    \ |  /
	//	     \|/
	//	    P *
	//
	// P is the point we need to transform into light space
	// X is one axis of the 2 axes of the 2D plane
	// -Z is the vector pointing toward the light
	// N=(0,1,0) is the plane normal
	// C is m_ShadowPlaneCenterKm
	// 
	// We pose h = [C-P].N
	// We need to find	d = h / -Zy  which is the distance to the plane by following the light direction (i.e. projection)
	//					d = [P-C].N/Zy
	//
	// We retrieve the projected point's position P' = P - d*Z and we're now on the shadow plane
	// We then retrieve the x component of the 2D plane vector by writing:
	//
	// Plane x	= [P' - C].X
	//			= [P - d*Z - C].X
	//			= [P - [(P-C).N/Zy]*Z - C].X
	//			= [P - [(P-C).N]*Z/Zy - C].X
	//
	// Let's write Z' = Z/Zy , the scaled version of Z that accounts for lengthening due to grazing angles
	// So:
	// Plane x	= [P - [(P-C).N]*Z' - C].X
	//			= [P - [P.N - C.N]*Z' - C].X
	//			= P.X - [P.N - C.N]*(Z'.X) - C.X
	//			= P.X - (P.N)*(Z'.X) + (C.N)*(Z'.X) - C.X
	//			= P.[X - N*(Z'.X)] + C.[N*(Z'.X) - X]
	//
	// Writing X' = X - N*(Z'.X), we finally have:
	//
	//		Plane x = P.X' - C.X'
	//
	// And the same obviously goes for Y.
	// In matrix form, this gives:
	//
	//	|  X'x   Y'x     0   |
	//	|  X'y   Y'y    1/Zy |
	//	|  X'z   Y'z     0   |
	//	| -C.X' -C.Y' -Cy/Zy |
	//
	float3	ScaledZ = m_ShadowPlaneNormal / m_ShadowPlaneNormal.y;

	float	ZdotX = ScaledZ.Dot( m_ShadowPlaneX );
	float3	NewX = m_ShadowPlaneX - float3( 0, ZdotX, 0 );

	float	ZdotY = ScaledZ.Dot( m_ShadowPlaneY );
	float3	NewY = m_ShadowPlaneY - float3( 0, ZdotY, 0 );

	float3	NewZ( 0, 1/m_ShadowPlaneNormal.y, 0 );

	float3	Translation = float3( -m_ShadowPlaneCenterKm.Dot( NewX ), -m_ShadowPlaneCenterKm.Dot( NewY ), -m_ShadowPlaneCenterKm.Dot( NewZ ) );

	m_World2Light.SetRow( 0, float4( NewX.x, NewY.x, NewZ.x, 0 ) );
	m_World2Light.SetRow( 1, float4( NewX.y, NewY.y, NewZ.y, 0 ) );
	m_World2Light.SetRow( 2, float4( NewX.z, NewY.z, NewZ.z, 0 ) );
	m_World2Light.SetRow( 3, float4( Translation.x, Translation.y, Translation.z, 1 ) );

	float4x4	Light2World = m_World2Light.Inverse();

// CHECK: We reproject the frustum and verify the values
float4	pCheckProjected[5];
for ( int i=0; i < 5; i++ )
	pCheckProjected[i] = float4( pCameraFrustumKm[i], 1 ) * m_World2Light;
// CHECK


	//////////////////////////////////////////////////////////////////////////
	// Compute bounding quad
	// Simply use a coarse quad and don't give a fuck (advantage is that it's always axis aligned despite camera orientation)
	float2	QuadMin( +1e6f, +1e6f );
	float2	QuadMax( -1e6f, -1e6f );

	if ( LightDirection.y > 0.0f )
	{	// When the Sun is above the clouds, project the frustum's corners to plane and keep the bounding quad of these points

		// Project frustum to shadow plane
		float		pDistances2Plane[5];
		float2	pFrustumProjKm[5];
		float2	Center = float2::Zero;
		for ( int i=0; i < 5; i++ )
		{
			pFrustumProjKm[i] = World2ShadowQuad( pCameraFrustumKm[i], pDistances2Plane[i] );
			Center = Center + pFrustumProjKm[i];
		}

//		// Re-center about the center
// 		Center = Center / 5;
// 		m_ShadowPlaneCenterKm = m_ShadowPlaneCenterKm + Center.x * m_ShadowPlaneX + Center.y * m_ShadowPlaneY;
// 
// 		// Reproject using new center
// 		NjFloat2	Center2 = NjFloat2::Zero;
// 		for ( int i=0; i < 5; i++ )
// 		{
// 			pFrustumProjKm[i] = World2ShadowQuad( pCameraFrustumKm[i], pDistances2Plane[i] );
// 			Center2 = Center2 + pFrustumProjKm[i];
// 		}
// 		Center2 = Center2 / 5;	// Ensure it's equal to 0!

		for ( int i=0; i < 5; i++ )
		{
			QuadMin = QuadMin.Min( pFrustumProjKm[i] );
			QuadMax = QuadMax.Max( pFrustumProjKm[i] );
		}
	}
	else
	{	// If the Sun is below the clouds then there's no need to account for godrays and we should only focus
		//	on the cloud volume that will actually be lit by the Sun and that we can see.
		// To do this, we unfortunately have to compute the intersection of the camera frustum with the cloud box
		//	and compute our bounding quad from there...
		//
		ComputeFrustumIntersection( pCameraFrustumKm, m_pCB_Volume->m._CloudAltitudeThickness.x, QuadMin, QuadMax );
		ComputeFrustumIntersection( pCameraFrustumKm, m_pCB_Volume->m._CloudAltitudeThickness.x + m_pCB_Volume->m._CloudAltitudeThickness.y, QuadMin, QuadMax );
	}

	// Also compute the cloud box's bounding quad and clip our quad values with it since it's useless to have a quad larger
	//	than what the cloud is covering anyway...
	float2	CloudQuadMin( +1e6f, +1e6f );
	float2	CloudQuadMax( -1e6f, -1e6f );
	for ( int BoxCornerIndex=0; BoxCornerIndex < 8; BoxCornerIndex++ )
	{
		int			Z = BoxCornerIndex >> 2;
		int			Y = (BoxCornerIndex >> 1) & 1;
		int			X = BoxCornerIndex & 1;
		float3	CornerLocal( 2*float(X)-1, 2*float(Y)-1, 2*float(Z)-1 );
		float3	CornerWorld = float4( CornerLocal, 1 ) * m_Cloud2World;

		float		Distance;
		float2	CornerProj = World2ShadowQuad( CornerWorld, Distance );

		CloudQuadMin = CloudQuadMin.Min( CornerProj );
		CloudQuadMax = CloudQuadMax.Max( CornerProj );
	}

	QuadMin = QuadMin.Max( CloudQuadMin );
	QuadMax = QuadMax.Min( CloudQuadMax );

	float2	QuadSize = QuadMax - QuadMin;


	//////////////////////////////////////////////////////////////////////////
	// Determine the rendering viewport based on quad's size

// Statistics of min/max sizes
static float2		s_QuadSizeMin = float2( +1e6f, +1e6f );
static float2		s_QuadSizeMax = float2( -1e6f, -1e6f );
s_QuadSizeMin = s_QuadSizeMin.Min( QuadSize );
s_QuadSizeMax = s_QuadSizeMax.Max( QuadSize );

	// This is the max reported size for a quad when clipping is set at 100km
	// A quad of exactly this size should fit the shadow map's size exactly
	const float	REFERENCE_SHADOW_FAR_CLIP = 100.0f;				// The size was determined using a default clip distance of 100km
	float		MaxWorldSize = 180.0f;							// The 180 factor is arbitrary and comes from experimenting with s_QuadSizeMax and moving the camera around and storing the largest value...
				MaxWorldSize *= SHADOW_FAR_CLIP_DISTANCE / (SHADOW_SCALE * REFERENCE_SHADOW_FAR_CLIP);

// 	ASSERT( QuadSize.x < MaxWorldSize, "Increase max world size!" );
// 	ASSERT( QuadSize.y < MaxWorldSize, "Increase max world size!" );
	float		WorldSizeX = MAX( MaxWorldSize, QuadSize.x );
	float		WorldSizeY = MAX( MaxWorldSize, QuadSize.y );

	float		TexelRatioX = MaxWorldSize / WorldSizeX;		// Scale factor compared to our max accountable world size.
	float		TexelRatioY = MaxWorldSize / WorldSizeY;		// A ratio below 1 means we exceeded the maximum bounds and texels will be skipped.
																// This should be avoided as much as possible as it results in a lack of precision...
//	ASSERT( TexelRatioX >= 1.0f, "Increase quad size max to avoid texel squeeze!" );	// We can't avoid that with slant rays...
//	ASSERT( TexelRatioY >= 1.0f, "Increase quad size max to avoid texel squeeze!" );	// We can't avoid that with slant rays...

	float2	World2TexelScale( (SHADOW_MAP_SIZE-1) / WorldSizeX, (SHADOW_MAP_SIZE-1) / WorldSizeY );	// Actual scale to convert from world to shadow texels

	// Normalized size in texels
	float2	QuadMinTexel = World2TexelScale * QuadMin;
	float2	QuadMaxTexel = World2TexelScale * QuadMax;
	float2	QuadSizeTexel = QuadMaxTexel - QuadMinTexel;

	// Compute viewport size in texels
	int	ViewMinX = int( floorf( QuadMinTexel.x ) );
	int	ViewMinY = int( floorf( QuadMinTexel.y ) );
	int	ViewMaxX = int(  ceilf( QuadMaxTexel.x ) );
	int	ViewMaxY = int(  ceilf( QuadMaxTexel.y ) );

	m_ViewportWidth = ViewMaxX - ViewMinX;
	m_ViewportHeight = ViewMaxY - ViewMinY;

	float2	UVMin( (QuadMinTexel.x - ViewMinX) / SHADOW_MAP_SIZE, (QuadMinTexel.y - ViewMinY) / SHADOW_MAP_SIZE );
	float2	UVMax( (QuadMaxTexel.x - ViewMinX) / SHADOW_MAP_SIZE, (QuadMaxTexel.y - ViewMinY) / SHADOW_MAP_SIZE );
	float2	UVSize = UVMax - UVMin;

	//////////////////////////////////////////////////////////////////////////
	// Build the matrix to transform UVs into Light coordinates
	// We can write QuadPos = QuadMin + (QuadMax-QuadMin) / (UVmax-UVmin) * (UV - UVmin)
	// Unfolding, we obtain:
	//
	//	QuadPos = [QuadMin - (QuadMax-QuadMin) / (UVmax-UVmin) * UVmin] + [(QuadMax-QuadMin) / (UVmax-UVmin)] * UV
	//
	float2	Scale( QuadSize.x / UVSize.x, QuadSize.y / UVSize.y );
	float2	Offset = QuadMin - Scale * UVMin;

	float4x4	UV2Light;
	UV2Light.SetRow( 0, float4( Scale.x, 0, 0, 0 ) );
	UV2Light.SetRow( 1, float4( 0, Scale.y, 0, 0 ) );
	UV2Light.SetRow( 2, float4( 0, 0, 1, 0 ) );
	UV2Light.SetRow( 3, float4( Offset.x, Offset.y, 0, 1 ) );

	float4x4	Light2UV = UV2Light.Inverse();


// CHECK
float4	Test0 = float4( QuadMin.x, QuadMin.y, 0, 1 ) * Light2UV;	// Get back UV min/max
float4	Test1 = float4( QuadMax.x, QuadMax.y, 0, 1 ) * Light2UV;
float4	Test2 = float4( UVMin.x, UVMin.y, 0, 1 ) * UV2Light;			// Get back quad min/max
float4	Test3 = float4( UVMax.x, UVMax.y, 0, 1 ) * UV2Light;
// CHECK

	m_pCB_Shadow->m.World2Shadow = m_World2Light * Light2UV;
	m_pCB_Shadow->m.Shadow2World = UV2Light * Light2World;
	m_pCB_Shadow->m.ZMinMax.Set( 0, ZSize );


// CHECK: We reproject the frustum and verify the UVs...
float4	pCheckUVs[5];
for ( int i=0; i < 5; i++ )
	pCheckUVs[i] = float4( pCameraFrustumKm[i], 1 ) * m_pCB_Shadow->m.World2Shadow;
// CHECK


	// Create an alternate projection matrix that doesn't keep the World Z but instead projects it in [0,1]
// 	UV2Light.SetRow( 2, NjFloat4( 0, 0, ZSize, 0 ) );
// 
// 	m_Light2ShadowNormalized = UV2Light.Inverse();
}

// Computes the intersection of the 5 points camera frustum in WORLD space with a plane and returns the bounding quad of the intersected points
void	EffectVolumetric::ComputeFrustumIntersection( float3 _pCameraFrustumKm[5], float _PlaneHeight, float2& _QuadMin, float2& _QuadMax )
{
	int		pEdges[2*8] = {
		0, 1,
		0, 2,
		0, 3,
		0, 4,
		1, 2,
		2, 3,
		3, 4,
		4, 1
	};
	for ( int EdgeIndex=0; EdgeIndex < 8; EdgeIndex++ )
	{
		float3&	V0 = _pCameraFrustumKm[pEdges[2*EdgeIndex+0]];
		float3&	V1 = _pCameraFrustumKm[pEdges[2*EdgeIndex+1]];
		float3	V = V1 - V0;
		float		VerticalDistance = _PlaneHeight - V0.y;
		float		Distance2Intersection = VerticalDistance / V.y;	// Time until we reach the plane following V
		if ( Distance2Intersection < 0.0f || Distance2Intersection > 1.0f )
			continue;	// No intersection...

		float3	Intersection = V0 + Distance2Intersection * V;

		// Project to shadow plane
		float		Distance2ShadowPlane;
		float2	Projection = World2ShadowQuad( Intersection, Distance2ShadowPlane );

		// Update bounding-quad
		_QuadMin = _QuadMin.Min( Projection );
		_QuadMax = _QuadMax.Max( Projection );
	}
}

#endif


//////////////////////////////////////////////////////////////////////////
// Computes the projection matrix for the terrain shadow map
float4x4	EffectVolumetric::ComputeTerrainShadowTransform()
{
	const float	FRUSTUM_FAR_CLIP = 60.0f;

	float3	LightDirection = m_pCB_Atmosphere->m.LightDirection;

	float3	Z = -LightDirection;
	if ( abs( Z.y ) > 1.0f - 1e-3f )
		Z = float3( 1e-2f, Z.y > 0.0f ? 1.0f : -1.0f, 0 ).Normalize();
	float3	X = float3::UnitY.Cross( Z ).Normalize();
	float3	Y = Z.Cross( X );

	float4x4	Light2World;
	Light2World.SetRow( 0, X, 0 );
	Light2World.SetRow( 1, Y, 0 );
	Light2World.SetRow( 2, Z, 0 );
	Light2World.SetRow( 3, m_Terrain2World.GetRow( 3 ) );

	float4x4	World2Light = Light2World.Inverse();

	// Build frustum points
	float		TanFovH = m_Camera.GetCB().Params.x;
	float		TanFovV = m_Camera.GetCB().Params.y;
	float4x4&	Camera2World = m_Camera.GetCB().Camera2World;

	float3	pFrustumWorld[5] = {
		float3::Zero,
		FRUSTUM_FAR_CLIP * float3( -TanFovH, +TanFovV, 1 ),
		FRUSTUM_FAR_CLIP * float3( -TanFovH, -TanFovV, 1 ),
		FRUSTUM_FAR_CLIP * float3( +TanFovH, -TanFovV, 1 ),
		FRUSTUM_FAR_CLIP * float3( +TanFovH, +TanFovV, 1 ),
	};
	for ( int i=0; i < 5; i++ )
		pFrustumWorld[i] = float4( pFrustumWorld[i], 1 ) * Camera2World;

	// Transform frustum and terrain into light space
	float3	FrustumMin( 1e6f, 1e6f, 1e6f );
	float3	FrustumMax( -1e6f, -1e6f, -1e6f );
	for ( int i=0; i < 5; i++ )
	{
		float3	FrustumLight = float4( pFrustumWorld[i], 1 ) * World2Light;
		FrustumMin = FrustumMin.Min( FrustumLight );
		FrustumMax = FrustumMax.Max( FrustumLight );
	}
	float3	TerrainMin( 1e6f, 1e6f, 1e6f );
	float3	TerrainMax( -1e6f, -1e6f, -1e6f );
	for ( int i=0; i < 8; i++ )
	{
		float	X = 2.0f * (i&1) - 1.0f;
		float	Y = float( (i>>1)&1 );
		float	Z = 2.0f * ((i>>2)&1) - 1.0f;

		float4	TerrainWorld = float4( X, 0, Z, 1 ) * m_Terrain2World;
					TerrainWorld.y = m_pCB_Object->m.TerrainHeight * Y;

		float3	TerrainLight = TerrainWorld * World2Light;
		TerrainMin = TerrainMin.Min( TerrainLight );
		TerrainMax = TerrainMax.Max( TerrainLight );
	}

	// Clip frustum with terrain as it's useless to render parts that aren't even covered by the terrain...
	FrustumMin = FrustumMin.Max( TerrainMin );
	FrustumMax = FrustumMax.Min( TerrainMax );

	float3	Center = 0.5f * (FrustumMin + FrustumMax);
	float3	Scale = 0.5f * (FrustumMax - FrustumMin);
	float4x4	Light2Proj;
	Light2Proj.SetRow( 0, float4( 1.0f / Scale.x, 0, 0, 0 ) );
	Light2Proj.SetRow( 1, float4( 0, 1.0f / Scale.y, 0, 0 ) );
	Light2Proj.SetRow( 2, float4( 0, 0, 0.5f / Scale.z, 0 ) );
	Light2Proj.SetRow( 3, float4( -Center.x / Scale.x, -Center.y / Scale.y, -0.5f * FrustumMin.z / Scale.z, 1 ) );

	float4x4	World2Proj = World2Light * Light2Proj;

// CHECK
float4	FrustumShadow;
for ( int i=0; i < 5; i++ )
{
	FrustumShadow = float4( pFrustumWorld[i], 1 ) * World2Proj;
}
// CHECK

	return World2Proj;
}


//////////////////////////////////////////////////////////////////////////
// Builds a fractal texture compositing several octaves of tiling Perlin noise
// Successive mips don't average previous mips but rather attenuate higher octaves <= WRONG! ^^
#define USE_WIDE_NOISE
#ifdef USE_WIDE_NOISE

// ========================================================================
// Since we're using a very thin slab of volume, vertical precision is not necessary
// The scale of the world=>noise transform is World * 0.05, meaning the noise will tile every 20 world units.
// The cloud slab is 2 world units thick, meaning 10% of its vertical size is used.
// That also means that we can reuse 90% of its volume and convert it into a surface.
//
// The total volume is 128x128x128. The actually used volume is 128*128*13 (13 is 10% of 128).
// Keeping the height at 16 (rounding 13 up to next POT), the available surface is 131072.
//
// This yields a final teture size of 360x360x16
//
Texture3D*	EffectVolumetric::BuildFractalTexture( bool _bBuildFirst )
{
	Noise*	pNoises[FRACTAL_OCTAVES];
	float	NoiseFrequency = 0.0001f;
	float	FrequencyFactor = 2.0f;
	float	AmplitudeFactor = _bBuildFirst ? 0.707f : 0.707f;

	for ( int OctaveIndex=0; OctaveIndex < FRACTAL_OCTAVES; OctaveIndex++ )
	{
		pNoises[OctaveIndex] = new Noise( _bBuildFirst ? 1+OctaveIndex : 37951+OctaveIndex );
		pNoises[OctaveIndex]->SetWrappingParameters( NoiseFrequency, 198746+OctaveIndex );
		NoiseFrequency *= FrequencyFactor;
	}

//static const int TEXTURE_SIZE_XY = 360;	// 280 FPS full res
static const int TEXTURE_SIZE_XY = 180;		// 400 FPS full res
static const int TEXTURE_SIZE_Z = 16;
static const int TEXTURE_MIPS = 5;		// Max mips is the lowest dimension's mip

	int		SizeXY = TEXTURE_SIZE_XY;
	int		SizeZ = TEXTURE_SIZE_Z;

	float	Normalizer = 0.0f;
	float	Amplitude = 1.0f;
	for ( int OctaveIndex=1; OctaveIndex < FRACTAL_OCTAVES; OctaveIndex++ )
	{
		Normalizer += Amplitude;
		Amplitude *= AmplitudeFactor;
	}
	Normalizer = 1.0f / Normalizer;

	float**	ppMips = new float*[TEXTURE_MIPS];

	// Build first mip
	ppMips[0] = new float[SizeXY*SizeXY*SizeZ];

#if 0
	// Build & Save
	NjFloat3	UVW;
	for ( int Z=0; Z < SizeZ; Z++ )
	{
		UVW.z = float( Z ) / SizeXY;	// Here we keep a cubic aspect ratio for voxels so we also divide by the same size as other dimensions: we don't want the noise to quickly loop vertically!
		float*	pSlice = ppMips[0] + SizeXY*SizeXY*Z;
		for ( int Y=0; Y < SizeXY; Y++ )
		{
			UVW.y = float( Y ) / SizeXY;
			float*	pScanline = pSlice + SizeXY * Y;
			for ( int X=0; X < SizeXY; X++ )
			{
				UVW.x = float( X ) / SizeXY;

				float	V = 0.0f;
				float	Amplitude = 1.0f;
				for ( int OctaveIndex=0; OctaveIndex < FRACTAL_OCTAVES; OctaveIndex++ )
				{
					V += Amplitude * pNoises[OctaveIndex]->WrapPerlin( UVW );
					Amplitude *= AmplitudeFactor;
				}
				V *= Normalizer;
				*pScanline++ = V;
			}
		}
	}
	FILE*	pFile = NULL;
	fopen_s( &pFile, _bBuildFirst ? "FractalNoise0.float" : "FractalNoise1.float", "wb" );
	ASSERT( pFile != NULL, "Couldn't write fractal file!" );
	fwrite( ppMips[0], sizeof(float), SizeXY*SizeXY*SizeZ, pFile );
	fclose( pFile );
#else
	// Only load
	FILE*	pFile = NULL;
	fopen_s( &pFile, _bBuildFirst ? "FractalNoise0.float" : "FractalNoise1.float", "rb" );
	ASSERT( pFile != NULL, "Couldn't load fractal file!" );
	fread_s( ppMips[0], SizeXY*SizeXY*SizeZ*sizeof(float), sizeof(float), SizeXY*SizeXY*SizeZ, pFile );
	fclose( pFile );
#endif

	// Build other mips
	for ( int MipIndex=1; MipIndex < TEXTURE_MIPS; MipIndex++ )
	{
		int		SourceSizeXY = SizeXY;
		int		SourceSizeZ = SizeZ;
		SizeXY = MAX( 1, SizeXY >> 1 );
		SizeZ = MAX( 1, SizeZ >> 1 );

		float*	pSource = ppMips[MipIndex-1];
		float*	pTarget = new float[SizeXY*SizeXY*SizeZ];
		ppMips[MipIndex] = pTarget;

		for ( int Z=0; Z < SizeZ; Z++ )
		{
			float*	pSlice0 = pSource + SourceSizeXY*SourceSizeXY*(2*Z);
			float*	pSlice1 = pSource + SourceSizeXY*SourceSizeXY*(2*Z+1);
			float*	pSliceT = pTarget + SizeXY*SizeXY*Z;
			for ( int Y=0; Y < SizeXY; Y++ )
			{
				float*	pScanline00 = pSlice0 + SourceSizeXY * (2*Y);
				float*	pScanline01 = pSlice0 + SourceSizeXY * (2*Y+1);
				float*	pScanline10 = pSlice1 + SourceSizeXY * (2*Y);
				float*	pScanline11 = pSlice1 + SourceSizeXY * (2*Y+1);
				float*	pScanlineT = pSliceT + SizeXY*Y;
				for ( int X=0; X < SizeXY; X++ )
				{
					float	V  = pScanline00[0] + pScanline00[1];	// From slice 0, current line
							V += pScanline01[0] + pScanline01[1];	// From slice 0, next line
							V += pScanline10[0] + pScanline10[1];	// From slice 1, current line
							V += pScanline11[0] + pScanline11[1];	// From slice 1, next line
					V *= 0.125f;

					*pScanlineT++ = V;

					pScanline00 += 2;
					pScanline01 += 2;
					pScanline10 += 2;
					pScanline11 += 2;
				}
			}
		}
	}

#define PACK_R8	// Use R8 instead of R32F
#ifdef PACK_R8

	const float	ScaleMin = -0.15062222f, ScaleMax = 0.16956991f;

	// Convert mips to U8
	U8**	ppMipsU8 = new U8*[TEXTURE_MIPS];

	SizeXY = TEXTURE_SIZE_XY;
	SizeZ = TEXTURE_SIZE_Z;
	for ( int MipIndex=0; MipIndex < TEXTURE_MIPS; MipIndex++ )
	{
		float*	pSource = ppMips[MipIndex];
		U8*		pTarget = new U8[SizeXY*SizeXY*SizeZ];
		ppMipsU8[MipIndex] = pTarget;

		float	Min = +1.0, Max = -1.0f;
		for ( int Z=0; Z < SizeZ; Z++ )
		{
			float*	pSlice = pSource + SizeXY*SizeXY*Z;
			U8*		pSliceT = pTarget + SizeXY*SizeXY*Z;
			for ( int Y=0; Y < SizeXY; Y++ )
			{
				float*	pScanline = pSlice + SizeXY * Y;
				U8*		pScanlineT = pSliceT + SizeXY*Y;
				for ( int X=0; X < SizeXY; X++ )
				{
					float	V = *pScanline++;
							V = (V-ScaleMin)/(ScaleMax-ScaleMin);
					*pScanlineT++ = U8( MIN( 255, int(256 * V) ) );

					Min = MIN( Min, V );
					Max = MAX( Max, V );
				}
			}
		}

		SizeXY = MAX( 1, SizeXY >> 1 );
		SizeZ = MAX( 1, SizeZ >> 1 );
	}

	// Build actual R8 texture
	Texture3D*	pResult = new Texture3D( m_Device, TEXTURE_SIZE_XY, TEXTURE_SIZE_XY, TEXTURE_SIZE_Z, PixelFormatR8::DESCRIPTOR, TEXTURE_MIPS, (void**) ppMipsU8 );

	for ( int MipIndex=0; MipIndex < TEXTURE_MIPS; MipIndex++ )
		delete[] ppMipsU8[MipIndex];
	delete[] ppMipsU8;


#if 1
	// Save as POM format
	Texture3D*	pStagingNoise = new Texture3D( m_Device, TEXTURE_SIZE_XY, TEXTURE_SIZE_XY, TEXTURE_SIZE_Z, PixelFormatR8::DESCRIPTOR, TEXTURE_MIPS, NULL, true );
	pStagingNoise->CopyFrom( *pResult );
	pStagingNoise->Save( "./Noise180x180x16.pom" );
	delete pStagingNoise;
#endif


#else
	// Build actual R32F texture
	Texture3D*	pResult = new Texture3D( m_Device, TEXTURE_SIZE_XY, TEXTURE_SIZE_XY, TEXTURE_SIZE_Z, PixelFormatR32F::DESCRIPTOR, TEXTURE_MIPS, (void**) ppMips );
#endif

	for ( int MipIndex=0; MipIndex < TEXTURE_MIPS; MipIndex++ )
		delete[] ppMips[MipIndex];
	delete[] ppMips;

	for ( int OctaveIndex=0; OctaveIndex < FRACTAL_OCTAVES; OctaveIndex++ )
		delete pNoises[OctaveIndex];

	return pResult;
}

#else
//////////////////////////////////////////////////////////////////////////
// This generator doesn't use a wide texture but a cube texture
// Most vertical space is wasted since we never sample there anyway!

namespace
{
	float	CombineDistances( float _pSqDistances[], int _pCellX[], int _pCellY[], int _pCellZ[], void* _pData )
	{
		return _pSqDistances[0];
	}
}


Texture3D*	EffectVolumetric::BuildFractalTexture( bool _bBuildFirst )
{
	Noise*	pNoises[FRACTAL_OCTAVES];
	float	NoiseFrequency = 0.0001f;
	float	FrequencyFactor = 2.0f;
	float	AmplitudeFactor = _bBuildFirst ? 0.707f : 0.707f;

	for ( int OctaveIndex=0; OctaveIndex < FRACTAL_OCTAVES; OctaveIndex++ )
	{
		pNoises[OctaveIndex] = new Noise( _bBuildFirst ? 1+OctaveIndex : 37951+OctaveIndex );
		pNoises[OctaveIndex]->SetWrappingParameters( NoiseFrequency, 198746+OctaveIndex );

		int	CellsCount = 4 << OctaveIndex;
		pNoises[OctaveIndex]->SetCellularWrappingParameters( CellsCount, CellsCount, CellsCount );
		NoiseFrequency *= FrequencyFactor;
	}

	int		Size = 1 << FRACTAL_TEXTURE_POT;
	int		MipsCount = 1+FRACTAL_TEXTURE_POT;

	float	Normalizer = 0.0f;
	float	Amplitude = 1.0f;
	for ( int OctaveIndex=1; OctaveIndex < FRACTAL_OCTAVES; OctaveIndex++ )
	{
		Normalizer += Amplitude;
		Amplitude *= AmplitudeFactor;
	}
	Normalizer = 1.0f / Normalizer;

	float**	ppMips = new float*[MipsCount];

	// Build first mip
	ppMips[0] = new float[Size*Size*Size];

//#define USE_CELLULAR_NOISE
#if defined(USE_CELLULAR_NOISE)
// ========================================================================
#if 0
	NjFloat3	UVW;
	for ( int Z=0; Z < Size; Z++ )
	{
		UVW.z = float( Z ) / Size;
		float*	pSlice = ppMips[0] + Size*Size*Z;
		for ( int Y=0; Y < Size; Y++ )
		{
			UVW.y = float( Y ) / Size;
			float*	pScanline = pSlice + Size * Y;
			for ( int X=0; X < Size; X++ )
			{
				UVW.x = float( X ) / Size;

				float	V = 0.0f;
				float	Amplitude = 1.0f;
				float	Frequency = 1.0f;
				for ( int OctaveIndex=0; OctaveIndex < FRACTAL_OCTAVES; OctaveIndex++ )
				{
					V += Amplitude * pNoises[OctaveIndex]->Worley( Frequency * UVW, CombineDistances, NULL, true );
					Amplitude *= AmplitudeFactor;
//					Frequency *= FrequencyFactor;
				}
				V *= Normalizer;
				*pScanline++ = V;
			}
		}
	}
	FILE*	pFile = NULL;
	fopen_s( &pFile, "CellularNoise.float", "wb" );
	ASSERT( pFile != NULL, "Couldn't write cellular file!" );
	fwrite( ppMips[0], sizeof(float), Size*Size*Size, pFile );
	fclose( pFile );
#else
	FILE*	pFile = NULL;
	fopen_s( &pFile, "CellularNoise.float", "rb" );
	ASSERT( pFile != NULL, "Couldn't load cellular file!" );
	fread_s( ppMips[0], Size*Size*Size*sizeof(float), sizeof(float), Size*Size*Size, pFile );
	fclose( pFile );
#endif

#else
// ========================================================================
#if 0
	NjFloat3	UVW;
	for ( int Z=0; Z < Size; Z++ )
	{
		UVW.z = float( Z ) / Size;
		float*	pSlice = ppMips[0] + Size*Size*Z;
		for ( int Y=0; Y < Size; Y++ )
		{
			UVW.y = float( Y ) / Size;
			float*	pScanline = pSlice + Size * Y;
			for ( int X=0; X < Size; X++ )
			{
				UVW.x = float( X ) / Size;

				float	V = 0.0f;
				float	Amplitude = 1.0f;
				float	Frequency = 1.0f;
				for ( int OctaveIndex=0; OctaveIndex < FRACTAL_OCTAVES; OctaveIndex++ )
				{
					V += Amplitude * pNoises[OctaveIndex]->WrapPerlin( Frequency * UVW );
					Amplitude *= AmplitudeFactor;
//					Frequency *= FrequencyFactor;
				}
				V *= Normalizer;
				*pScanline++ = V;
			}
		}
	}
	FILE*	pFile = NULL;
	fopen_s( &pFile, _bBuildFirst ? "FractalNoise0.float" : "FractalNoise1.float", "wb" );
	ASSERT( pFile != NULL, "Couldn't write fractal file!" );
	fwrite( ppMips[0], sizeof(float), Size*Size*Size, pFile );
	fclose( pFile );
#else
	FILE*	pFile = NULL;
	fopen_s( &pFile, _bBuildFirst ? "FractalNoise0.float" : "FractalNoise1.float", "rb" );
	ASSERT( pFile != NULL, "Couldn't load fractal file!" );
	fread_s( ppMips[0], Size*Size*Size*sizeof(float), sizeof(float), Size*Size*Size, pFile );
	fclose( pFile );
#endif

#endif

	// Build other mips
	for ( int MipIndex=1; MipIndex < MipsCount; MipIndex++ )
	{
		int		SourceSize = Size;
		Size >>= 1;

		float*	pSource = ppMips[MipIndex-1];
		float*	pTarget = new float[Size*Size*Size];
		ppMips[MipIndex] = pTarget;

		for ( int Z=0; Z < Size; Z++ )
		{
			float*	pSlice0 = pSource + SourceSize*SourceSize*(2*Z);
			float*	pSlice1 = pSource + SourceSize*SourceSize*(2*Z+1);
			float*	pSliceT = pTarget + Size*Size*Z;
			for ( int Y=0; Y < Size; Y++ )
			{
				float*	pScanline00 = pSlice0 + SourceSize * (2*Y);
				float*	pScanline01 = pSlice0 + SourceSize * (2*Y+1);
				float*	pScanline10 = pSlice1 + SourceSize * (2*Y);
				float*	pScanline11 = pSlice1 + SourceSize * (2*Y+1);
				float*	pScanlineT = pSliceT + Size*Y;
				for ( int X=0; X < Size; X++ )
				{
					float	V  = pScanline00[0] + pScanline00[1];	// From slice 0, current line
							V += pScanline01[0] + pScanline01[1];	// From slice 0, next line
							V += pScanline10[0] + pScanline10[1];	// From slice 1, current line
							V += pScanline11[0] + pScanline11[1];	// From slice 1, next line
					V *= 0.125f;

					*pScanlineT++ = V;

					pScanline00 += 2;
					pScanline01 += 2;
					pScanline10 += 2;
					pScanline11 += 2;
				}
			}
		}
	}
	Size = 1 << FRACTAL_TEXTURE_POT;	// Restore size after mip modifications

	// Build actual texture
	Texture3D*	pResult = new Texture3D( m_Device, Size, Size, Size, PixelFormatR32F::DESCRIPTOR, 0, (void**) ppMips );

	for ( int MipIndex=0; MipIndex < MipsCount; MipIndex++ )
		delete[] ppMips[MipIndex];
	delete[] ppMips;

	for ( int OctaveIndex=0; OctaveIndex < FRACTAL_OCTAVES; OctaveIndex++ )
		delete pNoises[OctaveIndex];

	return pResult;
}

#endif

//////////////////////////////////////////////////////////////////////////
// Sky tables precomputation
//
#ifdef BUILD_SKY_TABLES_USING_CS
#include "EffectVolumetricComputeSkyTablesCS.cpp"
#else
#include "EffectVolumetricComputeSkyTablesPS.cpp"
#endif

void	EffectVolumetric::FreeSkyTables()
{
	delete m_ppRTScattering[2];
	delete m_ppRTScattering[1];
	delete m_ppRTScattering[0];
	delete m_ppRTIrradiance[2];
	delete m_ppRTIrradiance[1];
	delete m_ppRTIrradiance[0];
	delete m_ppRTTransmittance[1];
	delete m_ppRTTransmittance[0];
}

//////////////////////////////////////////////////////////////////////////
// This computes the transmittance of Sun light as seen through the atmosphere
// This results in a 2D table with [CosSunDirection, AltitudeKm] as the 2 entries.
// It's done with the CPU because:
//	1] It's fast to compute
//	2] We need to access it from the CPU to compute the Sun's intensity & color for the directional light
//
void	EffectVolumetric::BuildTransmittanceTable( int _Width, int _Height, Texture2D& _StagingTexture ) {

	if ( m_pTableTransmittance != NULL ) {
		delete[] m_pTableTransmittance;
	}

	m_pTableTransmittance = new float3[_Width*_Height];

	float		HRefRayleigh = 8.0f;
	float		HRefMie = 1.2f;

	float		Sigma_s_Mie = 0.004f;	// !!!May cause strong optical depths and very low values if increased!!
	float3	Sigma_t_Mie = (Sigma_s_Mie / 0.9f) * float3::One;	// Should this be a parameter as well?? People might set it to values > 1 and that's physically incorrect...
	float3	Sigma_s_Rayleigh( 0.0058f, 0.0135f, 0.0331f );
//	NjFloat3	Sigma_s_Rayleigh( 0, 0, 0 );
								  
float3	MaxopticalDepth = float3::Zero;
int		MaxOpticalDepthX = -1;
int		MaxOpticalDepthY = -1;
	float2	UV;
	for ( int Y=0; Y < _Height; Y++ ) {
		UV.y = Y / (_Height-1.0f);
		float	AltitudeKm = UV.y*UV.y * ATMOSPHERE_THICKNESS_KM;					// Grow quadratically to have more precision near the ground

#ifdef USE_PRECISE_COS_THETA_MIN
		float	RadiusKm = GROUND_RADIUS_KM + 1e-2f + AltitudeKm;
		float	CosThetaMin = -1e-2f + -sqrtf( 1.0f - GROUND_RADIUS_KM*GROUND_RADIUS_KM / (RadiusKm*RadiusKm) );	// -0.13639737868529368408722196006097 at 60km
#else
		float	CosThetaMin = -0.15f;
#endif

		float3*	scanline = m_pTableTransmittance + _Width * Y;

		for ( int X=0; X < _Width; X++, scanline++ ) {	// CosTheta changes sign at X=0xB8 (UV.x = 71%) ==> 0xB7=-0.00226515974 & 0xB8=+0.00191573682
			UV.x = float(X) / _Width;

			float	t = tan( TRANSMITTANCE_TAN_MAX * UV.x ) / tan(TRANSMITTANCE_TAN_MAX);	// Grow tangentially to have more precision horizontally
// 			float	t = UV.x;									// Grow linearly
// 			float	t = UV.x*UV.x;								// Grow quadratically
//			float	CosTheta = LERP( -0.15f, 1.0f, t );
			float	CosTheta = LERP( CosThetaMin, 1.0f, t );


// const float	CosThetaEps = 8e-3f;
// if ( CosTheta > 0.0f && CosTheta < CosThetaEps )	CosTheta = CosThetaEps;
// if ( CosTheta < 0.0f && CosTheta > -CosThetaEps )	CosTheta = -CosThetaEps;

// if ( CosTheta > 0.0f )
// 	CosTheta = LERP( 0.02f, 1.0f, CosTheta );
// else
// 	CosTheta = LERP( -0.02f, -1.0f, -CosTheta );

// float	RadiusKm = GROUND_RADIUS_KM + 1e-2f + AltitudeKm;
// float	CosThetaGround = -sqrtf( 1.0f - GROUND_RADIUS_KM*GROUND_RADIUS_KM / (RadiusKm*RadiusKm) );	// -0.13639737868529368408722196006097 at 60km
// if ( CosTheta > CosThetaGround )
// 	CosTheta = LERP( CosThetaGround+0.01f, 1.0f, CosTheta / (1.0f-CosThetaGround) );
// else
// 	CosTheta = LERP( CosThetaGround+0.1001f, -1.0f, -CosTheta / (1.0f+CosThetaGround) );

//CosTheta = 1.0f - 0.999f * (1.0f - CosTheta);

			bool		groundHit = false;
			float3	OpticalDepth = Sigma_s_Rayleigh * ComputeOpticalDepth( AltitudeKm, CosTheta, HRefRayleigh, groundHit ) + Sigma_t_Mie * ComputeOpticalDepth( AltitudeKm, CosTheta, HRefMie, groundHit );
// 			if ( groundHit ) {
// 				scanline->Set( 1e-4f, 1e-4f, 1e-4f );	// Special case...
// 				continue;
// 			}

if ( OpticalDepth.z > MaxopticalDepth.z ) {
	MaxopticalDepth = OpticalDepth;
	MaxOpticalDepthX = X;
	MaxOpticalDepthY = Y;
}

//### 			// Here, the blue channel's optical depth's max value has been reported to be 19.6523819
// 			//	but the minimum supported value for Half16 has been measured to be something like 6.10351563e-5 (equivalent to d = -ln(6.10351563e-5) = 9.7040605270200343321767940202312)
// 			// What I'm doing here is patching very long optical depths in the blue channel to remap
// 			//	the [8,19.6523819] interval into [8,9.704061]
// 			//
// 			static const float	MAX_OPTICAL_DEPTH = 19.652382f;
// 			if ( OpticalDepth.z > 8.0f ) {
// 				OpticalDepth.z = 8.0f + (9.70f-8.0f) * SATURATE( (OpticalDepth.z - 8.0f) / (MAX_OPTICAL_DEPTH-8.0f) );
// 			}

//			scanline->Set( expf( -OpticalDepth.x ), expf( -OpticalDepth.y ), expf( -OpticalDepth.z ) );
			*scanline = OpticalDepth;
//			scanline->Set( sqrtf(OpticalDepth.x), sqrtf(OpticalDepth.y), sqrtf(OpticalDepth.z) );

//scanline->Set( 1e-4f+expf( -OpticalDepth.x ), 1e-4f+expf( -OpticalDepth.y ), 1e-4f+expf( -OpticalDepth.z ) );	// Just to avoid any division by 0 in the shader...

// 			scanline->x = 1.0f - idMath::Pow( scanline->x, 1.0f/8 );
// 			scanline->y = 1.0f - idMath::Pow( scanline->y, 1.0f/8 );
// 			scanline->z = 1.0f - idMath::Pow( scanline->z, 1.0f/8 );

// #ifdef _DEBUG
// // CHECK Ensure we never get 0 from a non 0 value
// NjHalf	TestX( scanline->x );
// if ( scanline->x != 0.0f && TestX.raw == 0 ) {
// 	DebugBreak();
// }
// NjHalf	TestY( scanline->y );
// if ( scanline->y != 0.0f && TestY.raw == 0 ) {
// 	DebugBreak();
// }
// NjHalf	TestZ( scanline->z );
// if ( scanline->z != 0.0f && TestZ.raw == 0 ) {
// 	DebugBreak();
// }
// // CHECK
// #endif
		}
	}




#ifdef _DEBUG
const float	WORLD2KM = 0.5f;
const float	CameraAltitudeKm = WORLD2KM * 1.5f;
float3	PositionWorldKm( 0, CameraAltitudeKm, 0 );

//float		ViewAngle = NUAJDEG2RAD(90.0f + 1.0f);	// Slightly downward
//float		ViewAngle = NUAJDEG2RAD(90.0f + 0.5f);	// Slightly downward

float		CameraRadiusKm = GROUND_RADIUS_KM+CameraAltitudeKm;
float		ViewAngle = acosf( -sqrtf( 1-GROUND_RADIUS_KM*GROUND_RADIUS_KM/(CameraRadiusKm*CameraRadiusKm) ) );

float3	View = float3( sinf(ViewAngle), cosf(ViewAngle), 0 );

float		HitDistanceKm = 200.0f;

	HitDistanceKm = MIN( HitDistanceKm, SphereIntersectionExit( PositionWorldKm, View, ATMOSPHERE_THICKNESS_KM ) );	// Limit to the atmosphere

if ( View.y < 0.0f )
	HitDistanceKm = MIN( HitDistanceKm, SphereIntersectionEnter( PositionWorldKm, View, 0.0f ) );					// Limit to the ground

float3	Test0 = GetTransmittance( PositionWorldKm.y, View.y, HitDistanceKm );
#endif




	// Build an actual RGBA16F texture from this table
	{
		D3D11_MAPPED_SUBRESOURCE	LockedResource = _StagingTexture.Map( 0, 0 );
		half4*	pTarget = (half4*) LockedResource.pData;
		for ( int Y=0; Y < _Height; Y++ ) {
			float3*	pScanlineSource = m_pTableTransmittance + _Width*Y;
			half4*	pScanlineTarget = pTarget + _Width*Y;
			for ( int X=0; X < _Width; X++, pScanlineSource++, pScanlineTarget++ ) {

				*pScanlineTarget = float4( *pScanlineSource, 0 );
			}
		}
		_StagingTexture.UnMap( 0, 0 );
	}
}

float	EffectVolumetric::ComputeOpticalDepth( float _AltitudeKm, float _CosTheta, const float _Href, bool& _bGroundHit, int _StepsCount ) const
{
	// Compute distance to atmosphere or ground, whichever comes first
	float4	PositionKm = float4( 0.0f, 1e-2f + _AltitudeKm, 0.0f, 0.0f );
	float3	View = float3( sqrtf( 1.0f - _CosTheta*_CosTheta ), _CosTheta, 0.0f );
	float	TraceDistanceKm = ComputeNearestHit( PositionKm, View, ATMOSPHERE_THICKNESS_KM, _bGroundHit );
	if ( _bGroundHit )
 		return 1e5f;	// Completely opaque due to hit with ground: no light can come this way...
 						// Be careful with large values in 16F!

	float3	EarthCenterKm( 0, -GROUND_RADIUS_KM, 0 );

	float	Result = 0.0;
	float4	StepKm = (TraceDistanceKm / _StepsCount) * float4( View, 1.0 );

	// Integrate until the hit
	float	PreviousAltitudeKm = _AltitudeKm;
	for ( int i=0; i < _StepsCount; i++ )
	{
		PositionKm = PositionKm + StepKm;
		_AltitudeKm = (float3(PositionKm) - EarthCenterKm).Length() - GROUND_RADIUS_KM;
		Result += expf( (PreviousAltitudeKm + _AltitudeKm) * (-0.5f / _Href) );	// Gives the integral of a linear interpolation in altitude
		PreviousAltitudeKm = _AltitudeKm;
	}

	return Result * StepKm.w;
}

float3	EffectVolumetric::GetTransmittance( float _AltitudeKm, float _CosTheta ) const
{
	float	NormalizedAltitude = sqrtf( max( 0.0f, _AltitudeKm ) * (1.0f / ATMOSPHERE_THICKNESS_KM) );

#ifdef USE_PRECISE_COS_THETA_MIN
	float	RadiusKm = GROUND_RADIUS_KM + _AltitudeKm;
	float	CosThetaMin = -sqrt( 1.0f - (GROUND_RADIUS_KM*GROUND_RADIUS_KM) / (RadiusKm*RadiusKm) );
#else
	float	CosThetaMin = -0.15;
#endif
 	float	NormalizedCosTheta = atan( (_CosTheta - CosThetaMin) / (1.0f - CosThetaMin) * tan(TRANSMITTANCE_TAN_MAX) ) / TRANSMITTANCE_TAN_MAX;

	float2	UV( NormalizedCosTheta, NormalizedAltitude );

	float3	Result = SampleTransmittance( UV );
	return Result;
}

float3	EffectVolumetric::GetTransmittance( float _AltitudeKm, float _CosTheta, float _DistanceKm ) const
{
	// P0 = [0, _RadiusKm]
	// V  = [SinTheta, CosTheta]
	//
	float	RadiusKm = GROUND_RADIUS_KM + _AltitudeKm;
	float	RadiusKm2 = sqrt( RadiusKm*RadiusKm + _DistanceKm*_DistanceKm + 2.0f * RadiusKm * _CosTheta * _DistanceKm );	// sqrt[ (P0 + d.V)� ]
	float	CosTheta2 = (RadiusKm * _CosTheta + _DistanceKm) / RadiusKm2;													// dot( P0 + d.V, V ) / RadiusKm2
	float	AltitudeKm2 = RadiusKm2 - GROUND_RADIUS_KM;

// 	if ( _CosTheta * CosTheta2 < 0.0f )
// 		DebugBreak();

	float	CosThetaGround = -sqrtf( 1.0f - (GROUND_RADIUS_KM*GROUND_RADIUS_KM) / (RadiusKm*RadiusKm) );

	float3	T0, T1;
	if ( _CosTheta > CosThetaGround )
	{
		T0 = GetTransmittance( _AltitudeKm, _CosTheta );
		T1 = GetTransmittance( AltitudeKm2, CosTheta2 );
	}
	else
	{
		T0 = GetTransmittance( AltitudeKm2, -CosTheta2 );
		T1 = GetTransmittance( _AltitudeKm, -_CosTheta );
	}

	float3	Result = T0 / T1;

//CHECK No really 16-bits precision computation...
// NjHalf4		T0Half = NjHalf4( NjFloat4( T0, 0 ) );
// NjHalf4		T1Half = NjHalf4( NjFloat4( T1, 0 ) );
// NjFloat4	ResultHalf = NjHalf4( NjFloat4( T0Half ) / NjFloat4( T1Half ) );
//CHECK

	return Result;
}

float3	EffectVolumetric::SampleTransmittance( const float2 _UV ) const {

	float	X = _UV.x * (TRANSMITTANCE_W-1);
	float	Y = _UV.y * TRANSMITTANCE_H;
	int		X0 = int( floorf( X ) );
			X0 = CLAMP( 0, TRANSMITTANCE_W-1, X0 );
	float	x = X - X0;
	int		Y0 = int( floorf( Y ) );
			Y0 = CLAMP( 0, TRANSMITTANCE_H-1, Y0 );
	float	y = Y - Y0;
	int		X1 = MIN( TRANSMITTANCE_W-1, X0+1 );
	int		Y1 = MIN( TRANSMITTANCE_H-1, Y0+1 );

	// Bilerp values
	const float3&	V00 = m_pTableTransmittance[TRANSMITTANCE_W*Y0+X0];
	const float3&	V01 = m_pTableTransmittance[TRANSMITTANCE_W*Y0+X1];
	const float3&	V10 = m_pTableTransmittance[TRANSMITTANCE_W*Y1+X0];
	const float3&	V11 = m_pTableTransmittance[TRANSMITTANCE_W*Y1+X1];
	float3	V0 = V00 + x * (V01 - V00);
	float3	V1 = V10 + x * (V11 - V10);
	float3	V = V0 + y * (V1 - V0);
	return V;
}

////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Planetary Helpers
//
void	EffectVolumetric::ComputeSphericalData( const float3& _PositionKm, float& _AltitudeKm, float3& _Normal ) const {
	float3	EarthCenterKm( 0, -GROUND_RADIUS_KM, 0 );
	float3	Center2Position = _PositionKm - EarthCenterKm;
	float	Radius2PositionKm = Center2Position.Length();
	_AltitudeKm = Radius2PositionKm - GROUND_RADIUS_KM;
	_Normal = Center2Position / Radius2PositionKm;
}

// ====== Intersections ======

// Computes the enter intersection of a ray and a sphere
float	EffectVolumetric::SphereIntersectionEnter( const float3& _PositionKm, const float3& _View, float _SphereAltitudeKm ) const {
	float3	EarthCenterKm( 0, -GROUND_RADIUS_KM, 0 );
	float	R = _SphereAltitudeKm + GROUND_RADIUS_KM;
	float3	D = _PositionKm - EarthCenterKm;
	float	c = D.Dot( D ) - R*R;
	float	b = D.Dot( _View );

	float	Delta = b*b - c;
	if ( Delta < 0.0f )
		return 1e6f;

	Delta = sqrt(Delta);
	float	HitDistance = -b - Delta;
	return  HitDistance;
}

// Computes the exit intersection of a ray and a sphere
// (No check for validity!)
float	EffectVolumetric::SphereIntersectionExit( const float3& _PositionKm, const float3& _View, float _SphereAltitudeKm ) const {
	float3	EarthCenterKm( 0, -GROUND_RADIUS_KM, 0 );
	float	R = _SphereAltitudeKm + GROUND_RADIUS_KM;
	float3	D = _PositionKm - EarthCenterKm;
	float	c = D.Dot( D ) - R*R;
	float	b = D.Dot( _View );

	float	Delta = b*b - c;
	if ( Delta < 0.0f )
		return 1e6f;

	Delta = sqrt(Delta);
	float	HitDistance = -b + Delta;
	return  HitDistance;
}

// Computes both intersections of a ray and a sphere
// Returns INFINITY if no hit is found
void	EffectVolumetric::SphereIntersections( const float3& _PositionKm, const float3& _View, float _SphereAltitudeKm, float2& _Hits ) const
{
	float3	EarthCenterKm( 0, -GROUND_RADIUS_KM, 0 );
	float	R = _SphereAltitudeKm + GROUND_RADIUS_KM;
	float3	D = _PositionKm - EarthCenterKm;
	float	c = D.Dot( D ) - R*R;
	float	b = D.Dot( _View );

	float	Delta = b*b - c;
	if ( Delta < 0.0 ) {
		_Hits.Set( 1e6f, 1e6f );
		return;
	}

	Delta = sqrt(Delta);

	_Hits.Set( -b - Delta, -b + Delta );
}

// Computes the nearest hit between provided sphere and ground sphere
float	EffectVolumetric::ComputeNearestHit( const float3& _PositionKm, const float3& _View, float _SphereAltitudeKm, bool& _IsGround ) const
{
	float2	GroundHits;
	SphereIntersections( _PositionKm, _View, 0.0, GroundHits );
	float	SphereHit = SphereIntersectionExit( _PositionKm, _View, _SphereAltitudeKm );

	_IsGround = false;
	if ( GroundHits.x < 0.0f || SphereHit < GroundHits.x )
		return SphereHit;	// We hit the top of the atmosphere...
	
	// We hit the ground first
	_IsGround = true;
	return GroundHits.x;
}
