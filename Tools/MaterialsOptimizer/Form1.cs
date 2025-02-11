﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using Microsoft.Win32;
using System.Diagnostics;
using RendererManaged;

//////////////////////////////////////////////////////////////////////////
//
//	AXIOMS
//	------
//	Diffuse =>	Texture alpha can contain gloss iff !$isMasking && !$isAlpha
//				In any case, the FetchDiffuseAlbedo should always replace alpha as a regular alpha channel when exiting the function...
//
//	Masks	=>	Masks cannot be merged if they don't have the same UV tiling/bias
//				Layer 0 mask is only used to apply coloring to layer 0 diffuse albedo
//
//////////////////////////////////////////////////////////////////////////
//
//	OPTIONS
//	-------
//
//		$alphaTest, $isMasking		=> Diffuse  with alpha, nothing to do for those materials: SKIP!
//		$extraLayer					=> 0, 1, 2 allows up to 3 layers
//
//	Layer 0
//		$use_Layer0_ColorConstant	=> Multiply diffuse by constant color
//		$Layer0_MaskMode			=> 0 = Vertex color, 1 = Map, 2 = Map * Vertex Color[channel]
//
//	Layer 1
//		$Layer1_diffuseReuseLayer	=> Re-use diffuse albedo from layer0, certainly with difference scaling
//		$use_Layer1_ColorConstant	=> Multiply diffuse by constant color
//		
//	Layer 2
//		$Layer2_diffuseReuseLayer	=> 0 = disabled, 1 = re-use layer 0 diffuse, 2 = re-use layer 1 diffuse
//		$use_Layer2_ColorConstant	=> Multiply diffuse by constant color
//		
//		
//		
//	renderparm Layer0_UVset					{ option 0 range [0, 1] }	// 0 first uvset // 1 second uvset
// 	renderparm Layer0_Mask_UVset			{ option 0 range [0, 1] }	// 0 first uvset // 1 second uvset
// 	renderparm Layer0_VtxColorMaskChannel	{ option 3 range [0, 3] }	// 0 red // 1 green // 2 blue // 3 alpha
// 	renderparm Layer0_MaskMode				{ option 0 range [0, 2] }	// 0 vertex color channel // 1 mask texture // 2 mask texture * vertex color channel
// 	renderparm Layer0_InvertMask			{ option 0 range [0, 1] }
// 	renderparm use_Layer0_ColorConstant		{ option 0 range [0, 1] }
// 	renderParm Layer0_ScaleBias				{ Uniform	float4	{ 1.0, 1.0, 0.0, 0.0 } range [{ -20.0, -20.0, 0.0, 0.0 }, { 20.0, 20.0, 1.0, 1.0 }] }
// 	renderParm Layer0_MaskScaleBias			{ Uniform	float4	{ 1.0, 1.0, 0.0, 0.0 } range [{ -20.0, -20.0, 0.0, 0.0 }, { 20.0, 20.0, 1.0, 1.0 }] }
// 	renderParm Layer0_Maskmap				{ Texture2D	float	_default }
// 	renderparm Layer0_ColorConstant			{ Uniform float4 { 1.0, 1.0, 1.0, 1.0 } range [{0,0,0,0}, {1,1,1,1}] }
// 	renderParm Layer0_RescaleValues			{ Uniform float2 { 0.4, 0.6 } range [{0,0}, {1,1}] }
// 
// 	renderparm Layer1_UVset					{ option 0 range [0, 1] }	// 0 first uvset // 1 second uvset
// 	renderparm Layer1_Mask_UVset			{ option 0 range [0, 1] }	// 0 first uvset // 1 second uvset
// 	renderparm Layer1_VtxColorMaskChannel	{ option 3 range [0, 3] }	// 0 red // 1 green // 2 blue // 3 alpha
// 	renderparm Layer1_MaskMode				{ option 0 range [0, 2] }	// 0 vertex color channel // 1 mask texture // 2 mask texture * vertex color channel
// 	renderparm Layer1_InvertMask			{ option 0 range [0, 1] }
// 	renderparm use_Layer1_ColorConstant		{ option 0 range [0, 1] }
// 	renderParm Layer1_diffuseReuseLayer		{ option 0 range [0, 1] }	// 0 = No reuse // 1 = re-use layer 0
// 	renderParm Layer1_bumpReuseLayer		{ option 0 range [0, 1] }	// 0 = No reuse // 1 = re-use layer 0
// 	renderParm Layer1_specularReuseLayer	{ option 0 range [0, 1] }	// 0 = No reuse // 1 = re-use layer 0
// 	renderParm Layer1_glossReuseLayer		{ option 0 range [0, 1] }	// 0 = No reuse // 1 = re-use layer 0
// 	renderParm Layer1_metallicReuseLayer	{ option 0 range [0, 1] }	// 0 = No reuse // 1 = re-use layer 0
// 	renderParm Layer1_maskReuseLayer		{ option 0 range [0, 1] }	// 0 = No reuse // 1 = re-use layer 0

// 	renderParm Layer1_ScaleBias				{ Uniform	float4	{ 1.0, 1.0, 0.0, 0.0 } range [{ -20.0, -20.0, 0.0, 0.0 }, { 20.0, 20.0, 1.0, 1.0 }] }
// 	renderParm Layer1_MaskScaleBias			{ Uniform	float4	{ 1.0, 1.0, 0.0, 0.0 } range [{ -20.0, -20.0, 0.0, 0.0 }, { 20.0, 20.0, 1.0, 1.0 }] }
// 	renderParm Layer1_Maskmap				{ Texture2D	float	_default }
// 	renderParm Layer1_diffuseMap			{ Texture2D	float4	_default }
// 	renderParm Layer1_bumpMap				{ Texture2D	float4	ipr_constantColor(0.5,0.5,0,0) }
// 	renderParm Layer1_specularMap			{ Texture2D	float4	ipr_constantColor(0,0,0,0) }
// 	renderParm Layer1_glossMap				{ Texture2D	float	ipr_constantColor(0,0,0,0) }
// 	renderParm Layer1_metallicMap			{ Texture2D	float	ipr_constantColor(0,0,0,0) }
// 	renderparm Layer1_ColorConstant			{ Uniform float4 { 1.0, 1.0, 1.0, 1.0 } range [{0,0,0,0}, {1,1,1,1}] }
// 	renderParm Layer1_RescaleValues			{ Uniform float2 { 0.4, 0.6 } range [{0,0}, {1,1}] }
// 
// 	renderparm Layer2_UVset					{ option 0 range [0, 1] }	// 0 first uvset // 1 second uvset
// 	renderparm Layer2_Mask_UVset			{ option 0 range [0, 1] }	// 0 first uvset // 1 second uvset
// 	renderparm Layer2_VtxColorMaskChannel	{ option 3 range [0, 3] }	// 0 red // 1 green // 2 blue // 3 alpha
// 	renderparm Layer2_MaskMode				{ option 0 range [0, 2] }	// 0 vertex color channel // 1 mask texture // 2 mask texture * vertex color channel
// 	renderparm Layer2_InvertMask			{ option 0 range [0, 1] }
// 	renderparm use_Layer2_ColorConstant		{ option 0 range [0, 1] }
// 	renderParm Layer2_diffuseReuseLayer		{ option 0 range [0, 2] }	// 0 = No re-use // 1 = re-user layer 0 // 2 = re-use layer 1
// 	renderParm Layer2_bumpReuseLayer		{ option 0 range [0, 2] }	// 0 = No re-use // 1 = re-user layer 0 // 2 = re-use layer 1
// 	renderParm Layer2_specularReuseLayer	{ option 0 range [0, 2] }	// 0 = No re-use // 1 = re-user layer 0 // 2 = re-use layer 1
// 	renderParm Layer2_glossReuseLayer		{ option 0 range [0, 2] }	// 0 = No re-use // 1 = re-user layer 0 // 2 = re-use layer 1
// 	renderParm Layer2_metallicReuseLayer	{ option 0 range [0, 2] }	// 0 = No re-use // 1 = re-user layer 0 // 2 = re-use layer 1
// 	renderParm Layer2_maskReuseLayer		{ option 0 range [0, 2] }	// 0 = No re-use // 1 = re-user layer 0 // 2 = re-use layer 1

// 	renderParm Layer2_ScaleBias				{ Uniform	float4	{ 1.0, 1.0, 0.0, 0.0 } range [{ -20.0, -20.0, 0.0, 0.0 }, { 20.0, 20.0, 1.0, 1.0 }] }
// 	renderParm Layer2_MaskScaleBias			{ Uniform	float4	{ 1.0, 1.0, 0.0, 0.0 } range [{ -20.0, -20.0, 0.0, 0.0 }, { 20.0, 20.0, 1.0, 1.0 }] }
// 	renderParm Layer2_Maskmap				{ Texture2D	float	_default }
// 	renderParm Layer2_diffuseMap			{ Texture2D	float4	_default }
// 	renderParm Layer2_bumpMap				{ Texture2D	float4	ipr_constantColor(0.5,0.5,0,0) }
// 	renderParm Layer2_specularMap			{ Texture2D	float4	ipr_constantColor(0,0,0,0) }
// 	renderParm Layer2_glossMap				{ Texture2D	float	ipr_constantColor(0,0,0,0) }
// 	renderParm Layer2_metallicMap			{ Texture2D	float	ipr_constantColor(0,0,0,0) }
// 	renderparm Layer2_ColorConstant			{ Uniform float4 { 1.0, 1.0, 1.0, 1.0 } range [{0,0,0,0}, {1,1,1,1}] }
// 	renderParm Layer2_RescaleValues			{ Uniform float2 { 0.4, 0.6 } range [{0,0}, {1,1}] }
//
//
//////////////////////////////////////////////////////////////////////////
//	Layering & Masking
//	------------------
// 
// 	void initLayeredTexcoord() {
// 		#if $Layer0_UVset == 0
// 			float2 layer0_uv = m_texCoords.xy;
// 		#else
// 			float2 layer0_uv = m_texCoords.zw;
// 		#endif
//
//		m_layerTexCoords[0] = layer0_uv*$Layer0_ScaleBias.xy + $Layer0_ScaleBias.zw;
//		m_layerTexCoords[1] = m_layerTexCoords[0];
//		m_layerTexCoords[2] = m_layerTexCoords[0];
// 		#if( $extraLayer > 0 )
// 			#if $Layer1_UVset == 0
// 				float2 layer1_uv = m_texCoords.xy;
// 			#else
// 				float2 layer1_uv = m_texCoords.zw;
// 			#endif
// 			m_layerTexCoords[1] = layer1_uv*$Layer1_ScaleBias.xy + $Layer1_ScaleBias.zw;
// 			#if( $extraLayer > 1 )
// 				#if $Layer2_UVset == 0
// 					float2 layer2_uv = m_texCoords.xy;
// 				#else
// 					float2 layer2_uv = m_texCoords.zw;
// 				#endif
// 				m_layerTexCoords[2] = layer2_uv*$Layer2_ScaleBias.xy + $Layer2_ScaleBias.zw;
// 			#endif
// 		#endif
//
// 		#if $Layer0_Mask_UVset == 0
// 			float2 layer0_mask_uv = m_texCoords.xy;
// 		#else
// 			float2 layer0_mask_uv = m_texCoords.zw;
// 		#endif
//
// 		m_maskTexCoords[0] = layer0_mask_uv*$Layer0_MaskScaleBias.xy + $Layer0_MaskScaleBias.zw;
// 		m_maskTexCoords[1] = m_maskTexCoords[0];
// 		m_maskTexCoords[2] = m_maskTexCoords[0];
// 		#if( $extraLayer > 0 )
// 			#if $Layer1_Mask_UVset == 0
// 				float2 layer1_mask_uv = m_texCoords.xy;
// 			#else
// 				float2 layer1_mask_uv = m_texCoords.zw;
// 			#endif
// 			m_maskTexCoords[1] = layer1_mask_uv*$Layer1_MaskScaleBias.xy + $Layer1_MaskScaleBias.zw;
// 			#if( $extraLayer > 1 )
// 				#if $Layer2_Mask_UVset == 0
// 					float2 layer2_mask_uv = m_texCoords.xy;
// 				#else
// 					float2 layer2_mask_uv = m_texCoords.zw;
// 				#endif
// 				m_maskTexCoords[2] = layer2_mask_uv*$Layer2_MaskScaleBias.xy + $Layer2_MaskScaleBias.zw;
// 			#endif
// 		#endif
// 	}
//
// 	void HQ_FetchMasks( inout arkPixelContext_t _pixelCtx ) {
// 		float layer0_mask = 0.0;
// 		#if $use_Layer0_ColorConstant
// 			#if $Layer0_MaskMode == 0		
// 				_pixelCtx.m_masks[0] = _pixelCtx.m_vertexColor[$Layer0_VtxColorMaskChannel];
// 			#elif $Layer0_MaskMode == 1
// 				layer0_mask = $Layer0_Maskmap.Sample($anisotropicSampler, _pixelCtx.m_maskTexCoords[0]);
// 				_pixelCtx.m_masks[0] = layer0_mask;
// 			#else
// 				layer0_mask = $Layer0_Maskmap.Sample($anisotropicSampler, _pixelCtx.m_maskTexCoords[0]).r;
// 				float L0_M1 = layer0_mask;
// 				float L0_M2 = _pixelCtx.m_vertexColor[$Layer0_VtxColorMaskChannel];
// 				float L0_M = L0_M1 * L0_M2;
// 				_pixelCtx.m_masks[0] = LinearStep( $Layer0_RescaleValues.x, $Layer0_RescaleValues.y, L0_M );
// 			#endif
// 		#else
// 			_pixelCtx.m_masks[0] = 0;
// 		#endif
// 
// 		#if $Layer0_InvertMask
// 			_pixelCtx.m_masks[0] = 1-_pixelCtx.m_masks[0];
// 		#endif
// 			
// 		#if( $extraLayer > 0 )
// 			float layer1_mask = 0.0;
// 
// 			#if $Layer1_MaskMode == 0		
// 				_pixelCtx.m_masks[1] = _pixelCtx.m_vertexColor[$Layer1_VtxColorMaskChannel];
// 			#elif $Layer1_MaskMode == 1
// 				#if $Layer1_maskReuseLayer
// 					layer1_mask = layer0_mask;
// 				#else
// 					layer1_mask = $Layer1_Maskmap.Sample($anisotropicSampler, _pixelCtx.m_maskTexCoords[1]).r;
// 				#endif
// 				_pixelCtx.m_masks[1] = layer1_mask;
// 			#else
// 				#if $Layer1_maskReuseLayer
// 					layer1_mask = layer0_mask;
// 				#else
// 					layer1_mask = $Layer1_Maskmap.Sample($anisotropicSampler, _pixelCtx.m_maskTexCoords[1]).r;
// 				#endif
// 				float L1_M1 = layer1_mask;
// 				float L1_M2 = _pixelCtx.m_vertexColor[$Layer1_VtxColorMaskChannel];
// 				float L1_M = L1_M1 * L1_M2;
// 				_pixelCtx.m_masks[1] = LinearStep( $Layer1_RescaleValues.x, $Layer1_RescaleValues.y, L1_M );
// 			#endif
// 			#if $Layer1_InvertMask
// 				_pixelCtx.m_masks[1] = 1-_pixelCtx.m_masks[1];
// 			#endif
// 
// 			#if( $extraLayer > 1 )
// 				float layer2_mask = 0.0;
// 				
// 				#if $Layer2_MaskMode == 0		
// 					_pixelCtx.m_masks[2] = _pixelCtx.m_vertexColor[$Layer2_VtxColorMaskChannel];
// 				#elif $Layer2_MaskMode == 1
// 					#if $Layer2_maskReuseLayer == 0
// 						layer2_mask = $Layer2_Maskmap.Sample($anisotropicSampler, _pixelCtx.m_maskTexCoords[2]).r;
// 					#elif $Layer2_maskReuseLayer == 1
// 						layer2_mask = layer0_mask;
// 					#else
// 						layer2_mask = layer1_mask;
// 					#endif
// 					_pixelCtx.m_masks[2] = layer2_mask;
// 				#else
// 					#if $Layer2_maskReuseLayer == 0
// 						layer2_mask = $Layer2_Maskmap.Sample($anisotropicSampler, _pixelCtx.m_maskTexCoords[2]).r;
// 					#elif $Layer2_maskReuseLayer == 1
// 						layer2_mask = layer0_mask;
// 					#else
// 						layer2_mask = layer1_mask;
// 					#endif
// 					float L2_M1 = layer2_mask;
// 					float L2_M2 = _pixelCtx.m_vertexColor[$Layer2_VtxColorMaskChannel];
// 					float L2_M = L2_M1 * L2_M2;
// 					_pixelCtx.m_masks[2] = LinearStep( $Layer2_RescaleValues.x, $Layer2_RescaleValues.y, L2_M );
// 				#endif
// 				#if $Layer2_InvertMask
// 					_pixelCtx.m_masks[2] = 1-_pixelCtx.m_masks[2];
// 				#endif
// 					
// 			#endif
// 		#endif
// 	}
// 
//
//	void HQ_FetchDiffuseAlbedo( inout arkPixelContext_t _pixelCtx ) {
// 		// ==== Sample diffuse albedo ====
// 		#if $alphaTest
// 			float4 Layer0_DiffuseTexture = $diffusemap.Sample( $anisotropicSampler, _pixelCtx.m_layerTexCoords[0] );
// 		#else
// 			float4 Layer0_DiffuseTexture = $diffusemap.Sample( $anisotropicSamplerHQ, _pixelCtx.m_layerTexCoords[0] );
// 		#endif
// 		_pixelCtx.m_diffuseAlbedo = Layer0_DiffuseTexture;
// 						
// 		#if $alphaTest && !$(tool/unlit)
// 			clip( _pixelCtx.m_diffuseAlbedo.w - $DefaultAlphaTest );
// 		#endif
// 
// 		#if $use_Layer0_ColorConstant
// 			float4 layer0_ColorConstant = lerp( float4(1,1,1,1), $Layer0_ColorConstant, _pixelCtx.m_masks[0] );
// 			_pixelCtx.m_diffuseAlbedo *= layer0_ColorConstant;
// 		#endif
// 			
// 		#if( $extraLayer > 0 )
// 			#if( $Layer1_diffuseReuseLayer )
// 				float4 Layer1_DiffuseTexture = Layer0_DiffuseTexture;
// 			#else
// 				float4 Layer1_DiffuseTexture = $Layer1_diffuseMap.Sample( $anisotropicSampler, _pixelCtx.m_layerTexCoords[1] );
// 			#endif
// 			float4 Layer1_Diffuse = Layer1_DiffuseTexture;
// 			#if $use_Layer1_ColorConstant
// 				Layer1_Diffuse *= $Layer1_ColorConstant;
// 			#endif
// 				
// 			_pixelCtx.m_diffuseAlbedo = lerp( _pixelCtx.m_diffuseAlbedo, Layer1_Diffuse, _pixelCtx.m_masks[1] );
// 
// 			#if( $extraLayer > 1 )
// 				#if( $Layer2_diffuseReuseLayer == 2 )
// 					float4 Layer2_Diffuse = Layer1_DiffuseTexture;
// 				#elif( $Layer2_diffuseReuseLayer == 1 )
// 					float4 Layer2_Diffuse = Layer0_DiffuseTexture;
// 				#else
// 					float4 Layer2_Diffuse = $Layer2_diffuseMap.Sample( $anisotropicSampler, _pixelCtx.m_layerTexCoords[2] );
// 				#endif
// 
// 				#if $use_Layer2_ColorConstant
// 					Layer2_Diffuse *= $Layer2_ColorConstant;
// 				#endif
// 
// 				_pixelCtx.m_diffuseAlbedo = lerp( _pixelCtx.m_diffuseAlbedo, Layer2_Diffuse, _pixelCtx.m_masks[2] );
// 			#endif
// 		#endif
// 
// 		#if $VtxColorMultiply || ($useParticleColorInstancing && !$isShadow && !$isZPrePass)
// 			_pixelCtx.m_diffuseAlbedo *= _pixelCtx.m_vertexColor;
// 		#endif
// 	}
//
//
//////////////////////////////////////////////////////////////////////////
//
namespace MaterialsOptimizer
{
	public partial class Form1 : Form {

