#include "../../../GodComplex.h"
#include "EffectScene.h"
#include "MaterialBank.h"

//////////////////////////////////////////////////////////////////////////
// EffectScene
EffectScene::EffectScene( Device& _Device )
	: m_Device					( _Device )
	, m_ObjectsCount			( 0 )
	, m_ppObjects				( NULL )
	, m_LightsCountDirectional	( 0 )
	, m_pLightsDirectional		( NULL )
	, m_LightsCountPoint		( 0 )
	, m_pLightsPoint			( NULL )
	, m_LightsCountSpot			( 0 )
	, m_pLightsSpot			( NULL )
{
	m_pMaterials = new MaterialBank( _Device );
}

EffectScene::~EffectScene()
{
	DestroyLights();
	DestroyObjects();
	delete m_pMaterials;
}

void	EffectScene::Update( float _Time, float _DeltaTime )
{
	for ( int ObjectIndex=0; ObjectIndex < m_ObjectsCount; ObjectIndex++ )
		if ( m_ppObjects[ObjectIndex] != NULL )
			m_ppObjects[ObjectIndex]->Update( _Time, _DeltaTime );
}

void	EffectScene::Render( Shader& _Material, bool _bDepthPass ) const
{
	// Upload our materials
	m_pMaterials->UpdateMaterialsBuffer();

	for ( int ObjectIndex=0; ObjectIndex < m_ObjectsCount; ObjectIndex++ )
		if ( m_ppObjects[ObjectIndex] != NULL )
			m_ppObjects[ObjectIndex]->Render( _Material, _bDepthPass );
}

void	EffectScene::AllocateObjects( int _ObjectsCount )
{
	DestroyObjects();

	m_ObjectsCount = _ObjectsCount;
	m_ppObjects = new Object*[m_ObjectsCount];
	for ( int ObjectIndex=0; ObjectIndex < m_ObjectsCount; ObjectIndex++ )
		m_ppObjects[ObjectIndex] = NULL;
}

void	EffectScene::DestroyObjects()
{
	if ( m_ppObjects == NULL )
		return;

	for ( int ObjectIndex=0; ObjectIndex < m_ObjectsCount; ObjectIndex++ )
		if ( m_ppObjects[ObjectIndex] != NULL )
			delete m_ppObjects[ObjectIndex];

	delete[] m_ppObjects;
	m_ppObjects = NULL;
	m_ObjectsCount = 0;
}

EffectScene::Object&	EffectScene::CreateObjectAt( int _ObjectIndex, const char* _pName )
{
	ASSERT( _ObjectIndex < m_ObjectsCount, "Object index out of range!" );

	// Delete any existing object first
	if ( m_ppObjects[_ObjectIndex] != NULL )
		delete m_ppObjects[_ObjectIndex];

	// Let's create our new object
	Object*	pNewObject = new Object( *this, _pName );
	m_ppObjects[_ObjectIndex] = pNewObject;

	return *pNewObject;
}

EffectScene::Object&		EffectScene::GetObjectAt( int _ObjectIndex )
{
	ASSERT( _ObjectIndex < m_ObjectsCount, "Object index out of range!" );
	return *m_ppObjects[_ObjectIndex];
}

void	EffectScene::AllocateLights( int _DirectionalsCount, int _PointsCount, int _SpotsCount )
{
	DestroyLights();

	m_EnabledLightsCountDirectional = m_LightsCountDirectional = _DirectionalsCount;
	m_pLightsDirectional = new Light[m_LightsCountDirectional];

	m_EnabledLightsCountPoint = m_LightsCountPoint = _PointsCount;
	m_pLightsPoint = new Light[_PointsCount];

	m_EnabledLightsCountSpot = m_LightsCountSpot = _SpotsCount;
	m_pLightsSpot = new Light[_SpotsCount];
}

