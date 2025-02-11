﻿//////////////////////////////////////////////////////////////////////////
// Builds an AO map from a height maps
//
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

using SharpMath;

namespace TestGroundTruthAOFitting
{
	public partial class GeneratorForm : Form {

		#region CONSTANTS

		private const uint		MAX_THREADS = 1024;			// Maximum threads run by the compute shader

		private const int		BILATERAL_PROGRESS = 50;	// Bilateral filtering is considered as this % of the total task (bilateral is quite long so I decided it was equivalent to 50% of the complete computation task)
		private const uint		MAX_LINES = 16;				// Process at most that amount of lines of a 4096x4096 image for a single dispatch

		private const uint		MAX_BOUNCE = 20;			// Maximum amount of bounces to compute

const float		ALBEDO = 1.0f;
const string	SUFFIX = "";
// const float		ALBEDO = 0.5f;
// const string	SUFFIX = " - rho=0.5";

		#endregion

		#region NESTED TYPES

		[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
		private struct	CB_AO {
			public uint		_dimensionsX;
			public uint		_dimensionsY;
			public uint		Y0;					// Index of the texture line we're processing
			public uint		_raysCount;			// Amount of rays in the structured buffer
			public uint		_maxStepsCount;		// Maximum amount of steps to take before stopping
			public uint		_tile;				// Tiling flag
			public float	_texelSize_mm;		// Size of a texel (in millimeters)
			public float	_displacement_mm;	// Max displacement value encoded by the height map (in millimeters)
		}

		[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
		private struct	CB_Indirect {
			public uint		_dimensionsX;
			public uint		_dimensionsY;
			public uint		_raysCount;			// Amount of rays in the structured buffer
			public float	_texelSize_mm;		// Size of a texel (in millimeters)
			public float	_displacement_mm;	// Max displacement value encoded by the height map (in millimeters)
		}

		[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
		private struct	CB_GroundTruth {
			public uint		_dimensionsX;
			public uint		_dimensionsY;
			public uint		Y0;					// Index of the texture line we're processing
			public uint		_raysCount;			// Amount of rays in the structured buffer

			public float	_texelSize_mm;		// Size of a texel (in millimeters)
			public float	_displacement_mm;	// Max displacement value encoded by the height map (in millimeters)
			float			__PAD0;
			float			__PAD1;

			public float3	_rho;				// Reflectance
			float			__PAD2;
		}

		[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
		private struct	CBFilter {
			public UInt32	Y0;					// Index of the texture line we're processing
// 			public float	Radius;				// Radius of the bilateral filter
// 			public float	Tolerance;			// Range tolerance of the bilateral filter
			public float	Sigma_Radius;		// Radius of the bilateral filter
			public float	Sigma_Tolerance;	// Range tolerance of the bilateral filter
			public UInt32	_tile;				// Tiling flag
		}

		[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
		[System.Diagnostics.DebuggerDisplay( "({sourcePixelIndex&0xFFFF},{sourcePixelIndex>>16}) - ({targetPixelIndex&0xFFFF},{targetPixelIndex>>16})" )]
		private struct	SBPixel {
			public uint		sourcePixelIndex;	// Index of the pixel that issued this entry
			public uint		targetPixelIndex;	// Index of the pixel that is perceived by the source pixel
		}

		#endregion

		#region FIELDS

		private RegistryKey							m_AppKey;
		private string								m_ApplicationPath;

		private System.IO.FileInfo					m_sourceFileName = null;
		private System.IO.FileInfo					m_resultFileName = null;
		private uint								W, H;
		private ImageUtility.ImageFile				m_imageSourceHeight = null;
		private ImageUtility.ImageFile				m_imageSourceNormal = null;
		private ImageUtility.ImageFile				m_imageSourceBentCone = null;

		internal Renderer.Device					m_device = new Renderer.Device();
		internal Renderer.Texture2D					m_textureSourceHeightMap = null;
		internal Renderer.Texture2D					m_textureSourceNormal = null;
		internal Renderer.Texture2D					m_textureSourceBentCone = null;

		internal Renderer.Texture2D					m_textureFilteredHeightMap = null;

		// AO Generation
		private Renderer.ConstantBuffer<CB_AO>		m_CB_AO;
		private Renderer.StructuredBuffer<float3>	m_SB_Rays = null;
		private Renderer.ComputeShader				m_CS_GenerateAOMap = null;

		// Indirect Lighting Computation
		private Renderer.ConstantBuffer<CB_Indirect>m_CB_Indirect;
		private Renderer.ComputeShader				m_CS_ComputeIndirectLighting = null;

		private float2[,]							m_AOValues = null;
		private float[][,]							m_arrayOfIlluminanceValues = new float[1+MAX_BOUNCE][,];

		// Ground Truth Computation
		private Renderer.ConstantBuffer<CB_GroundTruth>	m_CB_GroundTruth;
		private Renderer.ConstantBuffer<DemoForm.CB_SH>	m_CB_SH;
		private Renderer.ComputeShader				m_CS_GenerateGroundTruth_Direct = null;
		private Renderer.ComputeShader				m_CS_GenerateGroundTruth_Indirect = null;

		// Bilateral filtering pre-processing
		private Renderer.ConstantBuffer<CBFilter>	m_CB_Filter;
		private Renderer.ComputeShader				m_CS_BilateralFilter = null;

 		private ImageUtility.ColorProfile			m_profilesRGB = new ImageUtility.ColorProfile( ImageUtility.ColorProfile.STANDARD_PROFILE.sRGB );
		private ImageUtility.ColorProfile			m_profileLinear = new ImageUtility.ColorProfile( ImageUtility.ColorProfile.STANDARD_PROFILE.LINEAR );
		private ImageUtility.ImageFile				m_imageResult = null;

		#endregion

		#region PROPERTIES

		internal float	TextureHeight_mm {
			get { return 10.0f * floatTrackbarControlHeight.Value; }
		}

		internal float	TextureSize_mm {
			get { return 10.0f * floatTrackbarControlPixelDensity.Value; }
		}

		#endregion

		#region METHODS

		public unsafe GeneratorForm() {
			InitializeComponent();

 			m_AppKey = Registry.CurrentUser.CreateSubKey( @"Software\GodComplex\TestGroundTruthAOFitting" );
			m_ApplicationPath = System.IO.Path.GetDirectoryName( Application.ExecutablePath );

			#if DEBUG
				buttonReload.Visible = true;
			#endif

			m_demoForm = new DemoForm( this );
		}

		protected override void  OnLoad(EventArgs e) {
 			base.OnLoad(e);

			try {
				m_device.Init( viewportPanelResult.Handle, false, true );

				// Create our compute shaders
				#if !DEBUG
					using ( Renderer.ScopedForceShadersLoadFromBinary scope = new Renderer.ScopedForceShadersLoadFromBinary() )
				#endif
				{
					m_CS_GenerateGroundTruth_Direct = new Renderer.ComputeShader( m_device, new System.IO.FileInfo( "./Shaders/Demo/ComputeGroundTruth.hlsl" ), "CS_Direct" );
					m_CS_GenerateGroundTruth_Indirect = new Renderer.ComputeShader( m_device, new System.IO.FileInfo( "./Shaders/Demo/ComputeGroundTruth.hlsl" ), "CS_Indirect" );
					m_CS_GenerateAOMap = new Renderer.ComputeShader( m_device, new System.IO.FileInfo( "./Shaders/GenerateAOMap.hlsl" ), "CS" );
					m_CS_ComputeIndirectLighting = new Renderer.ComputeShader( m_device, new System.IO.FileInfo( "./Shaders/ComputeIndirectLighting.hlsl" ), "CS", new Renderer.ShaderMacro( "ALBEDO", ALBEDO.ToString() ) );
					m_CS_BilateralFilter = new Renderer.ComputeShader( m_device, new System.IO.FileInfo( "./Shaders/BilateralFiltering.hlsl" ), "CS" );
				}

				// Create our constant buffers
				m_CB_AO = new Renderer.ConstantBuffer<CB_AO>( m_device, 0 );
				m_CB_Indirect = new Renderer.ConstantBuffer<CB_Indirect>( m_device, 0 );
				m_CB_GroundTruth = new Renderer.ConstantBuffer<CB_GroundTruth>( m_device, 0 );
				m_CB_SH = new Renderer.ConstantBuffer<DemoForm.CB_SH>( m_device, 1 );
				m_CB_Filter = new Renderer.ConstantBuffer<CBFilter>( m_device, 0 );

				// Create our structured buffer containing the rays
				m_SB_Rays = new Renderer.StructuredBuffer<float3>( m_device, MAX_THREADS, true, false );
				integerTrackbarControlRaysCount_SliderDragStop( integerTrackbarControlRaysCount, 0 );

				// Create the default, planar normal map
				clearNormalToolStripMenuItem_Click( null, EventArgs.Empty );

			} catch ( Exception _e ) {
				MessageBox( "Failed to create DX11 device and default shaders:\r\n", _e );
				Close();
			}



LoadHeightMap( new System.IO.FileInfo( GetRegKey( "HeightMapFileName", System.IO.Path.Combine( m_ApplicationPath, "Example.jpg" ) ) ) );
LoadNormalMap( new System.IO.FileInfo( GetRegKey( "NormalMapFileName", System.IO.Path.Combine( m_ApplicationPath, "Example.jpg" ) ) ) );
LoadBentConeMap( new System.IO.FileInfo( GetRegKey( "BentConeMapFileName", System.IO.Path.Combine( m_ApplicationPath, "Example.jpg" ) ) ) );
//Generate();
//buttonComputeIndirect_Click( null, EventArgs.Empty );
//Compile();


		}

		protected override void OnClosing( CancelEventArgs e ) {
			try {
				m_CS_GenerateGroundTruth_Direct.Dispose();
				m_CS_GenerateGroundTruth_Indirect.Dispose();
				m_CS_ComputeIndirectLighting.Dispose();
				m_CS_GenerateAOMap.Dispose();
				m_CS_BilateralFilter.Dispose();

				m_SB_Rays.Dispose();

				m_CB_Filter.Dispose();
				m_CB_GroundTruth.Dispose();
				m_CB_Indirect.Dispose();
				m_CB_AO.Dispose();

				if ( m_textureSourceBentCone != null )
					m_textureSourceBentCone.Dispose();
				if ( m_textureSourceNormal != null )
					m_textureSourceNormal.Dispose();
				if ( m_textureSourceHeightMap != null )
					m_textureSourceHeightMap.Dispose();

				m_device.Dispose();
			} catch ( Exception ) {
			}

			e.Cancel = false;
			base.OnClosing( e );
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose( bool disposing )
		{
			if ( disposing && (components != null) )
			{
				components.Dispose();
			}
			base.Dispose( disposing );
		}

		/// <summary>
		/// Arguments-driven generation
		/// </summary>
		/// 
		public class	BuildArguments {
			public string	heightMapFileName = null;
			public string	normalMapFileName = null;
			public string	AOMapFileName = null;
			public float	textureSize_cm = 100.0f;
			public float	displacementSize_cm = 45.0f;
			public int		raysCount = 1024;
			public int		searchRange = 200;
			public float	coneAngle = 160.0f;
			public bool		tile = true;
			public float	bilateralRadius = 1.0f;
			public float	bilateralTolerance = 0.2f;

		}

		bool	m_silentMode = false;
		public void		Build( BuildArguments _args ) {

			// Setup arguments
			floatTrackbarControlHeight.Value = _args.displacementSize_cm;
			floatTrackbarControlPixelDensity.Value = _args.textureSize_cm;

			integerTrackbarControlRaysCount.Value = _args.raysCount;
			integerTrackbarControlMaxStepsCount.Value = _args.searchRange;
			floatTrackbarControlMaxConeAngle.Value = _args.coneAngle;

			floatTrackbarControlBilateralRadius.Value = _args.bilateralRadius;
			floatTrackbarControlBilateralTolerance.Value = _args.bilateralTolerance;

			checkBoxWrap.Checked = _args.tile;

			m_silentMode = true;

			// Create device, shaders and structures
			OnLoad( EventArgs.Empty );

			// Load height map
			System.IO.FileInfo	HeightMapFileName = new System.IO.FileInfo( _args.heightMapFileName );
			LoadHeightMap( HeightMapFileName );

			// Load normal map
			if ( _args.normalMapFileName != null ) {
				System.IO.FileInfo	NormalMapFileName = new System.IO.FileInfo( _args.normalMapFileName );
				LoadNormalMap( NormalMapFileName );
			}

			// Generate
 			Generate();

			// Save results
			m_imageResult.Save( new System.IO.FileInfo( _args.AOMapFileName ) );

			// Dispose
			CancelEventArgs	onsenfout = new CancelEventArgs();
			OnClosing( (CancelEventArgs) onsenfout );
		}

		private void	LoadHeightMap( System.IO.FileInfo _FileName ) {
			try {
				panelParameters.Enabled = false;

				// Dispose of existing resources
				if ( m_imageSourceHeight != null )
					m_imageSourceHeight.Dispose();
				m_imageSourceHeight = null;

				if ( m_textureFilteredHeightMap != null )
					m_textureFilteredHeightMap.Dispose();
				m_textureFilteredHeightMap = null;
				if ( m_textureSourceHeightMap != null )
					m_textureSourceHeightMap.Dispose();
				m_textureSourceHeightMap = null;

				// Load the source image
				// Assume it's in linear space
				m_sourceFileName = _FileName;
//				m_resultFileName = new System.IO.FileInfo( System.IO.Path.Combine( System.IO.Path.GetDirectoryName( _FileName.FullName ), "Results", System.IO.Path.GetFileNameWithoutExtension( _FileName.FullName ) ) );
				m_resultFileName = new System.IO.FileInfo( System.IO.Path.Combine( "Results", System.IO.Path.GetFileNameWithoutExtension( _FileName.FullName ) ) );	// Store into "Results" directory
				m_imageSourceHeight = new ImageUtility.ImageFile( _FileName );
				outputPanelInputHeightMap.Bitmap = m_imageSourceHeight.AsBitmap;

				m_demoForm.ImageHeight = m_imageSourceHeight;	// Send to demo form

				W = m_imageSourceHeight.Width;
				H = m_imageSourceHeight.Height;

				// Build the source texture
				float4[]	scanline = new float4[W];

				Renderer.PixelsBuffer	SourceHeightMap = new Renderer.PixelsBuffer( W*H*4 );
				using ( System.IO.BinaryWriter Wr = SourceHeightMap.OpenStreamWrite() )
					for ( uint Y=0; Y < H; Y++ ) {
						m_imageSourceHeight.ReadScanline( Y, scanline );
						for ( uint X=0; X < W; X++ ) {
							Wr.Write( scanline[X].y );
						}
					}

				m_textureSourceHeightMap = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.R32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, false, new Renderer.PixelsBuffer[] { SourceHeightMap } );
				m_textureFilteredHeightMap = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.R32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, true, null );

				panelParameters.Enabled = true;
				buttonGenerate.Focus();

			} catch ( Exception _e ) {
				MessageBox( "An error occurred while opening the image:\n\n", _e );
			}
		}

		private void	LoadNormalMap( System.IO.FileInfo _FileName ) {
			try {
				// Dispose of existing resources
				if ( m_imageSourceNormal != null )
					m_imageSourceNormal.Dispose();
				m_imageSourceNormal = null;

				if ( m_textureSourceNormal != null )
					m_textureSourceNormal.Dispose();
				m_textureSourceNormal = null;

				// Load the source image
				// Assume it's in linear space (all normal maps should be in linear space, with the default value being (0.5, 0.5, 1))
				m_imageSourceNormal = new ImageUtility.ImageFile( _FileName );
				imagePanelNormalMap.Bitmap = m_imageSourceNormal.AsBitmap;

				m_demoForm.ImageNormal = m_imageSourceNormal;	// Send to demo form

				uint	W = m_imageSourceNormal.Width;
				uint	H = m_imageSourceNormal.Height;

				// Build the source texture
				float4[]	scanline = new float4[W];

				Renderer.PixelsBuffer	SourceNormalMap = new Renderer.PixelsBuffer( W*H*4*4 );
				using ( System.IO.BinaryWriter Wr = SourceNormalMap.OpenStreamWrite() )
					for ( int Y=0; Y < H; Y++ ) {
						m_imageSourceNormal.ReadScanline( (uint) Y, scanline );
						for ( int X=0; X < W; X++ ) {
							float	Nx = 2.0f * scanline[X].x - 1.0f;
							float	Ny = 1.0f - 2.0f * scanline[X].y;
							float	Nz = 2.0f * scanline[X].z - 1.0f;
							Wr.Write( Nx );
							Wr.Write( Ny );
							Wr.Write( Nz );
							Wr.Write( 1.0f );
						}
					}

				m_textureSourceNormal = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.RGBA32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, false, new Renderer.PixelsBuffer[] { SourceNormalMap } );

			} catch ( Exception _e ) {
				MessageBox( "An error occurred while opening the image:\n\n", _e );
			}
		}

		private void	LoadBentConeMap( System.IO.FileInfo _FileName ) {
			try {
				// Dispose of existing resources
				if ( m_imageSourceBentCone != null )
					m_imageSourceBentCone.Dispose();
				m_imageSourceBentCone = null;

				if ( m_textureSourceBentCone != null )
					m_textureSourceBentCone.Dispose();
				m_textureSourceBentCone = null;

				// Load the source image
				// Assume it's in linear space (all normal maps should be in linear space, with the default value being (0.5, 0.5, 1))
				m_imageSourceBentCone = new ImageUtility.ImageFile( _FileName );
				imagePanelBentCone.Bitmap = m_imageSourceBentCone.AsBitmap;

				m_demoForm.ImageBentCone = m_imageSourceBentCone;	// Send to demo form

			} catch ( Exception _e ) {
				MessageBox( "An error occurred while opening the image:\n\n", _e );
			}
		}

		private void	Generate() {
			try {
				panelParameters.Enabled = false;

				//////////////////////////////////////////////////////////////////////////
				// 1] Apply bilateral filtering to the input texture as a pre-process
				ApplyBilateralFiltering( m_textureSourceHeightMap, m_textureFilteredHeightMap, floatTrackbarControlBilateralRadius.Value, floatTrackbarControlBilateralTolerance.Value, checkBoxWrap.Checked, BILATERAL_PROGRESS );


				//////////////////////////////////////////////////////////////////////////
				// 2] Compute directional occlusion
				Renderer.Texture2D	textureAO = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.RG32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, true, null );

				// Prepare computation parameters
				m_textureFilteredHeightMap.SetCS( 0 );
				m_textureSourceNormal.SetCS( 1 );
				m_SB_Rays.SetInput( 2 );

				// Create the counter & indirect pixel buffers
				uint	raysCount = (uint) Math.Min( MAX_THREADS, integerTrackbarControlRaysCount.Value );
				Renderer.StructuredBuffer<uint>	SB_IndirectPixels = new Renderer.StructuredBuffer<uint>( m_device, MAX_LINES * 1024 * 1024, true );	// A lot!! :'(

				textureAO.SetCSUAV( 0 );
				SB_IndirectPixels.SetOutput( 1 );

				m_CB_AO.m._dimensionsX = W;
				m_CB_AO.m._dimensionsY = H;
				m_CB_AO.m._raysCount = raysCount;
				m_CB_AO.m._maxStepsCount = (uint) integerTrackbarControlMaxStepsCount.Value;
				m_CB_AO.m._tile = (uint) (checkBoxWrap.Checked ? 1 : 0);
				m_CB_AO.m._texelSize_mm = TextureSize_mm / Math.Max( W, H );
				m_CB_AO.m._displacement_mm = TextureHeight_mm;

				// Start
				if ( !m_CS_GenerateAOMap.Use() )
					throw new Exception( "Can't generate self-shadowed bump map as compute shader failed to compile!" );

				uint	h = Math.Max( 1, MAX_LINES*1024 / W );
				uint	callsCount = (uint) Math.Ceiling( (float) H / h );

				uint[]	indirectPixels = new uint[W*H*m_CB_AO.m._raysCount];
				uint	targetOffset = 0;
				for ( uint callIndex=0; callIndex < callsCount; callIndex++ ) {
					uint	Y0 = callIndex * h;
					m_CB_AO.m.Y0 = Y0;
					m_CB_AO.UpdateData();

					m_CS_GenerateAOMap.Dispatch( W, h, 1 );

//*					// Read back and accumulate indirect pixels
					SB_IndirectPixels.Read();

					// Accumulate size of source pixel lists to establish final list offsets
					uint	Y1 = Math.Min( H, Y0+h );
					uint	sourceOffset = 0;
					for ( uint Y=Y0; Y < Y1; Y++ ) {
						for ( uint X=0; X < W; X++ ) {
							for ( uint rayIndex=0; rayIndex < raysCount; rayIndex++ ) {
								uint	v = SB_IndirectPixels.m[sourceOffset++];
// 								if ( v != raysCount * (W*Y+X) + rayIndex )
// 									throw new Exception( "Unexpected index!" );
								indirectPixels[targetOffset++] = v;
							}
						}
					}
//*/

//					m_device.Present( true );

					progressBar.Value = (int) (0.01f * (BILATERAL_PROGRESS + (100-BILATERAL_PROGRESS) * (callIndex+1) / callsCount) * progressBar.Maximum);
//					for ( int a=0; a < 10; a++ )
						Application.DoEvents();
				}

				progressBar.Value = progressBar.Maximum;

				// Compute in a single shot (this is madness!)
// 				m_CB_Input.m.y = 0;
// 				m_CB_Input.UpdateData();
// 				m_CS_GenerateSSBumpMap.Dispatch( W, H, 1 );


				SB_IndirectPixels.Dispose();


//*				//////////////////////////////////////////////////////////////////////////
				// 3] Store raw binary data
				Renderer.Texture2D	textureAO_CPU = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.RG32F, ImageUtility.COMPONENT_FORMAT.AUTO, true, false, null );
				textureAO_CPU.CopyFrom( textureAO );
				textureAO.Dispose();

				m_AOValues = new float2[textureAO_CPU.Width,textureAO_CPU.Height];

// textureAO_CPU.ReadPixels( 0, 0, ( uint X, uint Y, System.IO.BinaryReader R ) => {
// 	float	v = R.ReadSingle();
// 	AOValues[X,Y] = v / Mathf.PI;
// } );


//*
				System.IO.FileInfo	binaryDataFileName = new System.IO.FileInfo( m_resultFileName.FullName + ".indirectMap" );
				using ( System.IO.FileStream S = binaryDataFileName.Create() )
					using ( System.IO.BinaryWriter Wr = new System.IO.BinaryWriter( S ) ) {

						Wr.Write( W );
						Wr.Write( H );
						Wr.Write( raysCount );

						textureAO_CPU.ReadPixels( 0, 0, ( uint X, uint Y, System.IO.BinaryReader R ) => {
							float	AO = R.ReadSingle();
							float	illuminance = R.ReadSingle();
							m_AOValues[X,Y].Set( AO, illuminance );
							Wr.Write( AO );
							Wr.Write( illuminance );
						} );

						for ( uint i=0; i < targetOffset; i++ )
							Wr.Write( indirectPixels[i] );
					}

				m_demoForm.AOValues = m_AOValues;	// Send to demo form

				textureAO_CPU.Dispose();
//*/

//*				//////////////////////////////////////////////////////////////////////////
				// 4] Update the resulting bitmap
				if ( m_imageResult != null )
					m_imageResult.Dispose();
				m_imageResult = new ImageUtility.ImageFile( W, H, ImageUtility.PIXEL_FORMAT.BGRA8, new ImageUtility.ColorProfile( ImageUtility.ColorProfile.STANDARD_PROFILE.sRGB ) );

//				textureAO_CPU.ReadPixels( 0, 0, ( uint X, uint Y, System.IO.BinaryReader R ) => { AOValues[X,Y] = R.ReadSingle(); } );

				m_imageResult.WritePixels( ( uint X, uint Y, ref float4 _color ) => {
					float	v = m_AOValues[X,Y].x / (2.0f * Mathf.PI);	// Show AO
//					float	v = m_AOValues[X,Y].y / Mathf.PI;				// Show illuminance
//v = Mathf.Pow( v, 0.454545f );	// Quick gamma correction to have more precision in the shadows???
					
					_color.Set( v, v, v, 1.0f );
				} );

				// Assign result
				viewportPanelResult.Bitmap = m_imageResult.AsBitmap;

			} catch ( Exception _e ) {
				MessageBox( "An error occurred during generation!\r\n\r\nDetails: ", _e );
			} finally {
				panelParameters.Enabled = true;
			}
		}

		private void	ApplyBilateralFiltering( Renderer.Texture2D _Source, Renderer.Texture2D _Target, float _BilateralRadius, float _BilateralTolerance, bool _Wrap, int _ProgressBarMax ) {
			_Source.SetCS( 0 );
			_Target.SetCSUAV( 0 );

// 			m_CB_Filter.m.Radius = _BilateralRadius;
// 			m_CB_Filter.m.Tolerance = _BilateralTolerance;
			m_CB_Filter.m.Sigma_Radius = (float) (-0.5 * Math.Pow( _BilateralRadius / 3.0f, -2.0 ));
			m_CB_Filter.m.Sigma_Tolerance = _BilateralTolerance > 0.0f ? (float) (-0.5 * Math.Pow( _BilateralTolerance, -2.0 )) : -1e6f;
			m_CB_Filter.m._tile = (uint) (_Wrap ? 1 : 0);

			m_CS_BilateralFilter.Use();

			uint	h = Math.Max( 1, MAX_LINES*1024 / W );
			uint	CallsCount = (uint) Math.Ceiling( (float) H / h );
			for ( uint i=0; i < CallsCount; i++ ) {
				m_CB_Filter.m.Y0 = (UInt32) (i * h);
				m_CB_Filter.UpdateData();

				m_CS_BilateralFilter.Dispatch( W, h, 1 );

				m_device.Present( true );

				progressBar.Value = (int) (0.01f * (0 + _ProgressBarMax * (i+1) / CallsCount) * progressBar.Maximum);
//				for ( int a=0; a < 10; a++ )
					Application.DoEvents();
			}

			// Single gulp (crashes the driver on large images!)
//			m_CS_BilateralFilter.Dispatch( W, H, 1 );

			_Target.RemoveFromLastAssignedSlotUAV();	// So we can use it as input for next stage
		}

		internal static float3[] GenerateRays( int _raysCount, float _maxConeAngle ) {
			Hammersley	hammersley = new Hammersley();
			double[,]	sequence = hammersley.BuildSequence( _raysCount, 2 );
			float3[]	rays = hammersley.MapSequenceToSphere( sequence, 0.5f * _maxConeAngle );
//			float3[]	rays = hammersley.MapSequenceToHemisphere( sequence );

			float	sumCosTheta = 0.0f;
			for ( int rayIndex=0; rayIndex < _raysCount; rayIndex++ ) {
				float3	ray = rays[rayIndex];
				rays[rayIndex].Set( ray.x, -ray.z, ray.y );
				sumCosTheta += ray.y;
			}
			sumCosTheta /= _raysCount;	// Summing cos(theta) yields 1/2 as expected

			return rays;
		}
		private void	GenerateRays( int _raysCount, float _maxConeAngle, Renderer.StructuredBuffer<float3> _target ) {
			_raysCount = Math.Min( (int) MAX_THREADS, _raysCount );

			float3[]	rays = GenerateRays( _raysCount, _maxConeAngle );
			for ( int rayIndex=0; rayIndex < _raysCount; rayIndex++ ) {
				m_SB_Rays.m[rayIndex] = rays[rayIndex];
			}

			_target.Write();
		}

		private void	ComputeIndirectBounces() {
			try {
				panelParameters.Enabled = false;

				if ( m_textureSourceNormal == null )
					throw new Exception( "Need normal texture!" );
				if ( !m_CS_ComputeIndirectLighting.Use() )
					throw new Exception( "Failed to use Indirect Lighting compute shader!" );

				//////////////////////////////////////////////////////////////////////////
				// 1] Load raw binary data
				Renderer.Texture2D				texE0 = null;
				Renderer.StructuredBuffer<uint>	SBIndirectPixelsStack = null;

				uint		raysCount = 0;

				float[,]	AOValues = null;
				uint[]		indirectPixelIndices = null;

				System.IO.FileInfo	binaryDataFileName = new System.IO.FileInfo( m_resultFileName.FullName + ".indirectMap" );
				using ( System.IO.FileStream S = binaryDataFileName.OpenRead() )
					using ( System.IO.BinaryReader R = new System.IO.BinaryReader( S ) ) {

						// 1.1) We start by reading a WxH array of (AO,offset,count) triplets
						uint	tempW = R.ReadUInt32();
						uint	tempH = R.ReadUInt32();
						if ( tempW != W || tempH != H )
							throw new Exception( "Dimensions mismatch!" );

						raysCount = R.ReadUInt32();

						Renderer.PixelsBuffer	contentAO = new Renderer.PixelsBuffer( W*H*4 );

						AOValues = new float[W,H];
						float[,]	illuminanceValues = new float[W,H];
						m_arrayOfIlluminanceValues[0] = illuminanceValues;
						using ( System.IO.BinaryWriter W_E0 = contentAO.OpenStreamWrite() ) {
							for ( uint Y=0; Y < H; Y++ ) {
								for ( uint X=0; X < W; X++ ) {
									float	AO = R.ReadSingle();
									float	illuminance0 = R.ReadSingle();
									AOValues[X,Y] = AO;
									illuminanceValues[X,Y] = illuminance0;
									W_E0.Write( illuminance0 );
								}
							}
						}

						texE0 = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.R32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, false, new Renderer.PixelsBuffer[] { contentAO } );

						// 1.2) Then we read the full list of indirect pixels
						uint	stackSize = tempW * tempH * raysCount;
						indirectPixelIndices = new uint[stackSize];
						SBIndirectPixelsStack = new Renderer.StructuredBuffer<uint>( m_device, stackSize, true );
						for ( uint i=0; i < stackSize; i++ ) {
							uint	v = R.ReadUInt32();
// 							if ( v != i )
// 								throw new Exception( "Unexpected index!" );
							SBIndirectPixelsStack.m[i] = v;
							indirectPixelIndices[i] = v;
						}
						SBIndirectPixelsStack.Write();
					}

				//////////////////////////////////////////////////////////////////////////
				// 2] Compute multiple bounces of indirect lighting
				Renderer.Texture2D	targetIlluminance = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.R32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, true, null );
				Renderer.Texture2D	sourceIlluminance = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.R32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, true, null );