		#region NESTED TYPES

		class Error {
			public FileInfo		m_fileName;
			public Exception	m_error;

			public override string ToString() {
				return "ERROR! " + m_fileName.FullName + " > " + FullMessage( m_error );
			}

			public Error( FileInfo _fileName, Exception _error ) {
				m_fileName = _fileName;
				m_error = _error;
			}
			public Error( BinaryReader R ) {
				Read( R );
			}

			public void		Write( BinaryWriter W ) {
				W.Write( m_fileName.FullName );
				W.Write( FullMessage( m_error ) );
			}

			public void		Read( BinaryReader R ) {
				m_fileName = new FileInfo( R.ReadString() );
				m_error = new Exception( R.ReadString() );
			}

			public string	FullMessage( Exception _e ) {
				return _e.Message + (_e.InnerException != null ? "\r\n" + FullMessage( _e.InnerException ) : "");
			}
		}

		#endregion

		private RegistryKey			m_AppKey;
		private string				m_ApplicationPath;



		// Materials database
		private List< Material >		m_sourceMaterials = new List< Material >();
		private List< Material >		m_optimizedMaterials = new List< Material >();

		private List< DiffuseGlossTexture >							m_diffuseGlossTextures = new List< DiffuseGlossTexture >();
		private Dictionary< TextureFileInfo, DiffuseGlossTexture >	m_diffuse2DiffuseGlossTexture = new Dictionary< TextureFileInfo, DiffuseGlossTexture >();

		private List< Error >			m_materialErrors = new List< Error >();
		private FileInfo				m_sourceMaterialsDatabaseFileName = new FileInfo( "sourceMaterials.database" );
		private FileInfo				m_optimizedMaterialsDatabaseFileName = new FileInfo( "optimizedMaterials.database" );
		private FileInfo				m_diffuseGlossTextureDatabaseFileName = new FileInfo( "diffuseGlossTextures.database" );

		private int						m_materialsSortColumn = 0;
		private int						m_materialsSortOrder = 1;

		// Textures database
		private List< TextureFileInfo >	m_textures = new List< TextureFileInfo >();
		private List< Error >			m_textureErrors = new List< Error >();
		private Dictionary< string, TextureFileInfo >	m_textureFileName2Texture = new Dictionary< string, TextureFileInfo >();
		private FileInfo				m_texturesDatabaseFileName = new FileInfo( "textures.database" );
		private int						m_texturesSortColumn = 0;
		private int						m_texturesSortOrder = 1;

		public Form1() {
			InitializeComponent();

 			m_AppKey = Registry.CurrentUser.CreateSubKey( @"Software\Arkane\MaterialsOptimizer" );
			m_ApplicationPath = Path.GetDirectoryName( Application.ExecutablePath );

			textBoxMaterialsBasePath.Text = GetRegKey( "MaterialsBasePath", textBoxMaterialsBasePath.Text );
			textBoxTexturesBasePath.Text = GetRegKey( "TexturesBasePath", textBoxTexturesBasePath.Text );

			Material.Layer.Texture.ms_TexturesBasePath = new DirectoryInfo( textBoxTexturesBasePath.Text );

			// Reload source materials database
			if ( m_sourceMaterialsDatabaseFileName.Exists ) {
				LoadMaterialsDatabase( m_sourceMaterialsDatabaseFileName, m_sourceMaterials );
				buttonReExport.Enabled = m_sourceMaterials.Count > 0;
			}
			// Reload optimized materials database
			if ( m_optimizedMaterialsDatabaseFileName.Exists ) {
				LoadMaterialsDatabase( m_optimizedMaterialsDatabaseFileName, m_optimizedMaterials );
				buttonParseReExportedMaterials.Enabled = m_optimizedMaterials.Count > 0;
				buttonIntegratePerforce.Enabled = m_optimizedMaterials.Count > 0;
				buttonCreateTestMap.Enabled = m_optimizedMaterials.Count > 0;
			}
			// Reload textures database
			if ( m_texturesDatabaseFileName.Exists )
				LoadTexturesDatabase( m_texturesDatabaseFileName );

			// Reload (diffuse+gloss) textures database
			// !! Needs to be done AFTER texture database loading since we need to resolve texture file infos !!
			if ( m_diffuseGlossTextureDatabaseFileName.Exists )
				LoadDiffuseGlossTexturesDatabase( m_diffuseGlossTextureDatabaseFileName );

			// Analyse both materials databases that will rebuild list views
			AnalyzeMaterialsDatabase( m_sourceMaterials, false );
			if ( AnalyzeMaterialsDatabase( m_optimizedMaterials, false ) )
				radioButtonViewOptimizedMaterials.Checked = true;
			else
				RebuildMaterialsListView();

			// Rebuild texture list views
			RebuildTexturesListView();
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if ( this.WindowState != FormWindowState.Minimized )
				SetRegKey( "FormState", ((int) this.WindowState).ToString() );
			if ( this.WindowState != FormWindowState.Maximized ) {
				SetRegKey( "LocationX", this.Location.X.ToString() );
				SetRegKey( "LocationY", this.Location.Y.ToString() );
				SetRegKey( "Width", this.Width.ToString() );
				SetRegKey( "Height", this.Height.ToString() );
			}
			SetRegKey( "SplitterDistance", this.splitContainer1.SplitterDistance.ToString() );

			base.OnFormClosing(e);
		}

		protected override void OnLoad(EventArgs e) {
			base.OnLoad(e);

			// Restore location and UI state
			this.WindowState = (FormWindowState) GetRegKeyInt( "FormState", (int) FormWindowState.Normal );
			if ( this.WindowState == FormWindowState.Minimized )
				this.WindowState = FormWindowState.Normal;
			this.DesktopBounds = new System.Drawing.Rectangle(
				new Point( GetRegKeyInt( "LocationX", this.Location.X ), GetRegKeyInt( "LocationY", this.Location.Y ) ),
				new Size( GetRegKeyInt( "Width", this.Width ), GetRegKeyInt( "Height", this.Height ) )
			);
			this.splitContainer1.SplitterDistance = GetRegKeyInt( "SplitterDistance", this.splitContainer1.SplitterDistance );
		}

		#region Helpers

		private string	GetRegKey( string _Key, string _Default )
		{
			string	Result = m_AppKey.GetValue( _Key ) as string;
			return Result != null ? Result : _Default;
		}
		private void	SetRegKey( string _Key, string _Value )
		{
			m_AppKey.SetValue( _Key, _Value );
		}

		private float	GetRegKeyFloat( string _Key, float _Default )
		{
			string	Value = GetRegKey( _Key, _Default.ToString() );
			float	Result;
			float.TryParse( Value, out Result );
			return Result;
		}

		private int		GetRegKeyInt( string _Key, float _Default )
		{
			string	Value = GetRegKey( _Key, _Default.ToString() );
			int		Result;
			int.TryParse( Value, out Result );
			return Result;
		}

		private DialogResult	MessageBox( string _Text )
		{
			return MessageBox( _Text, MessageBoxButtons.OK );
		}
		private DialogResult	MessageBox( string _Text, MessageBoxButtons _Buttons )
		{
			return MessageBox( _Text, _Buttons, MessageBoxIcon.Information );
		}
		private DialogResult	MessageBox( string _Text, MessageBoxIcon _Icon )
		{
			return MessageBox( _Text, MessageBoxButtons.OK, _Icon );
		}
		private DialogResult	MessageBox( string _Text, MessageBoxButtons _Buttons, MessageBoxIcon _Icon )
		{
			return System.Windows.Forms.MessageBox.Show( this, _Text, "Materials Optimizer", _Buttons, _Icon );
		}

		void	Log( string _text ) {
			textBoxLog.AppendText( _text );
			tabControlInfo.SelectedTab = tabPageLog;
		}
		void	LogLine( string _text ) {
			Log( _text + "\r\n" );
		}

		#endregion

		#region Materials Parsing

		void	RecurseParseMaterials( DirectoryInfo _directory, List< Material > _materials, ProgressBar _progress ) {

			_progress.Visible = true;

			_materials.Clear();
			m_materialErrors.Clear();

			FileInfo[]	materialFileNames = _directory.GetFiles( "*.m2", SearchOption.AllDirectories );
			int			materialIndex = 0;
			foreach ( FileInfo materialFileName in materialFileNames ) {

				_progress.Value = (++materialIndex * _progress.Maximum) / materialFileNames.Length;

				try {
					ParseMaterialFile( materialFileName, _materials );
				} catch ( Exception _e ) {
					Error	Err = new Error( materialFileName, _e );
					m_materialErrors.Add( Err );
					LogLine( Err.ToString() );
				}
			}

			_progress.Visible = false;
		}