void	EffectScene::DestroyLights()
{
	if ( m_pLightsDirectional != NULL )
		delete[] m_pLightsDirectional;
	m_pLightsDirectional = NULL;
	m_LightsCountDirectional = 0;

	if ( m_pLightsPoint != NULL )
		delete[] m_pLightsPoint;
	m_pLightsPoint = NULL;
	m_LightsCountPoint = 0;

	if ( m_pLightsSpot != NULL )
		delete[] m_pLightsSpot;
	m_pLightsSpot = NULL;
	m_LightsCountSpot = 0;
}

EffectScene::Light&	EffectScene::GetDirectionalLightAt( int _LightIndex )
{
	ASSERT( _LightIndex < m_LightsCountDirectional, "Directional light index out of range!" );
	return m_pLightsDirectional[_LightIndex];
}
EffectScene::Light&	EffectScene::GetPointLightAt( int _LightIndex )
{
	ASSERT( _LightIndex < m_LightsCountPoint, "Point light index out of range!" );
	return m_pLightsPoint[_LightIndex];
}
EffectScene::Light&	EffectScene::GetSpotLightAt( int _LightIndex )
{
	ASSERT( _LightIndex < m_LightsCountSpot, "Spot light index out of range!" );
	return m_pLightsSpot[_LightIndex];
}

void	EffectScene::SetDirectionalLightEnabled( int _LightIndex, bool _bEnabled )
{
	EffectScene::Light&	L = GetDirectionalLightAt( _LightIndex );
	if ( _bEnabled && !L.m_bEnabled )
		m_EnabledLightsCountDirectional++;
	else if ( !_bEnabled && L.m_bEnabled )
		m_EnabledLightsCountDirectional--;
	L.m_bEnabled = _bEnabled;
}
void	EffectScene::SetPointLightEnabled( int _LightIndex, bool _bEnabled )
{
	EffectScene::Light&	L = GetDirectionalLightAt( _LightIndex );
	if ( _bEnabled && !L.m_bEnabled )
		m_EnabledLightsCountPoint++;
	else if ( !_bEnabled && L.m_bEnabled )
		m_EnabledLightsCountPoint--;
	L.m_bEnabled = _bEnabled;
}
void	EffectScene::SetSpotLightEnabled( int _LightIndex, bool _bEnabled )
{
	EffectScene::Light&	L = GetDirectionalLightAt( _LightIndex );
	if ( _bEnabled && !L.m_bEnabled )
		m_EnabledLightsCountSpot++;
	else if ( !_bEnabled && L.m_bEnabled )
		m_EnabledLightsCountSpot--;
	L.m_bEnabled = _bEnabled;
}

void	EffectScene::SetEnvMap( Texture2D& _EnvMap )
{
	m_pTexEnvMap = &_EnvMap;
}


//////////////////////////////////////////////////////////////////////////
// Object
EffectScene::Object::Object( EffectScene& _Owner, const char* _pName )
	: m_Owner( _Owner )
	, m_pName( _pName )
	, m_PrimitivesCount( 0 )
	, m_ppPrimitives( NULL )
	, m_Position( float3::Zero )
	, m_Rotation( float3::UnitY, 0.0f )
	, m_Scale( float3::One )
	, m_bPRSDirty( true )
{
	m_Rotation = float4::QuatFromAngleAxis( 0.0f, float3::UnitY );
	m_pCB_Object = new CB<CBObject>( m_Owner.m_Device, 10 );
}
EffectScene::Object::~Object()
{
	delete m_pCB_Object;
	DestroyPrimitives();
}

void	EffectScene::Object::SetPRS( const float3& _Position, const float4& _Rotation, const float3& _Scale )
{
	m_Position = _Position;
	m_Rotation = _Rotation;
	m_Scale = _Scale;
	m_bPRSDirty = true;
}

void	EffectScene::Object::Update( float _Time, float _DeltaTime )
{
	if ( !m_bPRSDirty )
		return;	// Already up to date!

	// Rebuild transform from PRS
	m_pCB_Object->m.Local2World.PRS( m_Position, m_Rotation, m_Scale );
	m_bPRSDirty = false;
}