				Renderer.Texture2D	targetIlluminance_CPU = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.R32F, ImageUtility.COMPONENT_FORMAT.AUTO, true, false, null );

				sourceIlluminance.CopyFrom( texE0 );

				texE0.Dispose();

				m_textureSourceHeightMap.SetCS( 1 );
				m_textureSourceNormal.SetCS( 2 );
				SBIndirectPixelsStack.SetInput( 3 );

m_SB_Rays.SetInput( 4 );

				m_CB_Indirect.m._dimensionsX = W;
				m_CB_Indirect.m._dimensionsY = H;
				m_CB_Indirect.m._raysCount = raysCount;
				m_CB_Indirect.m._texelSize_mm = TextureSize_mm / Math.Max( W, H );
				m_CB_Indirect.m._displacement_mm = TextureHeight_mm;
				m_CB_Indirect.UpdateData();

				for ( uint bounceIndex=0; bounceIndex < MAX_BOUNCE; bounceIndex++ ) {

					// Compute a single bounce
					sourceIlluminance.SetCS( 0 );
					targetIlluminance.SetCSUAV( 0 );

					m_CS_ComputeIndirectLighting.Dispatch( W, H, 1 );

					// Read back
					float[,]	illuminanceValues = new float[W,H];
					m_arrayOfIlluminanceValues[1+bounceIndex] = illuminanceValues;
					targetIlluminance_CPU.CopyFrom( targetIlluminance );
					targetIlluminance_CPU.ReadPixels( 0, 0, ( uint X, uint Y, System.IO.BinaryReader _R ) => { illuminanceValues[X,Y] = _R.ReadSingle(); } );

					// Swap source and target illuminance values for next bounce
					targetIlluminance.RemoveFromLastAssignedSlotUAV();
					sourceIlluminance.RemoveFromLastAssignedSlots();

					Renderer.Texture2D	temp = sourceIlluminance;
					sourceIlluminance = targetIlluminance;
					targetIlluminance = temp;
				}