		void	ParseMaterialFile( FileInfo _fileName, List< Material > _materials ) {
			string	fileContent = null;
			using ( StreamReader R = _fileName.OpenText() )
				fileContent = R.ReadToEnd();

			Parser	P = new Parser( fileContent );
			while ( P.OK ) {
				string	token = P.ReadString();
				if ( token == null )
					return;
				if ( token.StartsWith( "//" ) ) {
					P.ReadToEOL();
					continue;
				}
				if ( token.StartsWith( "/*" ) ) {
					P.SkipComment();
					continue;
				}

				switch ( token.ToLower() ) {
					case "material":
						string		materialName = P.ReadString();
						if ( materialName.EndsWith( "{" ) ) {
							materialName = materialName.Substring( 0, materialName.Length-1 );
							P.m_Index--;
						}

						P.SkipSpaces();
						if ( P[0] == '/' && P[1] == '*' ) {
							P.SkipComment();	// YES! Someone did do that!
						}

						string		materialContent;
						try {
							materialContent = P.ReadBlock();
						} catch ( Exception _e ) {
							throw new Exception( "Failed parsing material content block for material \"" + materialName + "\"! Check strange comment markers and matching closing brackets", _e );
						}
						try {
							Material	M = new Material( _fileName, materialName, materialContent );
							_materials.Add( M );
						} catch ( Exception _e ) {
							throw new Exception( "Failed parsing material!", _e );
						}
						break;
				}
			}
		}

		bool	AnalyzeMaterialsDatabase( List< Material > _materials, bool _yell ) {
			if ( _materials.Count == 0 ) {
				if ( _yell )
					MessageBox( "Can't analyze materials database since there are no materials available!\r\nTry and parse materials to enable analysis...", MessageBoxButtons.OK );
				return false;
			}
			if ( m_textures.Count == 0 ) {
				if ( _yell )
					MessageBox( "Can't analyze materials database since there are no textures available!\r\nTry and collect textures to enable analysis...", MessageBoxButtons.OK );
				return false;
			}

			// Clean up textures' ref counts
			foreach ( TextureFileInfo TFI in m_textures ) {
				TFI.m_refCount = 0;
			}

			// Analyze each material
			foreach ( Material M in _materials ) {

				// Ref count textures
				ResolveTextureFileInfo( M.m_height );
				foreach ( Material.Layer L in M.m_layers ) {
					ResolveTextureFileInfo( L.m_diffuse );
					ResolveTextureFileInfo( L.m_normal );
					ResolveTextureFileInfo( L.m_gloss );
					ResolveTextureFileInfo( L.m_metal );
					ResolveTextureFileInfo( L.m_specular );
					ResolveTextureFileInfo( L.m_mask );
					ResolveTextureFileInfo( L.m_AO );
					ResolveTextureFileInfo( L.m_translucency );
					ResolveTextureFileInfo( L.m_emissive );
				}

				//////////////////////////////////////////////////////////////////////////
				// Check errors and optimizations
				M.m_errors = null;
				M.ClearErrorLevel();
				M.m_warnings = null;

				int			layersCount = Math.Min( 1+M.m_options.m_extraLayers, M.m_layers.Count );

				string	T = "\t";

				//////////////////////////////////////////////////////////////////////////
				// Check general errors

					// Check no physical material
				if ( M.m_physicsMaterial == null && M.m_programs.m_type != Material.Programs.KNOWN_TYPES.VISTA ) {
					M.m_errors += T + "• Physics material is not setup!\n";
					M.RaiseErrorLevel( Material.ERROR_LEVEL.DIRTY );
				}

					// Check layers count is consistent
				if ( M.m_layers.Count != 1+M.m_options.m_extraLayers )
					M.m_warnings += T + "• Options specify extraLayer=" + M.m_options.m_extraLayers + " but found parameters implying " + M.m_layers.Count + " layers! Either adjust extra layers count or remove surnumerary layer parameters... (this doesn't have an impact on performance anyway)\n";

					// Check default-valued gloss/metal parms => means the material is not correctly initialized by the user
				if ( M.m_options.m_hasGloss && Math.Abs( M.m_glossMinMax.x - 0.0f ) < 1e-3f && Math.Abs( M.m_glossMinMax.y - 0.5f ) < 1e-3f ) {
					M.m_errors += T + "• Gloss min/max are the default values! Material is not initialized!\n";
					M.RaiseErrorLevel( Material.ERROR_LEVEL.DANGEROUS );
				}
				if ( M.m_options.m_hasMetal && Math.Abs( M.m_metallicMinMax.x - 0.0f ) < 1e-3f && Math.Abs( M.m_metallicMinMax.y - 0.5f ) < 1e-3f ) {
					M.m_errors += T + "• Metal min/max are the default values! Material is not initialized!\n";
					M.RaiseErrorLevel( Material.ERROR_LEVEL.DANGEROUS );
				}

				if ( M.m_options.m_hasGloss && Math.Abs( M.m_glossMinMax.x - M.m_glossMinMax.y ) < 1e-3f ) {
					M.m_errors += T + "• Gloss min/max are set to an empty range whereas the \"use gloss map\" option is set! This will have no effect! Options and textures should be removed...\n";
					M.RaiseErrorLevel( Material.ERROR_LEVEL.DANGEROUS );
				}
				if ( M.m_options.m_hasMetal && Math.Abs( M.m_metallicMinMax.x - M.m_metallicMinMax.y ) < 1e-3f ) {
					M.m_errors += T + "• Metal min/max are set to an empty range whereas the \"use metal option\" is set! This will have no effect! Options and textures should be removed...\n";
					M.RaiseErrorLevel( Material.ERROR_LEVEL.DANGEROUS );
				}

				//////////////////////////////////////////////////////////////////////////
				// Check shader-specific errors
				if ( M.m_forbiddenParms.Count > 0 ) {
					// User is using forbidden parms!
					M.m_warnings += T + "• Material is using forbidden parms:\n";
					foreach ( string forbiddenParm in M.m_forbiddenParms )
						M.m_warnings += T + "	" + forbiddenParm + "\n";
				}

				if ( M.m_lightMap != null && M.m_programs.m_type != Material.Programs.KNOWN_TYPES.VISTA ) {
					// Shader is using a light map but is not a vista shader!
					M.m_errors += T + "• Shader is using a light map but is not a VISTA shader!\n";
					M.RaiseErrorLevel( Material.ERROR_LEVEL.DANGEROUS );
				}

				if ( M.m_isUsingVegetationParms && M.m_programs.m_type != Material.Programs.KNOWN_TYPES.VEGETATION ) {
					M.m_warnings += T + "• Shader is using vegetation-related parameters but is not a VEGETATION shader!\n";
				}
				if ( M.m_isUsingCloudParms && M.m_programs.m_type != Material.Programs.KNOWN_TYPES.CLOUDS ) {
					M.m_warnings += T + "• Shader is using cloud-related parameters but is not a CLOUD shader!\n";
				}
				if ( M.m_isUsingWaterParms && M.m_programs.m_type != Material.Programs.KNOWN_TYPES.WATER && M.m_programs.m_type != Material.Programs.KNOWN_TYPES.OCEAN ) {
					M.m_warnings += T + "• Shader is using water-related parameters but is not a WATER or OCEAN shader!\n";
				}


				//////////////////////////////////////////////////////////////////////////
				// Check layer-specific errors
				for ( int layerIndex=0; layerIndex < layersCount; layerIndex++ ) {
					Material.Layer	L = M.m_layers[layerIndex];
					L.m_errors = null;
					L.ClearErrorLevel();
					L.m_warnings = null;
				}
				for ( int layerIndex=0; layerIndex < layersCount; layerIndex++ ) {
					Material.Layer	L = M.m_layers[layerIndex];

					if ( L.m_mask != null && L.m_mask.m_textureFileInfo != null && L.m_mask.m_textureFileInfo.m_usage == TextureFileInfo.USAGE.MASK_BAD_SUFFIX ) {
// 						L.m_errors += T + "• Mask texture uses the bad suffix \"_mask\" instead of the correct \"_m\" suffix: a heavy 4-channels BC7 texture will be created instead of a regular single channel texture!\n";
// 						L.RaiseErrorLevel( Material.ERROR_LEVEL.DANGEROUS );
						L.m_warnings += T + "• Mask texture uses the bad suffix \"_mask\" instead of the correct \"_m\" suffix: an automatic conversion from sRGB to linear will occur and you may lose some precision!\n";
					}

					// Check textures exist
					L.CheckTexture( L.m_diffuse, true, L.m_diffuseReUse, T, "diffuse", 4 );
					L.CheckTexture( L.m_normal, M.m_options.m_hasNormal, L.m_normalReUse, T, "normal", 2 );
					L.CheckTexture( L.m_gloss, M.m_options.m_hasGloss, L.m_glossReUse, T, "gloss", 1 );
					L.CheckTexture( L.m_metal, M.m_options.m_hasMetal, L.m_metalReUse, T, "metal", 1 );
					L.CheckTexture( L.m_specular, M.m_options.m_hasSpecular, L.m_specularReUse, T, "specular", 4 );
					L.CheckTexture( L.m_mask, L.m_maskingMode != Material.Layer.MASKING_MODE.VERTEX_COLOR, L.m_maskReUse, T, "mask", 1 );

					if ( L.m_index == 0 ) {
						// These textures only exist for the first layer
						L.CheckTexture( L.m_AO, M.m_options.m_hasOcclusionMap, Material.Layer.REUSE_MODE.DONT_REUSE, T, "AO", 1 );
						L.CheckTexture( L.m_translucency, M.m_options.m_translucencyEnabled && !M.m_options.m_translucencyUseVertexColor, Material.Layer.REUSE_MODE.DONT_REUSE, T, "translucency", 1 );
						L.CheckTexture( L.m_emissive, M.m_options.m_hasEmissive, Material.Layer.REUSE_MODE.DONT_REUSE, T, "emissive", 4 );
					}
				}

				//////////////////////////////////////////////////////////////////////////
				// Check inter-layer errors
				for ( int topLayerIndex=1; topLayerIndex < layersCount; topLayerIndex++ ) {
					Material.Layer	Ltop = M.m_layers[topLayerIndex];
					for ( int bottomLayerIndex=0; bottomLayerIndex < topLayerIndex; bottomLayerIndex++ ) {
						Material.Layer	Lbottom = M.m_layers[bottomLayerIndex];

						Ltop.CompareTextures( Ltop.m_diffuse, Ltop.m_diffuseReUse, Lbottom.m_diffuse, Lbottom.m_diffuseReUse, Lbottom, T, "diffuse texture (layer " + topLayerIndex + ")", "diffuse texture (layer " + bottomLayerIndex + ")" );
						if ( M.m_options.m_hasNormal )
							Ltop.CompareTextures( Ltop.m_normal, Ltop.m_normalReUse, Lbottom.m_normal, Lbottom.m_normalReUse, Lbottom, T, "normal texture (layer " + topLayerIndex + ")", "normal texture (layer " + bottomLayerIndex + ")" );
						if ( M.m_options.m_hasGloss )
							Ltop.CompareTextures( Ltop.m_gloss, Ltop.m_glossReUse, Lbottom.m_gloss, Lbottom.m_glossReUse, Lbottom, T, "gloss texture (layer " + topLayerIndex + ")", "gloss texture (layer " + bottomLayerIndex + ")" );
						if ( M.m_options.m_hasMetal )
							Ltop.CompareTextures( Ltop.m_metal, Ltop.m_metalReUse, Lbottom.m_metal, Lbottom.m_metalReUse, Lbottom, T, "metal texture (layer " + topLayerIndex + ")", "metal texture (layer " + bottomLayerIndex + ")" );
						if ( M.m_options.m_hasSpecular )
							Ltop.CompareTextures( Ltop.m_specular, Ltop.m_specularReUse, Lbottom.m_specular, Lbottom.m_specularReUse, Lbottom, T, "specular texture (layer " + topLayerIndex + ")", "specular texture (layer " + bottomLayerIndex + ")" );
// 						if ( M.m_options.m_hasEmissive )
// 							Ltop.CompareTextures( Ltop.m_emissive, Ltop.m_emissiveReUse, Lbottom.m_emissive, Lbottom.m_emissiveReUse, Lbottom, T, "emissive texture (layer " + topLayerIndex + ")", "emissive texture (layer " + bottomLayerIndex + ")" );
					}
				}

				// Report standard error levels unless specified otherwise
				if ( M.ErrorLevel_MaterialOnly == Material.ERROR_LEVEL.NONE && M.HasErrors )
					M.RaiseErrorLevel( Material.ERROR_LEVEL.STANDARD );
				foreach ( Material.Layer L in M.m_layers )
					if ( L.ErrorLevel == Material.ERROR_LEVEL.NONE && L.HasErrors )
						L.RaiseErrorLevel( Material.ERROR_LEVEL.STANDARD );
			}

			return true;
		}

		void	ResolveTextureFileInfo( Material.Layer.Texture _texture ) {
			if ( _texture == null )
				return;

			_texture.m_textureFileInfo = ResolveTextureFileInfo( _texture.m_fileName );
			if ( _texture.m_textureFileInfo != null )
				_texture.m_textureFileInfo.m_refCount++;
		}

		void	ResolveTextureFileInfo( DiffuseGlossTexture _DTG ) {
			_DTG.m_diffuse = ResolveTextureFileInfo( _DTG.m_diffuseFileName );
			if ( _DTG.m_diffuse == null )
				throw new Exception( "  !Failed to retrieve optimizable diffuse texture \"" + _DTG.m_diffuseFileName.FullName + "\"" );

			if ( _DTG.m_glossFileName != null ) {
				_DTG.m_gloss = ResolveTextureFileInfo( _DTG.m_glossFileName );
				if ( _DTG.m_gloss == null )
					throw new Exception( "  !Failed to retrieve optimizable gloss texture \"" + _DTG.m_glossFileName.FullName + "\"" );
			} else {
				_DTG.m_gloss = null;
			}

			_DTG.m_optimizedDiffuseGloss = _DTG.m_optimizedDiffuseGlossFileName != null ? ResolveTextureFileInfo( _DTG.m_optimizedDiffuseGlossFileName ) : null;
		}

		TextureFileInfo	ResolveTextureFileInfo( FileInfo _textureFileName ) {
			if ( _textureFileName == null )
				return null;

			string	normalizedFileName = TextureFileInfo.NormalizeFileName( _textureFileName.FullName );
			if ( !m_textureFileName2Texture.ContainsKey( normalizedFileName ) )
				return null;

			// Found it!
			TextureFileInfo	TFI = m_textureFileName2Texture[normalizedFileName];
			return TFI;
		}

		void	SaveMaterialsDatabase( FileInfo _fileName, List< Material > _materials ) {
			using ( FileStream S = _fileName.Create() )
				using ( BinaryWriter W = new BinaryWriter( S ) ) {
					W.Write( _materials.Count );
					foreach ( Material M in _materials )
						M.Write( W );

					W.Write( m_materialErrors.Count );
					foreach ( Error E in m_materialErrors )
						E.Write( W );
				}
		}