void	EffectScene::Object::Render( Shader& _Material, bool _bDepthPass ) const
{
	// Update our transform
	m_pCB_Object->UpdateData();

	for ( int PrimitiveIndex=0; PrimitiveIndex < m_PrimitivesCount; PrimitiveIndex++ )
		m_ppPrimitives[PrimitiveIndex]->Render( _Material, _bDepthPass );
}

void	EffectScene::Object::AllocatePrimitives( int _PrimitivesCount )
{
	DestroyPrimitives();

	m_PrimitivesCount = _PrimitivesCount;
	m_ppPrimitives = new Primitive*[_PrimitivesCount];
	for ( int PrimitiveIndex=0; PrimitiveIndex < m_PrimitivesCount; PrimitiveIndex++ )
		m_ppPrimitives[PrimitiveIndex] = new Primitive( *this );
}

void	EffectScene::Object::DestroyPrimitives()
{
	if ( m_ppPrimitives == NULL )
		return;

	for ( int PrimitiveIndex=0; PrimitiveIndex < m_PrimitivesCount; PrimitiveIndex++ )
		delete m_ppPrimitives[PrimitiveIndex];

	delete[] m_ppPrimitives;
	m_PrimitivesCount = 0;
	m_ppPrimitives = NULL;
}

EffectScene::Object::Primitive&	EffectScene::Object::GetPrimitiveAt( int _PrimitiveIndex )
{
	ASSERT( _PrimitiveIndex < m_PrimitivesCount, "Primitive index out of range!" );
	return *m_ppPrimitives[_PrimitiveIndex];
}


//////////////////////////////////////////////////////////////////////////
// Primitive
EffectScene::Object::Primitive::Primitive( Object& _Owner ) : m_Owner( _Owner )
{
	m_pCB_Primitive = new CB<CBPrimitive>( m_Owner.m_Owner.m_Device, 11 );
}
EffectScene::Object::Primitive::~Primitive()
{
	delete m_pCB_Primitive;
}

void	EffectScene::Object::Primitive::Render( Shader& _Material, bool _bDepthPass ) const
{
	ASSERT( m_pPrimitive != NULL, "Primitive was not set!" );

	if ( !_bDepthPass )
	{	// Send our primitive infos & textures
		ASSERT( m_pTextures != NULL, "Textures were not set!" );

		m_pCB_Primitive->UpdateData();
		m_pTextures->SetPS( 10 );
	}

	m_pPrimitive->Render( _Material );
}

void	EffectScene::Object::Primitive::SetRenderPrimitive( ::Primitive& _Primitive )
{
	m_pPrimitive = &_Primitive;
}

// void	EffectScene::Object::Primitive::SetMaterial( MaterialParameters& _Material )
// {
// 	m_pCB_Primitive->m.MatIDs[0] = _Material.MatIDs[0];
// 	m_pCB_Primitive->m.MatIDs[1] = _Material.MatIDs[1];
// 	m_pCB_Primitive->m.MatIDs[2] = _Material.MatIDs[2];
// 	m_pCB_Primitive->m.MatIDs[3] = _Material.MatIDs[3];
// 	m_pCB_Primitive->m.Thickness = _Material.Thickness;
// 	m_pCB_Primitive->m.Extinction = _Material.Extinction;	// TODO: Translate extinctions into values depending on thickness
// 	m_pCB_Primitive->m.IOR = _Material.IOR;
// 
// 	ASSERT( _Material.pTextures != NULL, "Invalid textures for primitive material!" );
// 	m_pTextures = _Material.pTextures;
// }