				targetIlluminance_CPU.Dispose();
				targetIlluminance.Dispose();
				sourceIlluminance.Dispose();

				SBIndirectPixelsStack.Dispose();


				//////////////////////////////////////////////////////////////////////////
				// 3] Write resulting data
				System.IO.FileInfo	resultDataFileName = new System.IO.FileInfo( m_resultFileName.FullName + SUFFIX + ".AO" );
				using ( System.IO.FileStream S = resultDataFileName.Create() )
					using ( System.IO.BinaryWriter Wr = new System.IO.BinaryWriter( S ) ) {
						Wr.Write( W );
						Wr.Write( H );
						for ( uint Y=0; Y < H; Y++ )
							for ( uint X=0; X < W; X++ )
								Wr.Write( AOValues[X,Y] );

						Wr.Write( 1+MAX_BOUNCE );
						for ( uint bounceIndex=0; bounceIndex <= MAX_BOUNCE; bounceIndex++ ) {
							float[,]	illuminanceValues = m_arrayOfIlluminanceValues[bounceIndex];
							for ( uint Y=0; Y < H; Y++ )
								for ( uint X=0; X < W; X++ )
									Wr.Write( illuminanceValues[X,Y] );
						}
					}

//				m_demoForm.ArrayOfIlluminanceValues = m_arrayOfIlluminanceValues;				// Send to demo form
				m_demoForm.SetIndirectPixelIndices( W, H, raysCount,  indirectPixelIndices );	// Send to demo form

//*				//////////////////////////////////////////////////////////////////////////
				// 4] Update the resulting bitmap
				integerTrackbarControlBounceIndex_ValueChanged( integerTrackbarControlBounceIndex, integerTrackbarControlBounceIndex.Value );

			} catch ( Exception _e ) {
				MessageBox( "An error occurred during generation!\r\n\r\nDetails: ", _e );
			} finally {
				panelParameters.Enabled = true;
			}
		}

		private void	Compile() {
			try {
				const uint	HISTOGRAM_SIZE = 100;

				//////////////////////////////////////////////////////////////////////////
				// 1] Read resulting data
				float[][]	histograms = null;

				System.IO.FileInfo	resultDataFileName = new System.IO.FileInfo( m_resultFileName.FullName + SUFFIX + ".AO" );
				using ( System.IO.FileStream S = resultDataFileName.OpenRead() )
					using ( System.IO.BinaryReader R = new System.IO.BinaryReader( S ) ) {
						uint	tempW = R.ReadUInt32();
						uint	tempH = R.ReadUInt32();

						// Build AO histogram
						uint[]		targetBinIndex = new uint[tempW*tempH];
						uint[]		histogramAOBinSize = new uint[HISTOGRAM_SIZE];
						float[]		histogramAONormalizer = new float[HISTOGRAM_SIZE];
						{
							float	maxAO = 0.0f;
							for ( uint i=0; i < tempW*tempH; i++ ) {
								float	AO = R.ReadSingle() / (2.0f * Mathf.PI);
								maxAO = Math.Max( maxAO, AO );
								uint	intAO = Math.Min( HISTOGRAM_SIZE-1, (uint) (AO * HISTOGRAM_SIZE) );
								targetBinIndex[i] = intAO;
								histogramAOBinSize[intAO]++;
							}
							for ( uint binIndex=0; binIndex < HISTOGRAM_SIZE; binIndex++ ) {
								if ( histogramAOBinSize[binIndex] > 0 )
									histogramAONormalizer[binIndex] = 1.0f / histogramAOBinSize[binIndex];
								else
									histogramAONormalizer[binIndex] = 0.0f;
							}
						}

						// Read illuminance values
						uint	bouncesCount = R.ReadUInt32();
						histograms = new float[bouncesCount][];

						for ( uint bounceIndex=0; bounceIndex < bouncesCount; bounceIndex++ ) {
							float[]		histogram = new float[HISTOGRAM_SIZE];
							histograms[bounceIndex] = histogram;

							// Accumulate bounce values
							for ( uint i=0; i < tempW*tempH; i++ ) {
								float	bounce = R.ReadSingle();
								uint	binIndex = targetBinIndex[i];
								histogram[binIndex] += bounce;
							}
							// Normalize histogram values to get an average
							for ( uint binIndex=0; binIndex < HISTOGRAM_SIZE; binIndex++ ) {
								histogram[binIndex] *= histogramAONormalizer[binIndex];
							}
						}
					}

				//////////////////////////////////////////////////////////////////////////
				// 2] Save histograms
				for ( uint bounceIndex=0; bounceIndex < histograms.GetLength(0); bounceIndex++ ) {
					System.IO.FileInfo	finalCurveDataFileName = new System.IO.FileInfo( m_resultFileName.FullName + bounceIndex + SUFFIX + ".float" );
					using ( System.IO.FileStream S = finalCurveDataFileName.Create() )
						using ( System.IO.BinaryWriter Wr = new System.IO.BinaryWriter( S ) ) {
							float[]	histogram = histograms[bounceIndex];
							for ( uint binIndex=0; binIndex < histogram.Length; binIndex++ ) {
								Wr.Write( histogram[binIndex] );
							}
						}
				}

				//////////////////////////////////////////////////////////////////////////
				// 3] Plot values
				if ( m_imageResult != null )
					m_imageResult.Dispose();
				m_imageResult = new ImageUtility.ImageFile( W, H, ImageUtility.PIXEL_FORMAT.BGRA8, new ImageUtility.ColorProfile( ImageUtility.ColorProfile.STANDARD_PROFILE.sRGB ) );

				m_imageResult.Clear( float4.One );
				float4[]	colors = new float4[] {
					new float4( 0, 0, 0, 1 ),
					new float4( 1, 0, 0, 1 ),
					new float4( 0, 0.5f, 0, 1 ),
					new float4( 0, 0, 1, 1 ),
				};
				for ( uint bounceIndex=1; bounceIndex < histograms.GetLength(0); bounceIndex++ ) {
					m_imageResult.PlotGraph( colors[(bounceIndex-1) & 3], new float2( 0.0f, 1.0f ), new float2( 0.0f, Mathf.PI ), ( float X ) => {
						float	bounceValue = histograms[bounceIndex][Math.Min( HISTOGRAM_SIZE-1, (uint) (HISTOGRAM_SIZE*X) )];
						return bounceValue;
//						return Mathf.PI * X + bounceValue;
					} );
				}

				// Assign result
				viewportPanelResult.Bitmap = m_imageResult.AsBitmap;
			} catch ( Exception _e ) {
				MessageBox( "Error displaying compiled data: " + _e.Message );
			}
		}

		#region Ground Truth Generation

		float4[][,]	m_images_GroundTruth = null;
		float3		m_groundTruthLastRho = float3.Zero;
		uint[]		m_indirectPixelIndices = null;
		uint		m_raysCount = 0;

		internal float4[][,] GenerateGroundTruth( float3 _rho, float3[] _SH ) {
			if ( m_images_GroundTruth != null && _rho == m_groundTruthLastRho )
				return m_images_GroundTruth;	// Already computed!
			if ( m_textureSourceNormal == null )
				return null;

			const uint	SCANLINES_COUNT = 64;
			const uint	BOUNCES_COUNT = 20;

			try {
 				panelParameters.Enabled = false;

				// Upload environment SH
				m_CB_SH.m._SH0.Set( _SH[0], 0 );
				m_CB_SH.m._SH1.Set( _SH[1], 0 );
				m_CB_SH.m._SH2.Set( _SH[2], 0 );
				m_CB_SH.m._SH3.Set( _SH[3], 0 );
				m_CB_SH.m._SH4.Set( _SH[4], 0 );
				m_CB_SH.m._SH5.Set( _SH[5], 0 );
				m_CB_SH.m._SH6.Set( _SH[6], 0 );
				m_CB_SH.m._SH7.Set( _SH[7], 0 );
				m_CB_SH.m._SH8.Set( _SH[8], 0 );
				m_CB_SH.UpdateData();

				//////////////////////////////////////////////////////////////////////////
				// 1] Load raw binary data
				if ( m_indirectPixelIndices == null ) {
					System.IO.FileInfo	binaryDataFileName = new System.IO.FileInfo( m_resultFileName.FullName + ".indirectMap" );
					using ( System.IO.FileStream S = binaryDataFileName.OpenRead() )
						using ( System.IO.BinaryReader R = new System.IO.BinaryReader( S ) ) {

							// 1.1) We start by reading a WxH array of (AO,offset,count) triplets
							uint	tempW = R.ReadUInt32();
							uint	tempH = R.ReadUInt32();
							if ( tempW != W || tempH != H )
								throw new Exception( "Dimensions mismatch!" );

							m_raysCount = R.ReadUInt32();

							Renderer.PixelsBuffer	contentAO = new Renderer.PixelsBuffer( W*H*4 );

							float[,]	AOValues = new float[W,H];
							float[,]	illuminanceValues = new float[W,H];
							for ( uint Y=0; Y < H; Y++ ) {
								for ( uint X=0; X < W; X++ ) {
									float	AO = R.ReadSingle();
									float	illuminance0 = R.ReadSingle();
									AOValues[X,Y] = AO;
									illuminanceValues[X,Y] = illuminance0;
								}
							}

							// 1.2) Then we read the full list of indirect pixels
							uint	stackSize = tempW * tempH * m_raysCount;
							m_indirectPixelIndices = new uint[stackSize];
							for ( uint i=0; i < stackSize; i++ ) {
								uint	v = R.ReadUInt32();
								m_indirectPixelIndices[i] = v;
							}
						}
				}

				//////////////////////////////////////////////////////////////////////////
				// 2] Generate ground truth
				Renderer.StructuredBuffer<uint>	SBIndirectPixelsStack = new Renderer.StructuredBuffer<uint>( m_device, W * SCANLINES_COUNT * m_raysCount, true );
				Renderer.Texture2D	tex_irradiance0 = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.RGBA32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, true, null );
				Renderer.Texture2D	tex_irradiance1 = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.RGBA32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, true, null );
				Renderer.Texture2D	tex_groundTruth_CPU = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.RGBA32F, ImageUtility.COMPONENT_FORMAT.AUTO, true, true, null );

				m_CB_GroundTruth.m._dimensionsX = W;
				m_CB_GroundTruth.m._dimensionsY = H;
				m_CB_GroundTruth.m._raysCount = m_raysCount;
				m_CB_GroundTruth.m._texelSize_mm = TextureSize_mm / Math.Max( W, H );
				m_CB_GroundTruth.m._displacement_mm = TextureHeight_mm;
				m_CB_GroundTruth.m._rho = _rho;

				m_textureSourceNormal.SetCS( 2 );
				SBIndirectPixelsStack.SetInput( 3 );
				m_SB_Rays.SetInput( 4 );

				m_images_GroundTruth = new float4[1+BOUNCES_COUNT][,];


				// ===============================================================
				// 2.1] Generate direct irradiance
				if ( !m_CS_GenerateGroundTruth_Direct.Use() )
					throw new Exception( "Failed to use Ground Truth compute shader!" );

				tex_irradiance0.SetCSUAV( 0 );

				uint	batchesCountY = (H + SCANLINES_COUNT-1) / SCANLINES_COUNT;
				for ( uint batchIndex=0; batchIndex < batchesCountY; batchIndex++ ) {

					// 2.1.1) Prepare scanline data
					uint	Y0 = batchIndex * SCANLINES_COUNT;
					m_CB_GroundTruth.m.Y0 = Y0;
					m_CB_GroundTruth.UpdateData();

					Array.Copy( m_indirectPixelIndices, W*m_raysCount * SCANLINES_COUNT * batchIndex, SBIndirectPixelsStack.m, 0, Math.Min( H - SCANLINES_COUNT * batchIndex, SCANLINES_COUNT ) * W*m_raysCount );
					SBIndirectPixelsStack.Write();

					// 2.1.2) Render batch
					m_CS_GenerateGroundTruth_Direct.Dispatch( W, SCANLINES_COUNT, 1 );

					progressBar.Value = (int) (0.01f * (batchIndex+1) / (BOUNCES_COUNT*batchesCountY) * progressBar.Maximum);
					Application.DoEvents();
				}

				tex_irradiance0.RemoveFromLastAssignedSlotUAV();

				// Copy to CPU
				tex_groundTruth_CPU.CopyFrom( tex_irradiance0 );
				float4[,]	imageSlice = new float4[W,H];
				m_images_GroundTruth[0] = imageSlice;
				tex_groundTruth_CPU.ReadPixels( 0, 0, ( uint X, uint Y, System.IO.BinaryReader _R ) => {
					imageSlice[X,Y].Set( _R.ReadSingle(), _R.ReadSingle(), _R.ReadSingle(), _R.ReadSingle() );
				} );


