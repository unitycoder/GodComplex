﻿using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShaderInterpreter
{
	public partial class InterpreterForm : Form
	{
		private RegistryKey	m_AppKey;
		private string		m_ApplicationPath;

		public InterpreterForm()
		{
			InitializeComponent();

			m_AppKey = Registry.CurrentUser.CreateSubKey( @"Software\GodComplex\ShaderInterpreter" );
			m_ApplicationPath = Path.GetDirectoryName( Application.ExecutablePath );

convertShaderToolStripMenuItem_Click( null, EventArgs.Empty );
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

		private void	MessageBox( string _Text )
		{
			MessageBox( _Text, MessageBoxButtons.OK );
		}
		private void	MessageBox( string _Text, MessageBoxButtons _Buttons )
		{
			MessageBox( _Text, _Buttons, MessageBoxIcon.Information );
		}
		private void	MessageBox( string _Text, MessageBoxIcon _Icon )
		{
			MessageBox( _Text, MessageBoxButtons.OK, _Icon );
		}
		private void	MessageBox( string _Text, MessageBoxButtons _Buttons, MessageBoxIcon _Icon )
		{
			System.Windows.Forms.MessageBox.Show( this, _Text, "Shader Interpreter", _Buttons, _Icon );
		}

		#endregion

		private void convertShaderToolStripMenuItem_Click( object sender, EventArgs e )
		{
 			string	OldFileName = GetRegKey( "LastShaderFilename", m_ApplicationPath );
			openFileDialogShader.InitialDirectory = Path.GetDirectoryName( OldFileName );
			openFileDialogShader.FileName = Path.GetFileName( OldFileName );
// 			if ( openFileDialogShader.ShowDialog( this ) != DialogResult.OK )
// 				return;
// 			SetRegKey( "LastShaderFilename", openFileDialogShader.FileName );

openFileDialogShader.FileName = @"E:\[Workspaces]\GodComplex\Resources\Shaders\GIRenderCubeMap.hlsl";


			// Perform conversion
			string	CSharpCode = null;
			try
			{
				// Load the shader code
				string		ShaderCode = null;
				FileInfo	ShaderFile = new FileInfo( openFileDialogShader.FileName );
				using ( StreamReader S = ShaderFile.OpenText() )
					ShaderCode = S.ReadToEnd();

				// Perform conversion
				CSharpCode = Converter.ConvertShader( ShaderFile, ShaderCode, "Test", "VS", "PS" );
			}
			catch ( Converter.ConverterException _e )
			{
				SourceErrorForm	F = new SourceErrorForm( "An error occurred while converting shader \"" + openFileDialogShader.FileName + "\" to C# class!\n\n", _e );
				F.ShowDialog( this );
				return;
			}
			catch ( Exception _e )
			{
				MessageBox( "An error occurred while converting shader to C# class!\n\n" + _e.Message );
				return;
			}

			// Save the result
			OldFileName = GetRegKey( "LastCSharpFilename", m_ApplicationPath );
			saveFileDialogShader.InitialDirectory = Path.GetDirectoryName( OldFileName );
			saveFileDialogShader.FileName = Path.GetFileName( OldFileName );
			if ( saveFileDialogShader.ShowDialog( this ) != DialogResult.OK )
				return;
			SetRegKey( "LastCSharpFilename", saveFileDialogShader.FileName );

			FileInfo	CSharpFile = new FileInfo( saveFileDialogShader.FileName );
			using ( StreamWriter S = CSharpFile.CreateText() )
			{
				S.Write( CSharpCode );
			}
		}
	}
}