		void	LoadMaterialsDatabase( FileInfo _fileName, List< Material > _materials ) {
			_materials.Clear();
			m_materialErrors.Clear();

			try {
				using ( FileStream S = _fileName.OpenRead() )
					using ( BinaryReader R = new BinaryReader( S ) ) {
						int	materialsCount = R.ReadInt32();
						for ( int materialIndex=0; materialIndex < materialsCount; materialIndex++ )
							_materials.Add( new Material( R ) );

						int	errorsCount = R.ReadInt32();
						for ( int errorIndex=0; errorIndex < errorsCount; errorIndex++ )
							m_materialErrors.Add( new Error( R ) );
					}
			} catch ( Exception ) {
				MessageBox( "An error occurred while reloading material database!\nThe format must have changed since it was last saved...\n\nPlease reparse material.", MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		void	RebuildMaterialsListView() {
			RebuildMaterialsListView( radioButtonViewSourceMaterials.Checked ? m_sourceMaterials : m_optimizedMaterials );
		}
		void	RebuildMaterialsListView( string _searchFor ) {
			RebuildMaterialsListView( radioButtonViewSourceMaterials.Checked ? m_sourceMaterials : m_optimizedMaterials, _searchFor );
		}
		void	RebuildMaterialsListView( List< Material > _materials ) {
			RebuildMaterialsListView( _materials, null );
		}
		void	RebuildMaterialsListView( List< Material > _materials, string _searchFor ) {

			if ( _materials == m_sourceMaterials )
				radioButtonViewSourceMaterials.Checked = true;
			else if ( _materials == m_optimizedMaterials )
				radioButtonViewOptimizedMaterials.Checked = true;


			// Filter materials
			List< Material >	filteredMaterials = new List< Material >();
			int					errorMaterialsCount = 0;
			if ( _searchFor == null ) {
				bool	skipDefault = !checkBoxShowArkDefault.Checked ^ checkBoxInvertMaterialFilters.Checked;
				bool	skipSkin = !checkBoxShowSkin.Checked ^ checkBoxInvertMaterialFilters.Checked;
				bool	skipEye = !checkBoxShowEye.Checked ^ checkBoxInvertMaterialFilters.Checked;
				bool	skipHair = !checkBoxShowHair.Checked ^ checkBoxInvertMaterialFilters.Checked;
				bool	skipVegetation = !checkBoxShowVegetation.Checked ^ checkBoxInvertMaterialFilters.Checked;
				bool	skipAlpha = !checkBoxShowAlpha.Checked && !checkBoxInvertMaterialFilters.Checked;
				bool	skipNonAlpha = !checkBoxShowAlpha.Checked && checkBoxInvertMaterialFilters.Checked;
				bool	skipVista = !checkBoxShowVista.Checked ^ checkBoxInvertMaterialFilters.Checked;
				bool	skipOther = !checkBoxShowOtherMaterialTypes.Checked ^ checkBoxInvertMaterialFilters.Checked;
				int		layersCountMin = integerTrackbarControlLayerMin.Value;
				int		layersCountMax = integerTrackbarControlLayerMax.Value;

				int		minErrorLevel = integerTrackbarControlErrorLevel.Value;
				bool	showOnlyWarningMats = checkBoxShowWarningMaterials.Checked;
				bool	showOnlyMissingPhysicsMats = checkBoxShowMissingPhysics.Checked;
				bool	showOnlyOptimizedMats = checkBoxShowOptimizableMaterials.Checked;

				foreach ( Material M in _materials ) {
					// Filter by layers count
					if ( M.LayersCount < layersCountMin || M.LayersCount > layersCountMax )
						continue;

					// Filter by program type
					bool	skip = false;
					switch ( M.m_programs.m_type ) {
						case Material.Programs.KNOWN_TYPES.DEFAULT: skip = skipDefault; break;
						case Material.Programs.KNOWN_TYPES.SKIN: skip = skipSkin; break;
						case Material.Programs.KNOWN_TYPES.EYE: skip = skipEye; break;
						case Material.Programs.KNOWN_TYPES.HAIR: skip = skipHair; break;
						case Material.Programs.KNOWN_TYPES.VEGETATION: skip = skipVegetation; break;
						case Material.Programs.KNOWN_TYPES.VISTA: skip = skipVista; break;
						default: skip = skipOther; break;
					}
					bool	isAlpha = M.IsAlpha;
					if ( (skipAlpha && isAlpha) || (skipNonAlpha && !isAlpha) )
						continue;

					errorMaterialsCount += M.ErrorLevel != Material.ERROR_LEVEL.NONE ? 1 : 0;
					if ( (int) M.ErrorLevel < minErrorLevel )
						continue;
					if ( showOnlyWarningMats && !M.HasWarnings )
						continue;
					if ( showOnlyMissingPhysicsMats && M.HasPhysicsMaterial )
						continue;
					if ( showOnlyOptimizedMats && !M.IsOptimized )
						continue;

					if ( !skip )
						filteredMaterials.Add( M );
				}
			} else {
				// Search by name
				_searchFor = _searchFor.ToLower();
				foreach ( Material M in _materials ) {
					if ( M.m_name.ToLower().IndexOf( _searchFor ) != -1 ) {
						filteredMaterials.Add( M );
						errorMaterialsCount += M.ErrorLevel != Material.ERROR_LEVEL.NONE ? 1 : 0;
					}
				}
			}

// 			checkBoxShowTGA.Text = "Show " + texCountTGA + " TGA";
// 			checkBoxShowPNG.Text = "Show " + texCountPNG + " PNG";
// 			checkBoxShowOtherFormats.Text = "Show " + texCountOther + " misc. formats";
			labelTotalMaterials.Text = "Total Materials: " + filteredMaterials.Count + "\r\n"
									 + "In-Error Materials: " + errorMaterialsCount;

			// Sort
			if ( m_materialsSortOrder == 1 ) {
				switch ( m_materialsSortColumn ) {
					case 0: filteredMaterials.Sort( new MatCompareNames_Ascending() ); break;
					case 1: filteredMaterials.Sort( new MatCompareTypes_Ascending() ); break;
					case 2: filteredMaterials.Sort( new MatCompareLayers_Ascending() ); break;
					case 3: filteredMaterials.Sort( new MatCompareAlpha_Ascending() ); break;
					case 4: filteredMaterials.Sort( new MatCompareOptimized_Ascending() ); break;
					case 6: filteredMaterials.Sort( new MatCompareFileNames_Ascending() ); break;
				}
			} else {
				switch ( m_materialsSortColumn ) {
					case 0: filteredMaterials.Sort( new MatCompareNames_Descending() ); break;
					case 1: filteredMaterials.Sort( new MatCompareTypes_Descending() ); break;
					case 2: filteredMaterials.Sort( new MatCompareLayers_Descending() ); break;
					case 3: filteredMaterials.Sort( new MatCompareAlpha_Descending() ); break;
					case 4: filteredMaterials.Sort( new MatCompareOptimized_Descending() ); break;
					case 6: filteredMaterials.Sort( new MatCompareFileNames_Descending() ); break;
				}
			}

			// Rebuild list view
			listViewMaterials.BeginUpdate();
			listViewMaterials.Items.Clear();
			foreach ( Material M in filteredMaterials ) {

				string	errorString = M.ErrorString;

				ListViewItem	item = new ListViewItem( M.m_name );
				item.Tag = M;
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, M.m_programs.m_type.ToString() ) );
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, M.LayersCount.ToString() ) );			// Show the ACTUAL amount of layers used by the shader!
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, M.IsAlpha ? "Yes" : "" ) );
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, M.IsOptimized ? "Yes" : "" ) );
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, errorString ) );
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, M.m_sourceFileName.FullName ) );

				switch ( M.ErrorLevel ) {
					case Material.ERROR_LEVEL.DIRTY: item.BackColor = Color.Sienna; break;
					case Material.ERROR_LEVEL.STANDARD: item.BackColor = Color.Salmon; break;
					case Material.ERROR_LEVEL.DANGEROUS: item.BackColor = Color.Red; break;
					case Material.ERROR_LEVEL.NONE:
						if ( M.HasWarnings )
							item.BackColor = Color.Gold;
						else if ( M.IsOptimized )
							item.BackColor = Color.ForestGreen;
						break;
				}

				// Build tooltip
				item.ToolTipText = (M.HasErrors ? errorString : "")
								 + (M.HasWarnings ? M.WarningString : "");

				listViewMaterials.Items.Add( item );
			}

			listViewMaterials.EndUpdate();
		}

		class	MatCompareNames_Ascending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				return StringComparer.CurrentCultureIgnoreCase.Compare( x.m_name, y.m_name );
			}
		}
		class	MatCompareNames_Descending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				return -StringComparer.CurrentCultureIgnoreCase.Compare( x.m_name, y.m_name );
			}
		}

		class	MatCompareTypes_Ascending : IComparer< Material > {
			public int Compare(Material x, Material y) {
//				return StringComparer.CurrentCultureIgnoreCase.Compare( x.m_programs.m_type.ToString(), y.m_programs.m_type.ToString() );
				return (int) x.m_programs.m_type < (int) y.m_programs.m_type ? -1 : ((int) x.m_programs.m_type > (int) y.m_programs.m_type ? 1 : 0);
			}
		}
		class	MatCompareTypes_Descending : IComparer< Material > {
			public int Compare(Material x, Material y) {
//				return -StringComparer.CurrentCultureIgnoreCase.Compare( x.m_programs.m_type.ToString(), y.m_programs.m_type.ToString() );
				return (int) x.m_programs.m_type < (int) y.m_programs.m_type ? 1 : ((int) x.m_programs.m_type > (int) y.m_programs.m_type ? -1 : 0);
			}
		}

		class	MatCompareLayers_Ascending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				return x.m_layers.Count < y.m_layers.Count ? -1 : (x.m_layers.Count > y.m_layers.Count ? 1 : 0);
			}
		}
		class	MatCompareLayers_Descending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				return x.m_layers.Count < y.m_layers.Count ? -1 : (x.m_layers.Count > y.m_layers.Count ? 1 : 0);
			}
		}

		class	MatCompareAlpha_Ascending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				bool	a = x.IsAlpha;
				bool	b = y.IsAlpha;
				return a == b ? 0 : (a ? 1 : -1);
			}
		}
		class	MatCompareAlpha_Descending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				bool	a = x.IsAlpha;
				bool	b = y.IsAlpha;
				return a == b ? 0 : (b ? 1 : -1);
			}
		}

		class	MatCompareOptimized_Ascending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				bool	a = x.IsOptimized;
				bool	b = y.IsOptimized;
				return a == b ? 0 : (a ? 1 : -1);
			}
		}
		class	MatCompareOptimized_Descending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				bool	a = x.IsOptimized;
				bool	b = y.IsOptimized;
				return a == b ? 0 : (b ? 1 : -1);
			}
		}

		class	MatCompareFileNames_Ascending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				return StringComparer.CurrentCultureIgnoreCase.Compare( x.m_sourceFileName.FullName, y.m_sourceFileName.FullName );
			}
		}
		class	MatCompareFileNames_Descending : IComparer< Material > {
			public int Compare(Material x, Material y) {
				return -StringComparer.CurrentCultureIgnoreCase.Compare( x.m_sourceFileName.FullName, y.m_sourceFileName.FullName );
			}
		}

		#endregion

		#region Textures Parsing

		void	CollectTextures( DirectoryInfo _directory ) {
			if ( !_directory.Exists )
				throw new Exception( "Directory \"" + _directory.FullName + "\" for texture collection does not exist!" );

			ImageUtility.Bitmap.ReadContent = false;
			progressBarTextures.Visible = true;

			m_textureErrors.Clear();

			foreach ( TextureFileInfo TFI in m_textures )
				TFI.m_refCount = 0;	// Clear refcount so we can retrieve which textures have disappeared since last collection

			string[]	supportedExtensions = new string[] {
				 ".jpg",
				 ".png",
				 ".tga",
				 ".tiff",
//				 ".dds",
//				 ".bimage",
			};

			DateTime	startTime = DateTime.Now;
			LogLine( "Parsing image files starting at " + startTime.ToString( @"HH\:mm:ss" ) );

			List< FileInfo[] >	textureFileNamesForExtenstion = new List< FileInfo[] >();
			int					totalFilesCount = 0;
			foreach ( string supportedExtension in supportedExtensions ) {
				FileInfo[]	textureFileNames = _directory.GetFiles( "*" + supportedExtension, SearchOption.AllDirectories );
				textureFileNamesForExtenstion.Add( textureFileNames );
				totalFilesCount += textureFileNames.Length;
			}

			int	extensionIndex = 0;
			int	textureIndex = 0;
			int	texturesCountUpdated = 0;
			int	texturesCountAdded = 0;
			foreach ( FileInfo[] textureFileNames in textureFileNamesForExtenstion ) {
				foreach ( FileInfo textureFileName in textureFileNames ) {
					string	normalizedTextureFileName = TextureFileInfo.NormalizeFileName( textureFileName.FullName );
					if ( m_textureFileName2Texture.ContainsKey( normalizedTextureFileName ) ) {
						// Already exists! Check timestamp...
						TextureFileInfo	TFI = m_textureFileName2Texture[normalizedTextureFileName];
						TFI.m_refCount++;

						textureFileName.Refresh();
						TimeSpan	deltaTimeStamp = textureFileName.LastWriteTime - TFI.m_timeStamp;
						if ( deltaTimeStamp.TotalSeconds > 30 ) {
							// Image changed and needs to be read back again
							TFI.ReadImageInfos();
							TFI.m_timeStamp = textureFileName.LastWriteTime;	// Update time stamp so we're now up to date
							texturesCountUpdated++;
						}
						continue;
					}

					// Image doesn't exist, create a new one!
					try {
						TextureFileInfo	T = new TextureFileInfo( textureFileName );
						T.m_refCount++;
						m_textures.Add( T );
						m_textureFileName2Texture.Add( normalizedTextureFileName, T );
						texturesCountAdded++;
					} catch ( Exception _e ) {
						Error	Err = new Error( textureFileName, _e );
						m_textureErrors.Add( Err );
						LogLine( Err.ToString() );
					}

					textureIndex++;
					if ( (textureIndex % 100) == 0 ) {
						progressBarTextures.Value = textureIndex * progressBarTextures.Maximum / totalFilesCount;
						progressBarTextures.Refresh();
					}
				}

				extensionIndex++;
			}

			// Reparse image files for the ones without any reference so we can delete them
			int	totalTexturesRemoved = 0;
			for ( int i=0; i < m_textures.Count; i++ ) {
				TextureFileInfo	TFI = m_textures[i];
				if ( TFI.m_refCount == 0 ) {
					// Not referenced anymore...
					m_textureFileName2Texture.Remove( TFI.NormalizedFileName );
					m_textures.RemoveAt( i );
					totalTexturesRemoved++;
				}
			}

			LogLine( "Total textures => Parsed: " + totalFilesCount + " - Added: " + texturesCountAdded + " - Updated: " + texturesCountUpdated + " Removed: " + totalTexturesRemoved );

			DateTime	endTime = DateTime.Now;
			TimeSpan	totalTime = endTime - startTime;
			LogLine( "Finished parsing at " + endTime.ToString( @"HH\:mm:ss" ) + ". Total time: " + totalTime.ToString( @"mm\:ss" ) );

			progressBarTextures.Visible = false;
			ImageUtility.Bitmap.ReadContent = true;
		}

		void	CollectDiffuseGlossTextures( DirectoryInfo _directory ) {
			if ( !_directory.Exists )
				throw new Exception( "Directory \"" + _directory.FullName + "\" for texture collection does not exist!" );

			ImageUtility.Bitmap.ReadContent = false;
			progressBarTextures.Visible = true;

			// Clear the textures from "diffuse+gloss"
			TextureFileInfo[]	texturesCopy = m_textures.ToArray();

			m_textureErrors.Clear();
			m_textures.Clear();
			m_textureFileName2Texture.Clear();
			foreach ( TextureFileInfo TFI in texturesCopy ) {
				if ( TFI.m_usage != TextureFileInfo.USAGE.DIFFUSE_GLOSS ) {
					m_textures.Add( TFI );
					m_textureFileName2Texture.Add( TFI.NormalizedFileName, TFI );
				}
			}

			// Reparse all types
			string[]	supportedExtensions = new string[] {
				 ".jpg",
				 ".png",
				 ".tga",
				 ".tiff",
//				 ".dds",
//				 ".bimage",
			};

			List< FileInfo[] >	textureFileNamesForExtenstion = new List< FileInfo[] >();
			int					totalFilesCount = 0;
			foreach ( string supportedExtension in supportedExtensions ) {
				FileInfo[]	textureFileNames = _directory.GetFiles( "*_dg" + supportedExtension, SearchOption.AllDirectories );
				textureFileNamesForExtenstion.Add( textureFileNames );
				totalFilesCount += textureFileNames.Length;
			}

			int	extensionIndex = 0;
			int	textureIndex = 0;
			foreach ( FileInfo[] textureFileNames in textureFileNamesForExtenstion ) {
				LogLine( "Parsing " + textureFileNames.Length + " " + supportedExtensions[extensionIndex] + " image files" );
				DateTime	startTime = DateTime.Now;

				foreach ( FileInfo textureFileName in textureFileNames ) {
					try {
						TextureFileInfo	T = new TextureFileInfo( textureFileName );
						if ( T.m_usage == TextureFileInfo.USAGE.DIFFUSE_GLOSS ) {
							m_textures.Add( T );
							m_textureFileName2Texture.Add( T.NormalizedFileName, T );
						}
					} catch ( Exception _e ) {
						Error	Err = new Error( textureFileName, _e );
						m_textureErrors.Add( Err );
						LogLine( Err.ToString() );
					}

					textureIndex++;
					if ( (textureIndex % 100) == 0 ) {
						progressBarTextures.Value = textureIndex * progressBarTextures.Maximum / totalFilesCount;
						progressBarTextures.Refresh();
					}
				}

				TimeSpan	totalTime = DateTime.Now - startTime;
				LogLine( "Finished parsing " + supportedExtensions[extensionIndex] + " image files. Total time: " + totalTime.ToString( @"mm\:ss" ) );
				extensionIndex++;
			}

			progressBarTextures.Visible = false;
			ImageUtility.Bitmap.ReadContent = true;
		}

		void	SaveTexturesDatabase( FileInfo _fileName ) {
			using ( FileStream S = _fileName.Create() )
				using ( BinaryWriter W = new BinaryWriter( S ) ) {
					W.Write( m_textures.Count );
					foreach ( TextureFileInfo TFI in m_textures )
						TFI.Write( W );

					W.Write( m_textureErrors.Count );
					foreach ( Error E in m_textureErrors )
						E.Write( W );
				}
		}

		void	LoadTexturesDatabase( FileInfo _fileName ) {
			m_textures.Clear();
			m_textureErrors.Clear();

			try {
				using ( FileStream S = _fileName.OpenRead() )
					using ( BinaryReader R = new BinaryReader( S ) ) {
						int	texturesCount = R.ReadInt32();
						for ( int textureIndex=0; textureIndex < texturesCount; textureIndex++ ) {
							TextureFileInfo	TFI = new TextureFileInfo( R );
							m_textures.Add( TFI );
							m_textureFileName2Texture.Add( TFI.NormalizedFileName, TFI );
						}

						int	errorsCount = R.ReadInt32();
						for ( int errorIndex=0; errorIndex < errorsCount; errorIndex++ )
							m_textureErrors.Add( new Error( R ) );
					}
			} catch ( Exception ) {
				MessageBox( "An error occurred while reloading textures database!\nThe format must have changed since it was last saved...\n\nPlease recollect textures.", MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		void	RebuildTexturesListView() {
			RebuildTexturesListView( null );
		}
		void	RebuildTexturesListView( string _searchFor ) {

			// Filter textures
			List< TextureFileInfo >	filteredTextures = new List< TextureFileInfo >();
			int		texCountPNG = 0;
			int		texCountTGA = 0;
			int		texCountOther = 0;
			if ( _searchFor == null ) {
				bool	skipDiffuse = !checkBoxShowDiffuse.Checked ^ checkBoxInvertFilters.Checked;
				bool	skipDiffuseGloss = !checkBoxShowDiffuseGloss.Checked ^ checkBoxInvertFilters.Checked;
				bool	skipNormal = !checkBoxShowNormal.Checked ^ checkBoxInvertFilters.Checked;
				bool	skipGloss = !checkBoxShowGloss.Checked ^ checkBoxInvertFilters.Checked;
				bool	skipMetal = !checkBoxShowMetal.Checked ^ checkBoxInvertFilters.Checked;
				bool	skipEmissive = !checkBoxShowEmissive.Checked ^ checkBoxInvertFilters.Checked;
				bool	skipMasks = !checkBoxShowMasks.Checked ^ checkBoxInvertFilters.Checked;
				bool	skipOther = !checkBoxShowOther.Checked ^ checkBoxInvertFilters.Checked;
				int		minRefCount = integerTrackbarControlMinRefCount.Value;

				foreach ( TextureFileInfo TFI in m_textures ) {
					// Filter by type
					if ( TFI.m_fileType == TextureFileInfo.FILE_TYPE.TGA ) {
						texCountTGA++;
						if ( !checkBoxShowTGA.Checked )
							continue;
					} else if ( TFI.m_fileType == TextureFileInfo.FILE_TYPE.PNG ) {
						texCountPNG++;
						if ( !checkBoxShowPNG.Checked )
							continue;
					} else {
						texCountOther++;
						if ( !checkBoxShowOtherFormats.Checked )
							continue;
					}

					// Filter by usage
					bool	skip = false;
					switch ( TFI.m_usage ) {
						case TextureFileInfo.USAGE.DIFFUSE: skip = skipDiffuse; break;
						case TextureFileInfo.USAGE.DIFFUSE_GLOSS: skip = skipDiffuseGloss; break;
						case TextureFileInfo.USAGE.NORMAL: skip = skipNormal; break;
						case TextureFileInfo.USAGE.GLOSS: skip = skipGloss; break;
						case TextureFileInfo.USAGE.METAL: skip = skipMetal; break;
						case TextureFileInfo.USAGE.EMISSIVE: skip = skipEmissive; break;
						case TextureFileInfo.USAGE.MASK: skip = skipMasks; break;
						default: skip = skipOther; break;
					}

					if ( TFI.m_refCount < minRefCount )
						continue;

					if ( !skip )
						filteredTextures.Add( TFI );
				}
			} else {
				// Search by name
				_searchFor = _searchFor.ToLower();
				foreach ( TextureFileInfo TFI in m_textures ) {
					if ( TFI.m_fileName.FullName.ToLower().IndexOf( _searchFor ) != -1 ) {
						filteredTextures.Add( TFI );
						if ( TFI.m_fileType == TextureFileInfo.FILE_TYPE.TGA ) {
							texCountTGA++;
						} else if ( TFI.m_fileType == TextureFileInfo.FILE_TYPE.PNG ) {
							texCountPNG++;
						} else {
							texCountOther++;
						}
					}
				}
			}

			checkBoxShowTGA.Text = "Show " + texCountTGA + " TGA";
			checkBoxShowPNG.Text = "Show " + texCountPNG + " PNG";
			checkBoxShowOtherFormats.Text = "Show " + texCountOther + " misc. formats";
			labelTotalTextures.Text = "Total Textures:\n" + filteredTextures.Count;

			// Sort
			if ( m_texturesSortOrder == 1 ) {
				switch ( m_texturesSortColumn ) {
					case 0: filteredTextures.Sort( new CompareNames_Ascending() ); break;
					case 1: filteredTextures.Sort( new CompareSizes_Ascending() ); break;
					case 2: filteredTextures.Sort( new CompareUsages_Ascending() ); break;
					case 3: filteredTextures.Sort( new CompareChannelsCounts_Ascending() ); break;
					case 4: filteredTextures.Sort( new CompareRefCounts_Ascending() ); break;
				}
			} else {
				switch ( m_texturesSortColumn ) {
					case 0: filteredTextures.Sort( new CompareNames_Descending() ); break;
					case 1: filteredTextures.Sort( new CompareSizes_Descending() ); break;
					case 2: filteredTextures.Sort( new CompareUsages_Descending() ); break;
					case 3: filteredTextures.Sort( new CompareChannelsCounts_Descending() ); break;
					case 4: filteredTextures.Sort( new CompareRefCounts_Descending() ); break;
				}
			}


			// Rebuild list view
			listViewTextures.BeginUpdate();
			listViewTextures.Items.Clear();
			foreach ( TextureFileInfo TFI in filteredTextures ) {

				ListViewItem	item = new ListViewItem( TFI.m_fileName.FullName );
				item.Tag = TFI;
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, TFI.m_width.ToString() + "x" + TFI.m_height.ToString() ) );
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, TFI.m_usage.ToString() ) );
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, TFI.ColorChannelsCount > 0 ? TFI.ColorChannelsCount.ToString() : "?" ) );
				item.SubItems.Add( new ListViewItem.ListViewSubItem( item, TFI.m_refCount.ToString() ) );

				int	area = TFI.m_width * TFI.m_height;
				if ( area > 2048*2048 ) {
					item.BackColor = Color.Salmon;
				} else if ( area > 1024*1024 ) {
					item.BackColor = Color.Gold;
				}

				listViewTextures.Items.Add( item );
			}

			listViewTextures.EndUpdate();
		}

		class	CompareNames_Ascending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
				return StringComparer.CurrentCultureIgnoreCase.Compare( x.m_fileName.FullName, y.m_fileName.FullName );
			}
		}
		class	CompareNames_Descending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
				return -StringComparer.CurrentCultureIgnoreCase.Compare( x.m_fileName.FullName, y.m_fileName.FullName );
			}
		}

		class	CompareSizes_Ascending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
				int	area0 = x.m_width * x.m_height;
				int	area1 = y.m_width * y.m_height;
				return area0 < area1 ? -1 : (area0 > area1 ? 1 : 0);
			}
		}
		class	CompareSizes_Descending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
				int	area0 = x.m_width * x.m_height;
				int	area1 = y.m_width * y.m_height;
				return area0 < area1 ? 1 : (area0 > area1 ? -1 : 0);
			}
		}

		class	CompareUsages_Ascending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