//*				// ===============================================================
				// 2.2] Generate new bounces
				if ( !m_CS_GenerateGroundTruth_Indirect.Use() )
					throw new Exception( "Failed to use Ground Truth compute shader!" );

				for ( uint bounceIndex=1; bounceIndex <= BOUNCES_COUNT; bounceIndex++ ) {

					tex_irradiance0.SetCS( 0 );
					tex_irradiance1.SetCSUAV( 0 );

					for ( uint batchIndex=0; batchIndex < batchesCountY; batchIndex++ ) {

						// 2.2.1) Prepare scanline data
						uint	Y0 = batchIndex * SCANLINES_COUNT;
						m_CB_GroundTruth.m.Y0 = Y0;
						m_CB_GroundTruth.UpdateData();

						Array.Copy( m_indirectPixelIndices, W*m_raysCount * SCANLINES_COUNT * batchIndex, SBIndirectPixelsStack.m, 0, Math.Min( H - SCANLINES_COUNT * batchIndex, SCANLINES_COUNT ) * W*m_raysCount );
						SBIndirectPixelsStack.Write();

						// 2.2.2) Render batch
						m_CS_GenerateGroundTruth_Indirect.Dispatch( W, SCANLINES_COUNT, 1 );

						progressBar.Value = (int) (0.01f * (bounceIndex+(batchIndex+1)) / (BOUNCES_COUNT*batchesCountY) * progressBar.Maximum);
						Application.DoEvents();
					}

					tex_irradiance1.RemoveFromLastAssignedSlotUAV();
					tex_irradiance0.RemoveFromLastAssignedSlots();

					// Swap
					Renderer.Texture2D	temp = tex_irradiance0;
					tex_irradiance0 = tex_irradiance1;
					tex_irradiance1 = temp;

					// Copy to CPU
					tex_groundTruth_CPU.CopyFrom( tex_irradiance0 );
					imageSlice = new float4[W,H];
					m_images_GroundTruth[bounceIndex] = imageSlice;
					tex_groundTruth_CPU.ReadPixels( 0, 0, ( uint X, uint Y, System.IO.BinaryReader _R ) => {
						imageSlice[X,Y].Set( _R.ReadSingle(), _R.ReadSingle(), _R.ReadSingle(), _R.ReadSingle() );
					} );
				}