void	EffectScene::Object::Primitive::SetLayerMaterials( Texture2D& _LayeredTextures, int _Mat0, int _Mat1, int _Mat2, int _Mat3 )
{
	const MaterialBank::Material::DynamicParameters*	ppMats[4] =
	{
		&m_Owner.m_Owner.m_pMaterials->GetMaterialAt( _Mat0 ).GetDynamic(),
		&m_Owner.m_Owner.m_pMaterials->GetMaterialAt( _Mat1 ).GetDynamic(),
		&m_Owner.m_Owner.m_pMaterials->GetMaterialAt( _Mat2 ).GetDynamic(),
		&m_Owner.m_Owner.m_pMaterials->GetMaterialAt( _Mat3 ).GetDynamic(),
	};

	m_pTextures = &_LayeredTextures;

	m_pCB_Primitive->m.MatIDs[0] = _Mat0;
	m_pCB_Primitive->m.MatIDs[1] = _Mat1;
	m_pCB_Primitive->m.MatIDs[2] = _Mat2;
	m_pCB_Primitive->m.MatIDs[3] = _Mat3;

	m_pCB_Primitive->m.Thickness.Set( MAX( 1e-6f, ppMats[0]->Thickness ), MAX( 1e-6f, ppMats[1]->Thickness ), MAX( 1e-6f, ppMats[2]->Thickness ), MAX( 1e-6f, ppMats[3]->Thickness ) );
	m_pCB_Primitive->m.IOR.Set( ppMats[1]->IOR, ppMats[2]->IOR, ppMats[3]->IOR );
	m_pCB_Primitive->m.Frosting.Set( ppMats[1]->Frosting, ppMats[2]->Frosting, ppMats[3]->Frosting );
	m_pCB_Primitive->m.NoDiffuse.Set( ppMats[0]->NoDiffuse, ppMats[1]->NoDiffuse, ppMats[2]->NoDiffuse, ppMats[3]->NoDiffuse );

	// Extinctions are given as [0,1] numbers from totally transparent to completely opaque
	// We need to convert them into actual extinction values to be used in the classical exp( -Sigma_t * Distance(millimeters) ) formula
	// We simply assume the opacity of the layer below should be a very low value for extinction=1 when the ray of light travels the layer's whole thickness:
	const float	LOW_OPACITY_VALUE = 1e-3;
	float3	TargetValueAtThickness(
		logf( LERP( LOW_OPACITY_VALUE, 1.0f, ppMats[1]->Opacity ) ) / m_pCB_Primitive->m.Thickness.y,
		logf( LERP( LOW_OPACITY_VALUE, 1.0f, ppMats[2]->Opacity ) ) / m_pCB_Primitive->m.Thickness.z,
		logf( LERP( LOW_OPACITY_VALUE, 1.0f, ppMats[3]->Opacity ) ) / m_pCB_Primitive->m.Thickness.w
		);

	m_pCB_Primitive->m.Extinction = TargetValueAtThickness;
}


//////////////////////////////////////////////////////////////////////////
// Light
EffectScene::Light::Light() : m_bEnabled( true )
{
}
void	EffectScene::Light::SetDirectional( const float3& _Irradiance, const float3& _Position, const float3& _Direction, float _RadiusHotSpot, float _RadiusFalloff, float _Length )
{
	m_Radiance = _Irradiance;
	m_Position = _Position;
	m_Direction = _Direction;
	m_Direction.Normalize();
	m_Data.m_RadiusHotSpot = _RadiusHotSpot;
	m_Data.m_RadiusFalloff = _RadiusFalloff;
	m_Data.m_Length = _Length;
}

void	EffectScene::Light::SetPoint( const float3& _Radiance, const float3& _Position, float _Radius )
{
	m_Radiance = _Radiance;
	m_Position = _Position;
	m_Data.m_Radius = _Radius;
}

void	EffectScene::Light::SetSpot( const float3& _Radiance, const float3& _Position, const float3& _Direction, float _AngleHotSpot, float _AngleFalloff, float _Length )
{
	m_Radiance = _Radiance;
	m_Position = _Position;
	m_Direction = _Direction;
	m_Direction.Normalize();
	m_Data.m_AngleHotSpot = _AngleHotSpot;
	m_Data.m_AngleFalloff = _AngleFalloff;
	m_Data.m_Length = _Length;
}