//				return StringComparer.CurrentCultureIgnoreCase.Compare( x.m_usage.ToString(), y.m_usage.ToString() );
				return (int) x.m_usage < (int) y.m_usage ? -1 : ((int) x.m_usage > (int) y.m_usage ? 1 : 0);
			}
		}
		class	CompareUsages_Descending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
//				return -StringComparer.CurrentCultureIgnoreCase.Compare( x.m_usage.ToString(), y.m_usage.ToString() );
				return (int) x.m_usage < (int) y.m_usage ? 1 : ((int) x.m_usage > (int) y.m_usage ? -1 : 0);
			}
		}

		class	CompareChannelsCounts_Ascending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
				return x.ColorChannelsCount < y.ColorChannelsCount ? -1 : (x.ColorChannelsCount > y.ColorChannelsCount ? 1 : 0);
			}
		}
		class	CompareChannelsCounts_Descending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
				return x.ColorChannelsCount < y.ColorChannelsCount ? 1 : (x.ColorChannelsCount > y.ColorChannelsCount ? -1 : 0);
			}
		}

		class	CompareRefCounts_Ascending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
				return x.m_refCount < y.m_refCount ? -1 : (x.m_refCount > y.m_refCount ? 1 : 0);
			}
		}
		class	CompareRefCounts_Descending : IComparer< TextureFileInfo > {
			public int Compare(TextureFileInfo x, TextureFileInfo y) {
				return x.m_refCount < y.m_refCount ? 1 : (x.m_refCount > y.m_refCount ? -1 : 0);
			}
		}

		#endregion

		#region Materials Re-Export

		void	BuildM22MaterialsDictionary( Dictionary< string, List< Material > > _M2File2Materials ) {
			string		sourceBasePath = textBoxMaterialsBasePath.Text.ToLower();
			if ( !sourceBasePath.EndsWith( "\\" ) )
				sourceBasePath += "\\";
			string		targetBasePath = textBoxReExportPath.Text.ToLower();

			foreach ( Material M in m_sourceMaterials ) {
				string	originalName = M.m_sourceFileName.FullName.ToLower();
				if ( !originalName.StartsWith( sourceBasePath ) ) {
					// Not in source base path?
					LogLine( "Failed to locate material file \"" + M.m_sourceFileName.FullName + "\" as based on the source materials path \"" + sourceBasePath + "\"! Can't determine base-less filename and can't rebase material for re-export..." );
					continue;
				}

				string	baseLessName = originalName.Substring( sourceBasePath.Length );
				string	rebasedName = Path.Combine( targetBasePath, baseLessName );
				if ( !_M2File2Materials.ContainsKey( rebasedName ) ) {
					_M2File2Materials[rebasedName] = new List<Material>();	// New M2 file
					Directory.CreateDirectory( Path.GetDirectoryName( rebasedName ) );				// Ensure target directory exists
				}
				_M2File2Materials[rebasedName].Add( M );
			}
		}

		void	ReExportMaterials( DirectoryInfo _directory ) {
			if ( m_sourceMaterials.Count == 0 ) {
				MessageBox( "Can't re-export materials since the list of parsed materials is empty!\r\nPlease parse materials first before re-exporting...", MessageBoxButtons.OK, MessageBoxIcon.Warning );
				return;
			}

			progressBarReExportMaterials.Visible = true;

			//////////////////////////////////////////////////////////////////////////
			// 1] Collect all unique material M2 files and rebase them into export target directory
			LogLine( "" );
			LogLine( "Registering " + m_sourceMaterials.Count + " materials..." );

// 			RecurseParseMaterials( new DirectoryInfo( textBoxMaterialsBasePath.Text ), m_sourceMaterials, progressBarMaterials );
// 			if ( m_sourceMaterials.Count == 0 ) {
// 				MessageBox( "Failed to parse materials. Can't export..." );
// 				return;
// 			}
// 			if ( !AnalyzeMaterialsDatabase( m_sourceMaterials, true ) )
// 				return;

			Dictionary< string, List< Material > >	M2File2Materials = new Dictionary< string, List< Material > >();
			BuildM22MaterialsDictionary( M2File2Materials );

			LogLine( "	• Registered " + m_sourceMaterials.Count + " materials" );
			LogLine( "	• Registered " + M2File2Materials.Keys.Count + " M2 files" );


			//////////////////////////////////////////////////////////////////////////
			// 2] Cleanup all materials
			LogLine( "" );
			LogLine( "Cleaning " + m_sourceMaterials.Count + " materials..." );

			int totalClearedOptionsCount = 0;
			int totalRemovedTexturesCount = 0;
			int totalBlackColorConstantsCount = 0;
			int	totalSwappedSlotsCount = 0;
			int totalMissingTexturesReplacedCount = 0;
			int	totalRemovedHasOcclusionMapOptionsCount = 0;
			int totalReUseOptionsSetCount = 0;
			int	totalCleanedUpMaterialsCount = 0;

			int	materialIndex = 0;
			foreach ( Material M in m_sourceMaterials ) {
				try {
					int clearedOptionsCount = 0;
					int removedTexturesCount = 0;
					int blackColorConstantsCount = 0;
					int	swappedSlotsCount = 0;
					int	missingTexturesReplacedCount = 0;
					int	removedHasOcclusionMapOptionsCount = 0;
					int reUseOptionsSetCount = 0;
					M.CleanUp( ref clearedOptionsCount, ref removedTexturesCount, ref blackColorConstantsCount, ref swappedSlotsCount, ref missingTexturesReplacedCount, ref removedHasOcclusionMapOptionsCount, ref reUseOptionsSetCount );

					totalClearedOptionsCount += clearedOptionsCount;
					totalRemovedTexturesCount += removedTexturesCount;
                    totalBlackColorConstantsCount += blackColorConstantsCount;
					totalSwappedSlotsCount += swappedSlotsCount;
					totalMissingTexturesReplacedCount += missingTexturesReplacedCount;
					totalRemovedHasOcclusionMapOptionsCount += removedHasOcclusionMapOptionsCount;
					totalReUseOptionsSetCount += reUseOptionsSetCount;
					if ( clearedOptionsCount > 0 || removedTexturesCount > 0 || missingTexturesReplacedCount > 0 || reUseOptionsSetCount > 0 )
						totalCleanedUpMaterialsCount++;

				} catch ( Exception _e ) {
					LogLine( "Failed to cleanup material \"" + M.m_name + "\" because of the following error: " + _e.Message );
				}

				progressBarReExportMaterials.Value = ++materialIndex * progressBarReExportMaterials.Maximum / (2*m_sourceMaterials.Count);	// Progress from 0 to 50%
			}

			LogLine( "	• Total cleaned-up materials: " + totalCleanedUpMaterialsCount );
			LogLine( "	  > Total options removed: " + totalClearedOptionsCount );
			LogLine( "	  > Total textures removed: " + totalRemovedTexturesCount );
			LogLine( "	  > Total black color constant diffuse textures: " + totalBlackColorConstantsCount + " (= amount of diffuse textures finally multiplied by 0)" );
			LogLine( "	  > Total swapped textures: " + totalSwappedSlotsCount + " (2 slots were erroneously mixed together, like a normal instead of a gloss and vice-versa)" );
			LogLine( "	  > Total missing textures replaced: " + totalMissingTexturesReplacedCount );
			LogLine( "	  > Total invalid \"hasOcclusionMap\" options cleared: " + totalRemovedHasOcclusionMapOptionsCount + " (= amount of materials specifying AO maps but not providing any. The option was removed)" );
			LogLine( "	  > Total re-use options added: " + totalReUseOptionsSetCount );


			//////////////////////////////////////////////////////////////////////////
			// 4] Collect optimizable textures
			LogLine( "" );
			LogLine( "Collecting optimizable diffuse+gloss textures..." );

			foreach ( TextureFileInfo TFI in m_textures ) {
				TFI.m_associatedTexture = null;	// Clear associated textures
			}

			// 4.1) Simply collect all possible diffuse+gloss couples
			Dictionary< TextureFileInfo, List< TextureFileInfo > >	diffuse2GlossMaps = new Dictionary< TextureFileInfo, List< TextureFileInfo > >();
			foreach ( Material M in m_sourceMaterials ) {
				M.CollectDiffuseGlossTextures( diffuse2GlossMaps );
			}

			// 4.2) Keep (diffuse+gloss) forming a single couple (i.e. diffuse maps that are never used with more than a single gloss texture)
			Dictionary< TextureFileInfo, DiffuseGlossTexture >	oldDiffuse2DiffuseGlossTexture = m_diffuse2DiffuseGlossTexture;

 			m_diffuse2DiffuseGlossTexture =  new Dictionary< TextureFileInfo, DiffuseGlossTexture >();
 			m_diffuseGlossTextures.Clear();
			foreach ( TextureFileInfo diffuseMap in diffuse2GlossMaps.Keys ) {
				List< TextureFileInfo >	glossMaps = diffuse2GlossMaps[diffuseMap];
				if ( glossMaps.Count == 0 )
					continue;	// Never used with any gloss map

				bool			allSimilarGloss = true;
				TextureFileInfo	firstGlossMap = glossMaps[0];
				for ( int glossMapIndex=1; glossMapIndex < glossMaps.Count; glossMapIndex++ ) {
					TextureFileInfo	otherGlossMap = glossMaps[glossMapIndex];
					if ( otherGlossMap != firstGlossMap ) {
						// Several gloss maps are associated to that diffuse map, we can't compact it...
						allSimilarGloss = false;
						break;
					}
				}

				if ( allSimilarGloss ) {
					// Okay! Bingo!
					diffuseMap.m_associatedTexture = firstGlossMap;		// The gloss is now associated to the diffuse

					DiffuseGlossTexture	DGT = new DiffuseGlossTexture( diffuseMap, firstGlossMap );
					m_diffuseGlossTextures.Add( DGT );
					m_diffuse2DiffuseGlossTexture.Add( diffuseMap, DGT );

					// Try and reconnect with former existing texture
					if ( oldDiffuse2DiffuseGlossTexture.ContainsKey( diffuseMap ) ) {
						DiffuseGlossTexture	oldDGT = oldDiffuse2DiffuseGlossTexture[diffuseMap];
						DGT.m_diffuseTimeAtGeneration = oldDGT.m_diffuseTimeAtGeneration;
						if ( DGT.m_gloss == oldDGT.m_gloss )
							DGT.m_glossTimeAtGeneration = oldDGT.m_glossTimeAtGeneration;
						DGT.m_optimizedDiffuseGlossFileName = oldDGT.m_optimizedDiffuseGlossFileName;
						DGT.m_optimizedDiffuseGloss = oldDGT.m_optimizedDiffuseGloss;
					}
				}
			}
			LogLine( "	• Possible candidate textures for (diffuse+gloss) optimization: " + m_diffuseGlossTextures.Count );

			// 4.3) Analyze materials again to know which ones are using paired textures
			// As soon as a material is NOT using a paired texture, in either of its channels, then it discards the (diffuse+gloss) pair
			int	previousTotalOptimizableMaterialsCount = -1;
			int	totalOptimizableMaterialsCount = 0;
			int	totalTexturesRemoved = -1;
//			while ( totalOptimizableMaterialsCount != previousTotalOptimizableMaterialsCount ) {
			while ( totalTexturesRemoved != 0 ) {
				// Repeat the process multiple times as some materials may discard some textures after some materials approved them
				// After a couple of iterations, the number of discarded textures and materials is steady we get our final count
				previousTotalOptimizableMaterialsCount = totalOptimizableMaterialsCount;
				totalOptimizableMaterialsCount = 0;
				totalTexturesRemoved = 0;
				foreach ( Material M in m_sourceMaterials ) {
					bool    allLayersAreUsingPairedTexture = !M.IsAlpha && M.m_options.m_hasGloss && M.m_programs.m_type == Material.Programs.KNOWN_TYPES.DEFAULT;
					foreach ( Material.Layer L in M.m_layers ) {
						Material.Layer.Texture  diffuseTexture = L.Diffuse;
						if (	diffuseTexture == null
							 || diffuseTexture.m_textureFileInfo == null
							 || diffuseTexture.m_textureFileInfo.m_usage != TextureFileInfo.USAGE.DIFFUSE
							 || diffuseTexture.m_textureFileInfo.m_associatedTexture == null ) {
							// This layer is not using a paired texture, the material is an invalid candidate and all its layers' diffuse textures should be prevented from being optimized
							allLayersAreUsingPairedTexture = false;
							break;
						}
					}

					if ( allLayersAreUsingPairedTexture ) {
						totalOptimizableMaterialsCount++;
						continue;
					}

					// Okay so at least one layer of the material is not using a paired texture
					// That means no layer can use paired texture so we cancel pairing for all diffuse textures used by the material...
					foreach ( Material.Layer L in M.m_layers ) {
						if ( L.m_diffuse != null && L.m_diffuse.m_textureFileInfo != null && L.m_diffuse.m_textureFileInfo.m_associatedTexture != null ) {
							L.m_diffuse.m_textureFileInfo.m_associatedTexture = null;		// Clear the pairing

							DiffuseGlossTexture	DGT = m_diffuse2DiffuseGlossTexture[L.m_diffuse.m_textureFileInfo];
							m_diffuseGlossTextures.Remove( DGT );
							m_diffuse2DiffuseGlossTexture.Remove( L.m_diffuse.m_textureFileInfo );
							totalTexturesRemoved++;
						}
					}
				}
			}
			LogLine( "	• Final count of candidate textures for (diffuse+gloss) optimization after filtering by optimizable materials: " + m_diffuseGlossTextures.Count );
			LogLine( "	• Total optimizable materials: " + totalOptimizableMaterialsCount );

			// 4.4) Optimize materials so they replace their diffuse by the new combo
			SaveDiffuseGlossTexturesDatabase( m_diffuseGlossTextureDatabaseFileName );
			LogLine( "	• Saved optimizable textures database" );
			buttonGenerate_dgTextures.Enabled = m_diffuseGlossTextures.Count > 0;

			// 4.5) Optimize materials so they replace their diffuse by the new combo
			int	totalDiffuseGlossTexturesReplaced = 0;
			int	totalOptimizedMaterialsCount = 0;
			foreach ( Material M in m_sourceMaterials )
				try {
					if ( M.Optimize( ref totalDiffuseGlossTexturesReplaced ) )
						totalOptimizedMaterialsCount++;
				} catch ( Exception _e ) {
					LogLine( " !Failed to optimize material \"" + M + "\": " + _e.Message );
				}

			LogLine( "	• Total optimized materials: " + totalOptimizedMaterialsCount + " (" + totalDiffuseGlossTexturesReplaced + " diffuse textures now using embedded gloss)" );

			
			//////////////////////////////////////////////////////////////////////////
			// 5] Regenerate all M2 files
			LogLine( "" );
			LogLine( "Writing " + M2File2Materials.Keys.Count + " M2 files..." );

			m_optimizedMaterials.Clear();

			int	fileIndex = 0;
			int	correctlyExportedFilesCount = 0;
			foreach ( string targetM2FileName in M2File2Materials.Keys ) {

				try {
					StringBuilder	SB = new StringBuilder();
					using( StringWriter W = new StringWriter( SB ) ) {
						// Regenerate all materials in the M2 file
						List< Material >	materialsInFile = M2File2Materials[targetM2FileName];
						foreach ( Material M in materialsInFile ) {
							M.Export( W );
							m_optimizedMaterials.Add( M );
						}
					}

					// Write to M2 file in a single time
					FileInfo	M2FileName = new FileInfo( targetM2FileName );
					using ( StreamWriter fileS = M2FileName.CreateText() )
						fileS.Write( SB.ToString() );

					correctlyExportedFilesCount++;

				} catch ( Exception _e ) {
					LogLine( "An error occurred while exporting M2 file \"" + targetM2FileName + "\": " + _e.Message );
				}

				progressBarReExportMaterials.Value = progressBarReExportMaterials.Maximum/2 + (++fileIndex * progressBarReExportMaterials.Maximum) / (2*M2File2Materials.Keys.Count);	// Progress from 50 to 100%
			}

			LogLine( "	• Successfully wrote " + correctlyExportedFilesCount + " M2 files..." );

			//////////////////////////////////////////////////////////////////////////
			// 6] Re-parse optimized materials
			progressBarReExportMaterials.Visible = false;

			buttonParseReExportedMaterials.Enabled = m_optimizedMaterials.Count > 0;
			buttonIntegratePerforce.Enabled = m_optimizedMaterials.Count > 0;
			buttonCreateTestMap.Enabled = m_optimizedMaterials.Count > 0;

			buttonParseReExportedMaterials_Click( null, EventArgs.Empty );
		}

		#endregion

		#region (Diffuse+Gloss) Textures Generation

		void SaveDiffuseGlossTexturesDatabase( FileInfo _fileName ) {
			try {
				using ( FileStream S = _fileName.Create() )
					using ( BinaryWriter W = new BinaryWriter( S ) ) {
						W.Write( m_diffuseGlossTextures.Count );
						foreach ( DiffuseGlossTexture DGT in m_diffuseGlossTextures ) {
							DGT.Write( W );
						}
					}
			} catch ( Exception ) {
				MessageBox( "An error occurred while saving (diffuse+gloss) textures database!", MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		void LoadDiffuseGlossTexturesDatabase( FileInfo _fileName ) {
			m_diffuseGlossTextures.Clear();
			m_diffuse2DiffuseGlossTexture.Clear();

			try {
				using ( FileStream S = _fileName.OpenRead() )
					using ( BinaryReader R = new BinaryReader( S ) ) {
						int	texturesCount = R.ReadInt32();
						for ( int textureIndex=0; textureIndex < texturesCount; textureIndex++ ) {
							try {
								DiffuseGlossTexture	DGT = new DiffuseGlossTexture( R );
								ResolveTextureFileInfo( DGT );

								m_diffuseGlossTextures.Add( DGT );
								m_diffuse2DiffuseGlossTexture.Add( DGT.m_diffuse, DGT );
							} catch ( Exception _e ) {
								LogLine( "An error occurred while rebuilding (diffuse+gloss) texture info: " + _e.Message );
								throw _e;
							}
						}
					}
			} catch ( Exception ) {
				MessageBox( "An error occurred while reloading (diffuse+gloss) textures database!\nThe format must have changed since it was last saved...\n\nPlease re-export optimized materials.", MessageBoxButtons.OK, MessageBoxIcon.Error );
			}

			buttonGenerate_dgTextures.Enabled = m_diffuseGlossTextures.Count > 0;
		}

		/// <summary>
		/// Parses all materials requiring "_dg" diffuse + gloss textures and builds a list of unique textures
		/// Creates the image file when the diffuse and gloss texture files are available and of the same size
		/// </summary>
		void GenerateDiffuseGlossTextures() {
			if ( m_diffuseGlossTextures.Count <= 0 ) {
				MessageBox( "No (diffuse+gloss) textures are available...\nYou need to re-export materials first!" );
				return;
			}

			try {
				progressBarTextures.Visible = true;
				ImageUtility.Bitmap.ConvertContent2XYZ = false;	// Skip conversion

				LogLine( "" );

				// 1] List unique textures to generate
				List< TextureFileInfo >		glossTexturesToRescale = new List< TextureFileInfo >();
				List< DiffuseGlossTexture >	diffuseGlossTexturesToGenerate = new List< DiffuseGlossTexture >();
				int							glossLargerThanDiffuseCount = 0;
				LogLine( "Checking " + m_diffuseGlossTextures.Count + " (diffuse+gloss) texture candisdates..." );
				foreach ( DiffuseGlossTexture DGT in m_diffuseGlossTextures ) {
					if ( DGT == null ) {
						LogLine( "Invalid (diffuse+gloss) entry" );
						continue;
					}
					if ( DGT.m_diffuse == null ) {
						LogLine( "Invalid (diffuse+gloss) diffuse texture!" );
						continue;
					}
					if ( DGT.m_gloss == null )
						continue;

					if ( DGT.m_gloss.m_width < DGT.m_diffuse.m_width || DGT.m_gloss.m_height < DGT.m_diffuse.m_height ) {
						// Needs scaling...
						glossTexturesToRescale.Add( DGT.m_gloss );
						continue;
					}
					if ( DGT.m_gloss.m_width > DGT.m_diffuse.m_width || DGT.m_gloss.m_height > DGT.m_diffuse.m_height )
						glossLargerThanDiffuseCount++;

					if ( DGT.NeedsToBeGenerated )
						diffuseGlossTexturesToGenerate.Add( DGT );
				}
				LogLine( " > Found " + glossTexturesToRescale.Count + " gloss texture files that need to be rescaled to match diffuse texture size" );
				LogLine( " > Found " + glossLargerThanDiffuseCount + " gloss texture files that are LARGER than diffuse texture size" );
				LogLine( " > Found " + diffuseGlossTexturesToGenerate.Count + " (diffuse+gloss) texture files that need to be regenerated" );

				TexturesToRescaleForm	F = new TexturesToRescaleForm();
				F.glossTexturesToRescale = glossTexturesToRescale;
				if ( F.ShowDialog( this ) != DialogResult.OK )
					return;


				// 2] Merge textures with same size
				DateTime	startTime = DateTime.Now;
				LogLine( "" );
				LogLine( "Generating " + diffuseGlossTexturesToGenerate.Count + " (diffuse+gloss) textures..." );

				int	processedTexturesCount = 0;
				int	generatedTexturesCount = 0;
				foreach ( DiffuseGlossTexture DGT in diffuseGlossTexturesToGenerate ) {
					try {
						progressBarTextures.Value = (++processedTexturesCount) * progressBarTextures.Maximum / diffuseGlossTexturesToGenerate.Count;

						if ( DGT.m_gloss != null && (DGT.m_gloss.m_width < DGT.m_diffuse.m_width || DGT.m_gloss.m_height < DGT.m_diffuse.m_height) ) {
							LogLine( " > Gloss texture \"" + DGT.m_gloss.m_fileName.FullName + "\" is SMALLER than its associated diffuse texture \"" + DGT.m_diffuse.m_fileName.FullName + "\" => Image will be scaled using bilinear interpolation!" );
						}
						if ( DGT.m_gloss != null && (DGT.m_gloss.m_width > DGT.m_diffuse.m_width || DGT.m_gloss.m_height > DGT.m_diffuse.m_height) ) {
							LogLine( " > Gloss texture \"" + DGT.m_gloss.m_fileName.FullName + "\" is LARGER than its associated to diffuse texture \"" + DGT.m_diffuse.m_fileName.FullName + "\" => Image will be downscaled using averaging" );
// 							continue;	// Not the same size... Can't merge!
 						}

						FileInfo	targetFileName = DGT.GetDiffuseGlossTextureFileName();
						bool		markForAdd = !PerforceCheckOut( targetFileName.FullName );

						DGT.GenerateDiffuseGlossTexture();

						if ( markForAdd )
							PerforceAdd( targetFileName.FullName );

						generatedTexturesCount++;

					} catch ( Exception _e ) {
						LogLine( " > An error occurred while generating (diffuse+gloss) texture for \"" + DGT.m_diffuseFileName.FullName + "\": " + _e.Message );
					}
				}

				SaveDiffuseGlossTexturesDatabase( m_diffuseGlossTextureDatabaseFileName );

				TimeSpan	totalTime = DateTime.Now - startTime;
				LogLine( " • Generated " + generatedTexturesCount + " (diffuse+gloss) texture files." );
				if ( generatedTexturesCount != processedTexturesCount )
					LogLine( " • " + (processedTexturesCount-generatedTexturesCount) + " texture failed to generate." );
				LogLine( "Total time: " + totalTime.ToString( @"mm\:ss" ) );
				LogLine( "" );

				// Collect (diffuse+gloss) textures only + analyze optimized materials that should be able to resolve their (diffuse+gloss) textures now
				buttonCollect_dgTextures_Click( null, EventArgs.Empty );

			} catch ( Exception ) {
				throw;
			} finally {
				progressBarTextures.Visible = false;
				ImageUtility.Bitmap.ConvertContent2XYZ = true;
			}
		}

		#endregion

		private void buttonSetMaterialsBasePath_Click(object sender, EventArgs e) {
			folderBrowserDialog.SelectedPath = GetRegKey( "MaterialsBasePath", textBoxMaterialsBasePath.Text );
			folderBrowserDialog.Description = "Select the folder containing materials to parse";
			if ( folderBrowserDialog.ShowDialog( this ) != DialogResult.OK ) {
				return;
			}

			SetRegKey( "MaterialsBasePath", folderBrowserDialog.SelectedPath );
			textBoxMaterialsBasePath.Text = folderBrowserDialog.SelectedPath;
		}

		private void buttonSetTexturesBasePath_Click(object sender, EventArgs e) {
			folderBrowserDialog.SelectedPath = GetRegKey( "TexturesBasePath", textBoxTexturesBasePath.Text );
			folderBrowserDialog.Description = "Select the base folder for Dishonored 2";
			if ( folderBrowserDialog.ShowDialog( this ) != DialogResult.OK ) {
				return;
			}

			SetRegKey( "TexturesBasePath", folderBrowserDialog.SelectedPath );
			textBoxTexturesBasePath.Text = folderBrowserDialog.SelectedPath;

			Material.Layer.Texture.ms_TexturesBasePath = new DirectoryInfo( textBoxTexturesBasePath.Text );
		}

		private void buttonSetMaterialsReExportPath_Click(object sender, EventArgs e) {
			folderBrowserDialog.SelectedPath = GetRegKey( "MaterialsReExportPath", textBoxReExportPath.Text );
			folderBrowserDialog.Description = "Select the base folder where to re-export";
			if ( folderBrowserDialog.ShowDialog( this ) != DialogResult.OK ) {
				return;
			}

			SetRegKey( "MaterialsReExportPath", folderBrowserDialog.SelectedPath );
			textBoxReExportPath.Text = folderBrowserDialog.SelectedPath;
		}

		private void buttonParseMaterials_Click(object sender, EventArgs e) {
			try {
				RecurseParseMaterials( new DirectoryInfo( textBoxMaterialsBasePath.Text ), m_sourceMaterials, progressBarMaterials );
				buttonReExport.Enabled = m_sourceMaterials.Count > 0;

				SaveMaterialsDatabase( m_sourceMaterialsDatabaseFileName, m_sourceMaterials );
				AnalyzeMaterialsDatabase( m_sourceMaterials, true );
				RebuildMaterialsListView( m_sourceMaterials );
			} catch ( Exception _e ) {
				MessageBox( "An error occurred while parsing materials:\r\n" + _e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		private void buttonCollectTextures_Click(object sender, EventArgs e) {
			if ( MessageBox( "Collecting textures can take some serious time as they're read to determine their sizes!\r\nAre you sure you wish to proceed?", MessageBoxButtons.YesNo, MessageBoxIcon.Question ) != DialogResult.Yes ) {
				return;
			}

			try {
				CollectTextures( new DirectoryInfo( Path.Combine( textBoxTexturesBasePath.Text, "models" ) ) );
				SaveTexturesDatabase( m_texturesDatabaseFileName );
				AnalyzeMaterialsDatabase( m_sourceMaterials, true );
				RebuildTexturesListView();
			} catch ( Exception _e ) {
				MessageBox( "An error occurred while collecting textures:\r\n" + _e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		private void buttonCollect_dgTextures_Click(object sender, EventArgs e) {
			try {
				CollectDiffuseGlossTextures( new DirectoryInfo( Path.Combine( textBoxTexturesBasePath.Text, "models" ) ) );
				SaveTexturesDatabase( m_texturesDatabaseFileName );
				AnalyzeMaterialsDatabase( m_optimizedMaterials, true );
				RebuildTexturesListView();
			} catch ( Exception _e ) {
				MessageBox( "An error occurred while collecting diffuse+gloss textures:\r\n" + _e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		private void buttonReExport_Click(object sender, EventArgs e) {
			try {
				ReExportMaterials( new DirectoryInfo( textBoxReExportPath.Text ) );
			} catch ( Exception _e ) {
				MessageBox( "An error occurred while re-exporting materials:\r\n" + _e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		private void buttonParseReExportedMaterials_Click(object sender, EventArgs e) {
			try {
				RecurseParseMaterials( new DirectoryInfo( textBoxReExportPath.Text ), m_optimizedMaterials, progressBarReExportMaterials );
				SaveMaterialsDatabase( m_optimizedMaterialsDatabaseFileName, m_optimizedMaterials );
				AnalyzeMaterialsDatabase( m_optimizedMaterials, true );
				RebuildMaterialsListView( m_optimizedMaterials );
			} catch ( Exception _e ) {
				MessageBox( "An error occurred while parsing materials:\r\n" + _e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		private void buttonGenerate_dgTextures_Click(object sender, EventArgs e) {
			try {
				GenerateDiffuseGlossTextures();
			} catch ( Exception _e ) {
				MessageBox( "An error occurred while parsing materials:\r\n" + _e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		#region Perforce Integration

// 		string	m_template = "Change:	new\n\n"
// 							+ "#Client:	cougar_dis2\n"
// 							+ "#Status:	new\n"
// 							+ "Description:\n<DESCRIPTION>\n\n"
// 							+ "Files:\n<FILES>\n";

		string		ExecutePerforceCommand( string _command ) {
			try {
 				Process	P = new Process();
				ProcessStartInfo	psi = P.StartInfo;
				psi.FileName = "P4";
				psi.Arguments = _command;
 				psi.UseShellExecute = false;
				psi.CreateNoWindow = true;
				psi.RedirectStandardOutput = true;
 				P.Start();

				P.WaitForExit( 100000 );

				string	result = P.StandardOutput.ReadToEnd();
				return result;
			} catch ( Exception _e ) {
				LogLine( "An error occurred while executing perforce command \"" + _command + "\": " + _e.Message );
			}
			return null;
		}

		bool		PerforceIsFileInDepot( string _fileName ) {
			string	result = ExecutePerforceCommand( "fstat -T haveRev " + _fileName );
			return result.ToLower().IndexOf( "haverev" ) != -1;;
		}

		bool		PerforceCheckOut( string _fileName ) {
			string	result = ExecutePerforceCommand( "edit " + _fileName );
			return result.ToLower().IndexOf( "opened for edit" ) != -1;
		}
		void		PerforceAdd( string _fileName ) {
			ExecutePerforceCommand( "add " + _fileName );
		}
		void		PerforceCheckOutOrAdd( string _fileName ) {
			if ( PerforceIsFileInDepot( _fileName ) )
				PerforceCheckOut( _fileName );
			else
				PerforceAdd( _fileName );
		}

		private void buttonIntegratePerforce_Click(object sender, EventArgs e) {

// 			// Write changelist description file
// 			FileInfo	changeListFileName = new FileInfo( "perforceChangeList_Materials.txt" );
// 			using ( StreamWriter W = changeListFileName.CreateText() ) {
// 
// 				string	description = "ta mere!";
// 				string	files = "testFile\ntestFile2";
// 
// 				string	content = m_template.Replace( "<DESCRIPTION>", description ).Replace( "<FILES>", files );
// 
// 				W.Write( content );
// 			}
// 
//  			ProcessStartInfo psi = new ProcessStartInfo( "P4 change " + changeListFileName.FullName );
//  			psi.UseShellExecute = true;
//  			Process.Start(psi);

			// List all source and optimized M2 files
			Dictionary< string, string >	originalM2optimizedM2 = new Dictionary< string, string >();
			string		sourceBasePath = textBoxMaterialsBasePath.Text.ToLower();
			if ( !sourceBasePath.EndsWith( "\\" ) )
				sourceBasePath += "\\";
			string		targetBasePath = textBoxReExportPath.Text.ToLower();

			foreach ( Material M in m_sourceMaterials ) {
				string	originalName = M.m_sourceFileName.FullName.ToLower();
				if ( !originalName.StartsWith( sourceBasePath ) ) {
					// Not in source base path?
					LogLine( "Failed to locate material file \"" + M.m_sourceFileName.FullName + "\" as based on the source materials path \"" + sourceBasePath + "\"! Can't determine base-less filename and can't rebase material for re-export..." );
					continue;
				}

				string	baseLessName = originalName.Substring( sourceBasePath.Length );
				string	rebasedName = Path.Combine( targetBasePath, baseLessName );
				if ( originalM2optimizedM2.ContainsKey( originalName ) ) {
					continue;
				}

				originalM2optimizedM2.Add( originalName, rebasedName );
			}

			// Check-out all original files and copy optimized files over
			foreach ( string originalM2FileName_string in originalM2optimizedM2.Keys ) {
				try {
					FileInfo	originalM2FileName = new FileInfo( originalM2FileName_string );
					if ( !originalM2FileName.Exists ) {
						LogLine( "Failed to locate original material file \"" + originalM2FileName.FullName + "\" for checkout..." );
						continue;
					}
					FileInfo	optimizedM2FileName = new FileInfo( originalM2optimizedM2[originalM2FileName_string] );
					if ( !optimizedM2FileName.Exists ) {
						LogLine( "Failed to locate optimized material file \"" + optimizedM2FileName.FullName + "\" for copy..." );
						continue;
					}

					// Check out
					if ( !PerforceCheckOut( originalM2FileName.FullName ) ) {
						LogLine( "FAILED to check-out \"" + originalM2FileName + "!" );
						continue;
					}

					// Copy
					optimizedM2FileName.CopyTo( originalM2FileName.FullName, true );
					LogLine( "Integrated \"" + originalM2FileName + "..." );

				} catch ( Exception _e ) {
					LogLine( "An error occurred while integrating material file \"" + originalM2FileName_string + "\": " + _e.Message );
				}
			}
		}

		#endregion

		#region Test Map Generation

		private void buttonCreateTestMap_Click(object sender, EventArgs e) {

			// Parse all materials featuring a (diffuse+gloss) map
			List< Material >	materialsWithOptimizedDiffuseGloss = new List< Material >();
			foreach ( Material M in m_optimizedMaterials ) {
				foreach ( Material.Layer L in M.m_layers ) {
					if ( L.m_diffuse != null && L.m_diffuse.m_textureFileInfo != null && L.m_diffuse.m_textureFileInfo.m_usage == TextureFileInfo.USAGE.DIFFUSE_GLOSS ) {
						// Found another material with diffuse+gloss!
						materialsWithOptimizedDiffuseGloss.Add( M );
						break;
					}
				}
			}

			if ( materialsWithOptimizedDiffuseGloss.Count == 0 ) {
				MessageBox( "There are currently no optimized material that are using (diffuse+gloss) optimized texture.\nIt's useless to generate an empty map...", MessageBoxButtons.OK, MessageBoxIcon.Warning );
				return;
			}
			if ( MessageBox( "You're about to generate a test map with " + materialsWithOptimizedDiffuseGloss.Count + " optimized materials.\n\nProceed?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question ) != DialogResult.OK )
				return;

			try {
				// Ask the map name
				string	testMapPath = GetRegKey( "TestMapPath", textBoxMaterialsBasePath.Text );
				saveFileDialogTestMap.FileName = Path.GetFileName( testMapPath );
				saveFileDialogTestMap.InitialDirectory = Path.GetDirectoryName( testMapPath );
				if ( saveFileDialogTestMap.ShowDialog( this ) != DialogResult.OK ) {
					return;
				}

				SetRegKey( "TestMapPath", saveFileDialogTestMap.FileName );

				string	mapDirectory = Path.GetDirectoryName( saveFileDialogTestMap.FileName ).Replace( "\\", "/" );
				string	mapBasePath = "";
				int		stripStartIndex = mapDirectory.ToLower().IndexOf( "/maps/" );
				if ( stripStartIndex != -1 ) {
					mapBasePath = mapDirectory.Substring( stripStartIndex+1 );
				}

				// Build the map
				StringBuilder	SB = new StringBuilder();
				SB.Append( Properties.Resources.mapHeader );
				SB.Append( Properties.Resources.entityTemplateProbe.Replace( "<MAP_PATH>", mapBasePath ) );

				int		size = (int) Math.Ceiling( Math.Sqrt( materialsWithOptimizedDiffuseGloss.Count ) );
				float	offsetX = -0.5f * size;
				float	offsetZ = 0.0f;

				int	materialIndex = 0;
				for ( int rowIndex=0; rowIndex < size; rowIndex++ ) {
					for ( int columnIndex=0; columnIndex < size; columnIndex++, materialIndex++ ) {
						if ( materialIndex >= materialsWithOptimizedDiffuseGloss.Count )
							break;

						Material	M = materialsWithOptimizedDiffuseGloss[materialIndex];

						string	entityString = Properties.Resources.entityTemplateSlate;
						entityString = entityString.Replace( "<ENTITY_NAME>", "slate_" + columnIndex.ToString( "G2" ) + "_" + rowIndex.ToString( "G2" ) );
						entityString = entityString.Replace( "<MATERIAL_NAME>", M.m_name );
						entityString = entityString.Replace( "<POSITION_X>", (offsetX + columnIndex).ToString() );
						entityString = entityString.Replace( "<POSITION_Y>", "0" );
						entityString = entityString.Replace( "<POSITION_Z>", (offsetZ + rowIndex).ToString() );
						SB.Append( entityString );
					}
				}

				// Save...
				using ( StreamWriter W = new FileInfo( saveFileDialogTestMap.FileName ).CreateText() )
					W.Write( SB.ToString() );

				MessageBox( "Success!", MessageBoxButtons.OK, MessageBoxIcon.Information );

			} catch ( Exception _e ) {
				MessageBox( "An error occurred while saving the test map: " + _e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error );
			}
		}

		#endregion

		#region Texture List View Events

		private void checkBoxShowOtherFormats_CheckedChanged(object sender, EventArgs e)
		{
			RebuildTexturesListView();
		}

		private void checkBoxShowPNG_CheckedChanged(object sender, EventArgs e)
		{
			RebuildTexturesListView();
		}

		private void checkBoxShowTGA_CheckedChanged(object sender, EventArgs e)
		{
			RebuildTexturesListView();
		}

		private void listViewTextures_ColumnClick(object sender, ColumnClickEventArgs e)
		{
			if ( e.Column == m_texturesSortColumn )
				m_texturesSortOrder *= -1;
			else
				m_texturesSortOrder = 1;
			m_texturesSortColumn = e.Column;

			RebuildTexturesListView();
		}

		private void checkBoxShowDiffuse_CheckedChanged(object sender, EventArgs e)
		{
			RebuildTexturesListView();
		}

		private void listViewTextures_MouseDoubleClick(object sender, MouseEventArgs e) {
			ListViewItem	item = listViewTextures.GetItemAt( e.X, e.Y );
			TextureFileInfo	TFI = item != null ? item.Tag as TextureFileInfo : null;
			if ( TFI == null || TFI.m_fileName == null )
				return;

			if ( !TFI.m_fileName.Exists ) {
				MessageBox( "Texture file \"" + TFI.m_fileName.FullName + "\" cannot be found on disk!" );
				return;
			}

			ProcessStartInfo psi = new ProcessStartInfo( TFI.m_fileName.FullName );
			psi.UseShellExecute = true;
			Process.Start(psi);
		}

		private void integerTrackbarControlMinRefCount_ValueChanged(Nuaj.Cirrus.Utility.IntegerTrackbarControl _Sender, int _FormerValue)
		{
			RebuildTexturesListView();
		}

		private void buttonSearchTexture_Click(object sender, EventArgs e)
		{
			RebuildTexturesListView( textBoxSearchTexture.Text != "" ? textBoxSearchTexture.Text : null );
		}

		#endregion

		#region Material List View Events

		private void radioButtonViewMaterialsList_CheckedChanged(object sender, EventArgs e) {
			if ( (sender as RadioButton).Checked )
				RebuildMaterialsListView();
		}

		private void listViewMaterials_SelectedIndexChanged(object sender, EventArgs e) {
			tabControlInfo.SelectedTab = tabPageInfo;	// Focus on info tab
			if ( listViewMaterials.SelectedItems.Count == 0 ) {
				textBoxInfo.Text = null;
				return;
			}

			Material	M = listViewMaterials.SelectedItems[0].Tag as Material;
//			textBoxInfo.Text = M.ToString();
			textBoxInfo.Lines = M.ToString().Split( '\n' );
		}

		private void listViewMaterials_MouseDoubleClick(object sender, MouseEventArgs e) {
			ListViewItem	item = listViewMaterials.GetItemAt( e.X, e.Y );
			Material		M = item != null ? item.Tag as Material : null;
			if ( M == null )
				return;
			if ( M.m_sourceFileName == null ) {
				MessageBox( "Material \"" + M.m_name + "\" doesn't have a source file name and can't be located on disk!" );
				return;
			}
			if ( !M.m_sourceFileName.Exists ) {
				MessageBox( "Material \"" + M.m_name + "\" source file \"" + M.m_sourceFileName.FullName + "\" cannot be found on disk!" );
				return;
			}

			Clipboard.SetText( M.m_name );

			ProcessStartInfo psi = new ProcessStartInfo( M.m_sourceFileName.FullName );
			psi.UseShellExecute = true;
			Process.Start(psi);
		}

		private void checkBoxShowArkDefault_CheckedChanged(object sender, EventArgs e)
		{
			RebuildMaterialsListView();
		}

		private void listViewMaterials_ColumnClick(object sender, ColumnClickEventArgs e)
		{
			if ( e.Column == m_materialsSortColumn )
				m_materialsSortOrder *= -1;
			else
				m_materialsSortOrder = 1;
			m_materialsSortColumn = e.Column;

			RebuildMaterialsListView();
		}

		private void integerTrackbarControlLayerMin_ValueChanged(Nuaj.Cirrus.Utility.IntegerTrackbarControl _Sender, int _FormerValue)
		{
			RebuildMaterialsListView();
		}

		private void integerTrackbarControlLayerMax_ValueChanged(Nuaj.Cirrus.Utility.IntegerTrackbarControl _Sender, int _FormerValue)
		{
			RebuildMaterialsListView();
		}

		private void integerTrackbarControlErrorLevel_ValueChanged(Nuaj.Cirrus.Utility.IntegerTrackbarControl _Sender, int _FormerValue)
		{
			RebuildMaterialsListView();
		}

		private void buttonSearch_Click(object sender, EventArgs e)
		{
			RebuildMaterialsListView( textBoxSearchMaterial.Text != "" ? textBoxSearchMaterial.Text : null );
		}

		private void buttonAnalyzeConstantColorTextures_Click(object sender, EventArgs e)
		{
			int	totalTexDiffuseCount = 0;
			int	totalTexNormalCount = 0;
			int	totalTexGlossCount = 0;
			int	totalTexMetalCount = 0;
			int	totalTexEmissiveCount = 0;
			int	totalTexSpecularCount = 0;

			int	totalCstDiffuseCount = 0;
			int	totalCstNormalCount = 0;
			int	totalCstGlossCount = 0;
			int	totalCstMetalCount = 0;
			int	totalCstEmissiveCount = 0;
			int	totalCstSpecularCount = 0;

			int[]			cstMetalTextureCount = new int[6];
			List< Material.Layer.Texture >	metalCstCustomColors = new List< Material.Layer.Texture >();

			foreach ( Material M in m_optimizedMaterials ) {
				for ( int layerIndex=0; layerIndex < M.SafeLayersCount; layerIndex++ ) {
					Material.Layer L = M.m_layers[layerIndex];

					if ( L.m_diffuse != null ) {
						if ( L.m_diffuse.m_constantColorType != Material.Layer.Texture.CONSTANT_COLOR_TYPE.TEXTURE )
							totalCstDiffuseCount++;
						totalTexDiffuseCount++;
					}
					if ( L.m_normal != null ) {
						if ( L.m_normal.m_constantColorType != Material.Layer.Texture.CONSTANT_COLOR_TYPE.TEXTURE )
							totalCstNormalCount++;
						totalTexNormalCount++;
					}
					if ( L.m_gloss != null ) {
						if ( L.m_gloss.m_constantColorType != Material.Layer.Texture.CONSTANT_COLOR_TYPE.TEXTURE )
							totalCstGlossCount++;
						totalTexGlossCount++;
					}
					if ( L.m_metal != null ) {
						if ( L.m_metal.m_constantColorType != Material.Layer.Texture.CONSTANT_COLOR_TYPE.TEXTURE ) {
							totalCstMetalCount++;
							cstMetalTextureCount[(int) L.m_metal.m_constantColorType-1]++;
							if ( L.m_metal.m_constantColorType == Material.Layer.Texture.CONSTANT_COLOR_TYPE.CUSTOM ) {
								bool	found = false;
								foreach ( Material.Layer.Texture existingCustomColor in metalCstCustomColors )
									if ( existingCustomColor == L.m_metal ) {
										existingCustomColor.m_dummyCounter++;
										found = true;
										break;
									}
								if ( !found ) {
									metalCstCustomColors.Add( L.m_metal );
									L.m_metal.m_dummyCounter = 1;
								}
							}
						}
						totalTexMetalCount++;
					}
					if ( L.m_emissive != null ) {
						if ( L.m_emissive.m_constantColorType != Material.Layer.Texture.CONSTANT_COLOR_TYPE.TEXTURE )
							totalCstEmissiveCount++;
						totalTexEmissiveCount++;
					}
					if ( L.m_specular != null ) {
						if ( L.m_specular.m_constantColorType != Material.Layer.Texture.CONSTANT_COLOR_TYPE.TEXTURE )
							totalCstSpecularCount++;
						totalTexSpecularCount++;
					}
				}
			}

			LogLine( "" );
			LogLine( " • Diffuse constant colors count = " + totalCstDiffuseCount + " out of " + totalTexDiffuseCount + " textures (" + (100.0f * totalCstDiffuseCount / totalTexDiffuseCount) + "%)" );
			LogLine( " • Normal constant colors count = " + totalCstNormalCount + " out of " + totalTexNormalCount + " textures (" + (100.0f * totalCstNormalCount / totalTexNormalCount) + "%)" );
			LogLine( " • Gloss constant colors count = " + totalCstGlossCount + " out of " + totalTexGlossCount + " textures (" + (100.0f * totalCstGlossCount / totalTexGlossCount) + "%)" );
			LogLine( " • Metal constant colors count = " + totalCstMetalCount + " out of " + totalTexMetalCount + " textures (" + (100.0f * totalCstMetalCount / totalTexMetalCount) + "%)" );
			LogLine( " • Emissive constant colors count = " + totalCstEmissiveCount + " out of " + totalTexEmissiveCount + " textures (" + (100.0f * totalCstEmissiveCount / totalTexEmissiveCount) + "%)" );
			LogLine( " • Specular constant colors count = " + totalCstSpecularCount + " out of " + totalTexSpecularCount + " textures (" + (100.0f * totalCstSpecularCount / totalTexSpecularCount) + "%)" );

			string[]	cstMetalTextureCategoryNames = new string[]
			{
				"DEFAULT",
				"BLACK",
				"BLACK_ALPHA_WHITE",
				"WHITE",
				"INVALID",		// <= Used for replacement when diffuse textures are missing (creates a lovely RED)
				"CUSTOM",
			};
			LogLine( "" );
			for ( int categoryIndex=0; categoryIndex < cstMetalTextureCategoryNames.Length; categoryIndex++ ) {
				LogLine( "  > Metal constant color for category \"" + cstMetalTextureCategoryNames[categoryIndex] + "\" count = " + cstMetalTextureCount[categoryIndex] );
			}
			LogLine( "" );
			LogLine( "• Different metal custom colors encountered:" );
			foreach ( Material.Layer.Texture T in metalCstCustomColors )
				LogLine( "  > { " + T.m_customConstantColor.x + ", " + T.m_customConstantColor.y + ", " + T.m_customConstantColor.z + ", " + T.m_customConstantColor.w + " } referenced " + T.m_dummyCounter + " times" );
		}

		#endregion
	}
}