//*/
				progressBar.Value = progressBar.Maximum;

				SBIndirectPixelsStack.Dispose();

				tex_irradiance1.Dispose();
				tex_irradiance0.Dispose();
				tex_groundTruth_CPU.Dispose();

/*				//////////////////////////////////////////////////////////////////////////
				// 3] Store raw binary data
				Renderer.Texture2D	textureAO_CPU = new Renderer.Texture2D( m_device, W, H, 1, 1, ImageUtility.PIXEL_FORMAT.RG32F, ImageUtility.COMPONENT_FORMAT.AUTO, true, false, null );
				textureAO_CPU.CopyFrom( textureAO );
				textureAO.Dispose();

				m_AOValues = new float2[textureAO_CPU.Width,textureAO_CPU.Height];

/*				//////////////////////////////////////////////////////////////////////////
				// 4] Update the resulting bitmap
				if ( m_imageResult != null )
					m_imageResult.Dispose();
				m_imageResult = new ImageUtility.ImageFile( W, H, ImageUtility.PIXEL_FORMAT.BGRA8, new ImageUtility.ColorProfile( ImageUtility.ColorProfile.STANDARD_PROFILE.sRGB ) );

//				textureAO_CPU.ReadPixels( 0, 0, ( uint X, uint Y, System.IO.BinaryReader R ) => { AOValues[X,Y] = R.ReadSingle(); } );

				m_imageResult.WritePixels( ( uint X, uint Y, ref float4 _color ) => {
					float	v = m_AOValues[X,Y].x / (2.0f * Mathf.PI);	// Show AO
//					float	v = m_AOValues[X,Y].y / Mathf.PI;				// Show illuminance
//v = Mathf.Pow( v, 0.454545f );	// Quick gamma correction to have more precision in the shadows???
					
					_color.Set( v, v, v, 1.0f );
				} );

				// Assign result
				viewportPanelResult.Bitmap = m_imageResult.AsBitmap;
*/

				m_groundTruthLastRho = _rho;

			} catch ( Exception _e ) {
				MessageBox( "An error occurred during generation!\r\n\r\nDetails: ", _e );
			} finally {
				panelParameters.Enabled = true;
			}

			return m_images_GroundTruth;
		}

		#endregion

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
		private DialogResult	MessageBox( string _Text, Exception _e )
		{
			return MessageBox( _Text + _e.Message, MessageBoxButtons.OK, MessageBoxIcon.Error );
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
			if ( m_silentMode )
				throw new Exception( _Text );

			return System.Windows.Forms.MessageBox.Show( this, _Text, "Ambient Occlusion Map Generator", _Buttons, _Icon );
		}

		#endregion 

		#endregion

		#region EVENT HANDLERS

 		private unsafe void buttonGenerate_Click( object sender, EventArgs e ) {
 			Generate();
//			Generate_CPU( integerTrackbarControlRaysCount.Value );
		}

		private void buttonComputeIndirect_Click( object sender, EventArgs e ) {
			ComputeIndirectBounces();
		}

		private void buttonTestBilateral_Click( object sender, EventArgs e ) {
			Compile();
		}

		private void integerTrackbarControlRaysCount_SliderDragStop( UIUtility.IntegerTrackbarControl _Sender, int _StartValue ) {
			GenerateRays( _Sender.Value, floatTrackbarControlMaxConeAngle.Value * (float) (Math.PI / 180.0), m_SB_Rays );
		}

		private void floatTrackbarControlMaxConeAngle_SliderDragStop( UIUtility.FloatTrackbarControl _Sender, float _fStartValue ) {
			GenerateRays( integerTrackbarControlRaysCount.Value, floatTrackbarControlMaxConeAngle.Value * (float) (Math.PI / 180.0), m_SB_Rays );
		}

		private void integerTrackbarControlBounceIndex_ValueChanged( UIUtility.IntegerTrackbarControl _Sender, int _FormerValue ) {
			if ( m_textureFilteredHeightMap == null ) {
				viewportPanelResult.Bitmap = null;
				return;
			}
			if ( m_imageResult != null )
				m_imageResult.Dispose();
			m_imageResult = new ImageUtility.ImageFile( W, H, ImageUtility.PIXEL_FORMAT.BGRA8, new ImageUtility.ColorProfile( ImageUtility.ColorProfile.STANDARD_PROFILE.sRGB ) );

			float[,]	illuminanceValues = m_arrayOfIlluminanceValues[_Sender.Value];
			if ( illuminanceValues == null )
				return;	// Not available yet
			m_imageResult.WritePixels( ( uint X, uint Y, ref float4 _color ) => {
				float	v = illuminanceValues[X,Y] / Mathf.PI;
//float	v = arrayOfIlluminanceValues[0][X,Y] / (2.0f * Mathf.PI);
//v = Mathf.Pow( v, 0.454545f );	// Quick gamma correction to have more precision in the shadows???
					
				_color.Set( v, v, v, 1.0f );
			} );

			// Assign result
			viewportPanelResult.Bitmap = m_imageResult.AsBitmap;
		}

		private unsafe void viewportPanelResult_Click( object sender, EventArgs e ) {
			if ( m_imageResult == null ) {
				MessageBox( "There is no result image to save!" );
				return;
			}

			string	SourceFileName = m_sourceFileName.FullName;
			string	TargetFileName = System.IO.Path.Combine( System.IO.Path.GetDirectoryName( SourceFileName ), System.IO.Path.GetFileNameWithoutExtension( SourceFileName ) + "_ao.png" );

			saveFileDialogImage.InitialDirectory = System.IO.Path.GetDirectoryName( TargetFileName );
			saveFileDialogImage.FileName = System.IO.Path.GetFileName( TargetFileName );
			if ( saveFileDialogImage.ShowDialog( this ) != DialogResult.OK )
				return;

			try {
				m_imageResult.Save( new System.IO.FileInfo( saveFileDialogImage.FileName ), ImageUtility.ImageFile.FILE_FORMAT.PNG );

				MessageBox( "Success!", MessageBoxButtons.OK, MessageBoxIcon.Information );
			}
			catch ( Exception _e )
			{
				MessageBox( "An error occurred while saving the image:\n\n", _e );
			}
		}

		#region Height Map

		private void outputPanelInputHeightMap_Click( object sender, EventArgs e ) {
			string	oldFileName = GetRegKey( "HeightMapFileName", System.IO.Path.Combine( m_ApplicationPath, "Example.jpg" ) );
			openFileDialogImage.InitialDirectory = System.IO.Path.GetDirectoryName( oldFileName );
			openFileDialogImage.FileName = System.IO.Path.GetFileName( oldFileName );
			if ( openFileDialogImage.ShowDialog( this ) != DialogResult.OK )
				return;

			SetRegKey( "HeightMapFileName", openFileDialogImage.FileName );

			LoadHeightMap( new System.IO.FileInfo( openFileDialogImage.FileName ) );
		}

		private string	m_draggedFileName = null;
		private void outputPanelInputHeightMap_DragEnter( object sender, DragEventArgs e ) {
			m_draggedFileName = null;
			if ( (e.AllowedEffect & DragDropEffects.Copy) != DragDropEffects.Copy )
				return;

			Array	data = ((IDataObject) e.Data).GetData( "FileNameW" ) as Array;
			if ( data == null || data.Length != 1 )
				return;
			if ( !(data.GetValue(0) is String) )
				return;

			string	DraggedFileName = (data as string[])[0];

			if ( ImageUtility.ImageFile.GetFileTypeFromFileNameOnly( DraggedFileName ) != ImageUtility.ImageFile.FILE_FORMAT.UNKNOWN ) {
				m_draggedFileName = DraggedFileName;	// Supported!
				e.Effect = DragDropEffects.Copy;
			}
		}

		private void outputPanelInputHeightMap_DragDrop( object sender, DragEventArgs e ) {
			if ( m_draggedFileName != null )
				LoadHeightMap( new System.IO.FileInfo( m_draggedFileName ) );
		}

		#endregion

		#region Normal Map

		private void outputPanelInputNormalMap_Click( object sender, EventArgs e ) {
			string	oldFileName = GetRegKey( "NormalMapFileName", System.IO.Path.Combine( m_ApplicationPath, "Example.jpg" ) );
			openFileDialogImage.InitialDirectory = System.IO.Path.GetDirectoryName( oldFileName );
			openFileDialogImage.FileName = System.IO.Path.GetFileName( oldFileName );
			if ( openFileDialogImage.ShowDialog( this ) != DialogResult.OK )
				return;

			SetRegKey( "NormalMapFileName", openFileDialogImage.FileName );

			LoadNormalMap( new System.IO.FileInfo( openFileDialogImage.FileName ) );
		}

		private void outputPanelInputNormalMap_DragEnter( object sender, DragEventArgs e ) {
			m_draggedFileName = null;
			if ( (e.AllowedEffect & DragDropEffects.Copy) != DragDropEffects.Copy )
				return;

			Array	data = ((IDataObject) e.Data).GetData( "FileNameW" ) as Array;
			if ( data == null || data.Length != 1 )
				return;
			if ( !(data.GetValue(0) is String) )
				return;

			string	DraggedFileName = (data as string[])[0];

			if ( ImageUtility.ImageFile.GetFileTypeFromFileNameOnly( DraggedFileName ) != ImageUtility.ImageFile.FILE_FORMAT.UNKNOWN ) {
				m_draggedFileName = DraggedFileName;	// Supported!
				e.Effect = DragDropEffects.Copy;
			}
		}

		private void outputPanelInputNormalMap_DragDrop( object sender, DragEventArgs e ) {
			if ( m_draggedFileName != null )
				LoadNormalMap( new System.IO.FileInfo( m_draggedFileName ) );
		}

		private void clearNormalToolStripMenuItem_Click( object sender, EventArgs e ) {
			if ( m_textureSourceNormal != null )
				m_textureSourceNormal.Dispose();
			m_textureSourceNormal = null;
			imagePanelNormalMap.Bitmap = null;

			// Create the default, planar normal map
			Renderer.PixelsBuffer	SourceNormalMap = new Renderer.PixelsBuffer( 4*4 );
			using ( System.IO.BinaryWriter Wr = SourceNormalMap.OpenStreamWrite() ) {
				Wr.Write( 0.0f );
				Wr.Write( 0.0f );
				Wr.Write( 1.0f );
				Wr.Write( 1.0f );
			}

			m_textureSourceNormal = new Renderer.Texture2D( m_device, 1, 1, 1, 1, ImageUtility.PIXEL_FORMAT.RGBA32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, false, new Renderer.PixelsBuffer[] { SourceNormalMap } );
		}

		#endregion

		#region Bent Cone Map

		private void outputPanelInputBentCone_Click( object sender, EventArgs e ) {
			string	oldFileName = GetRegKey( "BentConeMapFileName", System.IO.Path.Combine( m_ApplicationPath, "Example.jpg" ) );
			openFileDialogImage.InitialDirectory = System.IO.Path.GetDirectoryName( oldFileName );
			openFileDialogImage.FileName = System.IO.Path.GetFileName( oldFileName );
			if ( openFileDialogImage.ShowDialog( this ) != DialogResult.OK )
				return;

			SetRegKey( "BentConeMapFileName", openFileDialogImage.FileName );

			LoadBentConeMap( new System.IO.FileInfo( openFileDialogImage.FileName ) );
		}

		private void outputPanelInputBentCone_DragEnter( object sender, DragEventArgs e ) {
			m_draggedFileName = null;
			if ( (e.AllowedEffect & DragDropEffects.Copy) != DragDropEffects.Copy )
				return;

			Array	data = ((IDataObject) e.Data).GetData( "FileNameW" ) as Array;
			if ( data == null || data.Length != 1 )
				return;
			if ( !(data.GetValue(0) is String) )
				return;

			string	draggedFileName = (data as string[])[0];

			if ( ImageUtility.ImageFile.GetFileTypeFromFileNameOnly( draggedFileName ) != ImageUtility.ImageFile.FILE_FORMAT.UNKNOWN ) {
				m_draggedFileName = draggedFileName;	// Supported!
				e.Effect = DragDropEffects.Copy;
			}
		}

		private void outputPanelInputBentCone_DragDrop( object sender, DragEventArgs e ) {
			if ( m_draggedFileName != null )
				LoadBentConeMap( new System.IO.FileInfo( m_draggedFileName ) );
		}

		private void clearBentConeToolStripMenuItem_Click( object sender, EventArgs e ) {
			if ( m_textureSourceNormal != null )
				m_textureSourceNormal.Dispose();
			m_textureSourceNormal = null;
			imagePanelNormalMap.Bitmap = null;

			// Create the default, planar normal map
			Renderer.PixelsBuffer	SourceNormalMap = new Renderer.PixelsBuffer( 4*4 );
			using ( System.IO.BinaryWriter Wr = SourceNormalMap.OpenStreamWrite() ) {
				Wr.Write( 0.0f );
				Wr.Write( 0.0f );
				Wr.Write( 1.0f );
				Wr.Write( 1.0f );
			}

			m_textureSourceNormal = new Renderer.Texture2D( m_device, 1, 1, 1, 1, ImageUtility.PIXEL_FORMAT.RGBA32F, ImageUtility.COMPONENT_FORMAT.AUTO, false, false, new Renderer.PixelsBuffer[] { SourceNormalMap } );
		}

		#endregion

		private void buttonReload_Click( object sender, EventArgs e ) {
			m_device.ReloadModifiedShaders();
		}

		DemoForm	m_demoForm = null;
		private void buttonDemo_Click( object sender, EventArgs e ) {
			m_demoForm.SetImages( m_imageSourceHeight, m_imageSourceNormal, m_AOValues, m_arrayOfIlluminanceValues );
			m_demoForm.Show( this );
		}

		#endregion
	}
}
