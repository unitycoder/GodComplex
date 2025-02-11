using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TestMSBSDF
{
	[DefaultEvent( "ValueChanged" )]
	public partial class CompletionArrayControl : Panel
	{
		#region CONSTANTS

		#endregion

		#region NESTED TYPES

 		public delegate void	SelectionChangedEventHandler( CompletionArrayControl _Sender );

		#endregion

		#region FIELDS

		protected int				m_dimensionX = -1;
		protected int				m_dimensionY = -1;
		protected int				m_dimensionZ = -1;
		protected float[,,]			m_states = null;
		protected string[,,]		m_texts = null;

		protected int				m_selectedX = 0;
		protected int				m_selectedY = 0;
		protected int				m_selectedZ = 0;

		// Graphics
		protected Pen				m_penGrid = Pens.Black;
		protected Pen				m_penSelection = new Pen( Color.Orange, 2.0f );
		protected SolidBrush		m_brushSuccess = null;
		protected SolidBrush		m_brushFailed = null;

		#endregion

		#region PROPERTIES

		public int				SelectedX {
			get { return m_selectedX; }
			set {
				value = Math.Max( 0, Math.Min( m_dimensionX-1, value ) );
				if ( value == m_selectedX )
					return;

				m_selectedX = value;

				if ( SelectionChanged != null )
					SelectionChanged( this );

				Invalidate();
			}
		}

		public int				SelectedY {
			get { return m_selectedY; }
			set {
				value = Math.Max( 0, Math.Min( m_dimensionY-1, value ) );
				if ( value == m_selectedY )
					return;

				m_selectedY = value;

				if ( SelectionChanged != null )
					SelectionChanged( this );

				Invalidate();
			}
		}

		public int				SelectedZ {
			get { return m_selectedZ; }
			set {
				value = Math.Max( 0, Math.Min( m_dimensionZ-1, value ) );
				if ( value == m_selectedZ )
					return;

				m_selectedZ = value;

				if ( SelectionChanged != null )
					SelectionChanged( this );

				Invalidate();
			}
		}

		public float			SelectedState {
			get { return m_states[m_selectedX, m_selectedY, m_selectedZ]; }
			set {
				m_states[m_selectedX, m_selectedY, m_selectedZ] = value;
				Invalidate();
			}
		}

		public string			SelectedText {
			get { return m_texts[m_selectedX, m_selectedY, m_selectedZ]; }
			set {
				m_texts[m_selectedX, m_selectedY, m_selectedZ] = value;
				Invalidate();
			}
		}

		public Color			GridColor {
			get { return m_penGrid.Color; }
			set {
				if ( m_penGrid != Pens.Black )
					m_penGrid.Dispose();

				m_penGrid = new Pen( value );
				Invalidate();
			}
		}


// 		[Description( "The maximum value shown by the trackbar" )]
// 		[Category( "Value" )]
// 		[DefaultValue( DEFAULT_VISIBLE_RANGE_MAX )]
// 		public float	VisibleRangeMax
// 		{
// 			get { return m_VisibleRangeMax; }
// 			set
// 			{
// 				m_VisibleRangeMax = Math.Min( m_RangeMax, value );			// Can't go further than allowed range anyway
// 				m_VisibleRangeMax = Math.Max( Value, m_VisibleRangeMax );	// But can't go lower than displayed value either...
// 
// 				if ( !m_bInternalChange )
// 				{	// This means it's an external change (obvsiouly) so we can use the provided value as a default value
// 					m_DefaultVisibleRangeMax = m_VisibleRangeMax;
// 				}
// 
// 				// Update GUI
// 				Invalidate();
// 			}
// 		}

		[Description( "Triggered whenever the selection changes" )]
		[Category( "Value" )]
		public event SelectionChangedEventHandler	SelectionChanged;

		#endregion

		#region METHODS

		public CompletionArrayControl() {
			SetStyle( ControlStyles.Selectable, true );
			SetStyle( ControlStyles.DoubleBuffer, true );
			SetStyle( ControlStyles.AllPaintingInWmPaint, true );
			SetStyle( ControlStyles.UserPaint, true );
			SetStyle( ControlStyles.ResizeRedraw, true );

			InitializeComponent();

			InitializeGraphics();

			// Init
			Init( 10, 4, 1 );
//			if ( DesignMode ) {
				for ( int Y=0; Y < m_dimensionY-2; Y++ )
					for ( int X=0; X < m_dimensionX; X++ )
						m_states[X,Y,0] = 1;			// Done
				for ( int X=4; X < m_dimensionX; X++ )
					m_states[X,m_dimensionY-3,0] = -1;	// Failed
//			}
		}

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose( bool disposing )
		{
			if ( disposing )
			{
				if ( components != null )
					components.Dispose();

				DisposeGraphics();
			}

			base.Dispose( disposing );
		}

		/// <summary>
		/// Initializes the control with the correct dimensions
		/// </summary>
		/// <param name="_dimensionX"></param>
		/// <param name="_dimensionY"></param>
		/// <param name="_dimensionZ"></param>
		public void				Init( int _dimensionX, int _dimensionY, int _dimensionZ ) {
			if ( _dimensionX == m_dimensionX && _dimensionY == m_dimensionY && _dimensionZ == m_dimensionZ )
				return;	// No change

			m_dimensionX = _dimensionX;
			m_dimensionY = _dimensionY;
			m_dimensionZ = _dimensionZ;
			m_states = new float[m_dimensionX,m_dimensionY,m_dimensionZ];
			m_texts = new string[m_dimensionX,m_dimensionY,m_dimensionZ];

			m_selectedX = 0;
			m_selectedY = 0;
			m_selectedZ = 0;

			Invalidate();
		}

		/// <summary>
		/// Gets a state
		/// </summary>
		/// <param name="_X"></param>
		/// <param name="_Y"></param>
		/// <param name="_Z"></param>
		/// <returns></returns>
		public float			GetState( int _X, int _Y, int _Z ) {
			return m_states[_X, _Y, _Z];
		}

		/// <summary>
		/// Gets a state error
		/// </summary>
		/// <param name="_X"></param>
		/// <param name="_Y"></param>
		/// <param name="_Z"></param>
		/// <returns></returns>
		public string			GetText( int _X, int _Y, int _Z ) {
			return m_texts[_X, _Y, _Z];
		}

		/// <summary>
		/// Sets a state
		/// </summary>
		/// <param name="_X"></param>
		/// <param name="_Y"></param>
		/// <param name="_Z"></param>
		/// <param name="_state"></param>
		public void				SetState( int _X, int _Y, int _Z, float _state, string _text ) {
			m_states[_X, _Y, _Z] = _state;
			m_texts[_X, _Y, _Z] = _text;
			Invalidate();
		}

		/// <summary>
		/// Updates selection
		/// </summary>
		/// <param name="_X"></param>
		/// <param name="_Y"></param>
		/// <param name="_Z"></param>
		public void				Select( int _X, int _Y, int _Z ) {
			_X = Math.Max( 0, Math.Min( m_dimensionX-1, _X ) );
			_Y = Math.Max( 0, Math.Min( m_dimensionY-1, _Y ) );
			_Z = Math.Max( 0, Math.Min( m_dimensionZ-1, _Z ) );

			if ( _X == m_selectedX && _Y == m_selectedY && _Z == m_selectedZ )
				return;

			m_selectedX = _X;
			m_selectedY = _Y;
			m_selectedZ = _Z;

			if ( SelectionChanged != null )
				SelectionChanged( this );

			Invalidate();
		}

		public bool				IsPointValid( Point _clientPos ) {
			int	X = _clientPos.X * m_dimensionX / Width;
			if ( X < 0 || X >= m_dimensionX )
				return false;
			int	Y = _clientPos.Y * Math.Max( 8, m_dimensionY ) / Height;
			if ( Y < 0 || Y >= m_dimensionY )
				return false;

			return true;
		}

		public void				SelectAt( Point _clientPos ) {
			Select( _clientPos.X * m_dimensionX / Width, _clientPos.Y * m_dimensionY / Height, m_selectedZ );
		}

		public bool				GetXYAt( Point _clientPos, out int _X, out int _Y ) {
			_X = _clientPos.X * m_dimensionX / Width;
			_Y = _clientPos.Y * Math.Max( 8, m_dimensionY ) / Height;
			if ( _X < 0 || _X >= m_dimensionX )
				return false;
			if ( _Y < 0 || _Y >= m_dimensionY )
				return false;

			return true;
		}

		#region Control Members

		protected override void OnMouseDown( MouseEventArgs e ) {
			Focus();
			base.OnMouseDown( e );
			SelectAt( e.Location );

			if ( e.Button != MouseButtons.Left )
				return;

			// Show tooltip
			int	X, Y;
			if ( !GetXYAt( PointToClient( Control.MousePosition ), out X, out Y ) )
				return;

			float	state = m_states[X,Y,m_selectedZ];
			string	text = m_texts[X,Y,m_selectedZ];

			toolTip1.ToolTipTitle = "(" + X + ", " + Y + ", " + SelectedZ + ")";
			if ( state > 0.0f )
				toolTip1.Show( "Progress " + ((int) (100 * state)).ToString() + "%\r\n" + (text != null ? text : "unknown"), this );
			else
				toolTip1.Show( (text != null ? text : "unknown"), this );

		}

// 		Point		m_lastMousePosition;
// 		DateTime	m_lastStaticTime;
		protected override void OnMouseMove( MouseEventArgs e ) {
			base.OnMouseMove( e );

// 			if ( e.X == m_lastMousePosition.X
// 				&& e.Y == m_lastMousePosition.Y ) {
// 				// Didn't move...
// 				if ( (DateTime.Now - m_lastStaticTime).TotalMilliseconds < 100 )
// 					return;	// Too soon
// 
// 				// Reshow tooltip
// 				int	X, Y;
// 				if ( !GetXYAt( PointToClient( Control.MousePosition ), out X, out Y ) )
// 					return;
// 
// 				float	state = m_states[X,Y,m_selectedZ];
// 				string	text = m_texts[X,Y,m_selectedZ];
// 
// 				toolTip1.ToolTipTitle = "(" + X + ", " + Y + ", " + SelectedZ + ")";
// 				if ( state > 0.0f )
// 					toolTip1.Show( "Progress " + ((int) (100 * state)).ToString() + "%\r\n" + (text != null ? text : "unknown"), this );
// 				else
// 					toolTip1.Show( (text != null ? text : "unknown"), this );
// 			}
// 
// 			m_lastMousePosition = e.Location;
// 			m_lastStaticTime = DateTime.Now;
// 
// 			if (	Math.Abs( e.X - m_mousePositionOnTimer.X ) > 4
// 				||	Math.Abs( e.Y - m_mousePositionOnTimer.Y ) > 4 )
// 				return;	// Mouse moved
// 
// 			// Reshow tooltip
// 			int	X, Y;
// 			if ( !GetXYAt( PointToClient( Control.MousePosition ), out X, out Y ) )
// 				return;
// 
// 			float	state = m_states[X,Y,m_selectedZ];
// 			string	text = m_texts[X,Y,m_selectedZ];
// 
// 			toolTip1.ToolTipTitle = "(" + X + ", " + Y + ", " + SelectedZ + ")";
// 			if ( state > 0.0f )
// 				toolTip1.Show( "Progress " + ((int) (100 * state)).ToString() + "%\r\n" + (text != null ? text : "unknown"), this );
// 			else
// 				toolTip1.Show( (text != null ? text : "unknown"), this );
// 
// 			m_mousePositionOnTimer = Point.Empty;
		}

		protected override void OnMouseHover( EventArgs e ) {
			base.OnMouseHover( e );
		}

		protected override bool IsInputKey( Keys keyData ) {
			switch ( keyData ) {
				case Keys.Right:
				case Keys.Left:
				case Keys.Up:
				case Keys.Down:
					return true;
// 				case Keys.Shift | Keys.Right:
// 				case Keys.Shift | Keys.Left:
// 				case Keys.Shift | Keys.Up:
// 				case Keys.Shift | Keys.Down:
// 					return true;
			}
			return base.IsInputKey( keyData );
		}

 		protected override void OnKeyDown( KeyEventArgs e )
		{
			base.OnKeyDown( e );

			if ( e.KeyCode == Keys.Left && m_selectedX > 0 )
				Select( m_selectedX-1, m_selectedY, m_selectedZ );
			if ( e.KeyCode == Keys.Right && m_selectedX < m_dimensionX-1 )
				Select( m_selectedX+1, m_selectedY, m_selectedZ );
			if ( e.KeyCode == Keys.Up ) {
				if ( m_selectedY > 0 )
					Select( m_selectedX, m_selectedY-1, m_selectedZ );
				else if ( m_selectedZ > 0 )
					Select( m_selectedX, m_dimensionY-1, m_selectedZ-1 );
			}
			if ( e.KeyCode == Keys.Down ) {
				if ( m_selectedY < m_dimensionY-1 )
					Select( m_selectedX, m_selectedY+1, m_selectedZ );
				else if ( m_selectedZ < m_dimensionZ-1 )
					Select( m_selectedX, 0, m_selectedZ+1 );
			}
			if ( e.KeyCode == Keys.PageUp && m_selectedZ > 0 )
				Select( m_selectedX, m_selectedY, m_selectedZ-1 );
			if ( e.KeyCode == Keys.PageDown && m_selectedZ < m_dimensionZ-1 )
				Select( m_selectedX, m_selectedY, m_selectedZ+1 );
		}

		protected override void OnMouseWheel( MouseEventArgs e )
		{
			base.OnMouseWheel( e );

			if ( e.Delta > 0 )
				SelectedZ--;
			else
				SelectedZ++;
		}

		protected override void OnResize( EventArgs eventargs ) {
			base.OnResize( eventargs );
		}

		protected override void OnPaintBackground( PaintEventArgs e ) {
 			base.OnPaintBackground( e );
		}

		protected override void OnPaint( PaintEventArgs e )
		{
			base.OnPaint( e );

			float	rectangleWidth = (float) Width / m_dimensionX;
			float	rectangleHeight = (float) Height / Math.Max( 8, m_dimensionY );

			// Paint the grid
			for ( int X=1; X < m_dimensionX; X++ ) {
				float	x = X * rectangleWidth;
				for ( int Y=0; Y < m_dimensionY; Y++ ) {
					float	y0 = Y * rectangleHeight;
					float	y1 = (Y+1) * rectangleHeight;

					e.Graphics.DrawLine( m_penGrid, x, y0+1, x, y1-1 );
				}
			}

			for ( int Y=1; Y <= m_dimensionY; Y++ ) {
				float	y = Y * rectangleHeight;
				for ( int X=0; X < m_dimensionX; X++ ) {
					float	x0 = X * rectangleWidth;
					float	x1 = (X+1) * rectangleWidth;

					e.Graphics.DrawLine( m_penGrid, x0+1, y, x1-1, y );
				}
			}

			// Paint the rectangles
			float	margin = 2.0f;

			using ( GraphicsPath P = GetRoundedRect( new RectangleF( 0, 0, rectangleWidth - 2*margin-1, rectangleHeight - 2*margin-1 ), 2.0f ) ) {
				for ( int X=0; X < m_dimensionX; X++ ) {
					float	x0 = X * rectangleWidth;
					float	x1 = (X+1) * rectangleWidth;
					for ( int Y=0; Y < m_dimensionY; Y++ ) {
						float	y0 = Y * rectangleHeight;
						float	y1 = (Y+1) * rectangleHeight;

						float	state = m_states[X,Y,m_selectedZ];
						if ( state < 0.0f )
							FillRectangle( e.Graphics, m_brushFailed, x0+margin, y0+margin, x1-margin, y1-margin );
						else {
							// Show progress bar
							state = Math.Min( 1.0f, state );
							FillRectangle( e.Graphics, m_brushSuccess, x0+margin, y0+margin, x0+state*(x1-x0), y1-margin );
						}
					}
				}

				// Paint selection
				float	selectionX = m_selectedX * (float) Width / m_dimensionX;
				float	selectionY = m_selectedY * (float) Height / m_dimensionY;
				e.Graphics.ResetTransform();
				e.Graphics.TranslateTransform( selectionX, selectionY );
				e.Graphics.ScaleTransform( (rectangleWidth+2*margin)/rectangleWidth, (rectangleHeight+2*margin)/rectangleHeight );
				e.Graphics.DrawPath( m_penSelection, P );
			}
		}

		void	FillRectangle( Graphics _g, Brush _brush, float x0, float y0, float x1, float y1 ) {
// 			_g.ResetTransform();
// 			_g.TranslateTransform( x0, y0 );
// 			_g.FillPath( _brush, _path );
			_g.FillRectangle( _brush, x0, y0, x1-x0, y1-y0 );
		}

		#region Rounded Rectangle Path Creation

		// (Quite tedious) code from http://www.codeproject.com/Articles/5649/Extended-Graphics-An-implementation-of-Rounded-Rec

		private GraphicsPath GetRoundedRect( RectangleF baseRect, float radius ) {
			// if the corner radius is greater than or equal to 
			// half the width, or height (whichever is shorter) 
			// then return a capsule instead of a lozange 
			if ( radius >= 0.5f * Math.Min( baseRect.Width, baseRect.Height ) )
				return GetCapsule( baseRect );

			GraphicsPath	path = new GraphicsPath();

			// if corner radius is less than or equal to zero,
			// return the original rectangle 
			if ( radius <= 0.0f ) {
				path.AddRectangle( baseRect ); 
				path.CloseFigure();
				return path;
			}

			// create the arc for the rectangle sides and declare 
			// a graphics path object for the drawing 
			float		diameter = radius * 2.0F;
			SizeF		sizeF = new SizeF( diameter, diameter );
			RectangleF	arc = new RectangleF( baseRect.Location, sizeF );

			// top left arc 
			path.AddArc( arc, 180, 90 );

			// top right arc 
			arc.X = baseRect.Right-diameter;
			path.AddArc( arc, 270, 90 );

			// bottom right arc 
			arc.Y = baseRect.Bottom-diameter;
			path.AddArc( arc, 0, 90 );

			// bottom left arc
			arc.X = baseRect.Left;
			path.AddArc( arc, 90, 90 );

			path.CloseFigure();

			return path; 
		}

		private GraphicsPath GetCapsule( RectangleF baseRect ) { 
			float			diameter; 
			RectangleF		arc; 
			GraphicsPath	path = new GraphicsPath(); 

			if ( baseRect.Width>baseRect.Height ) { 
				// return horizontal capsule 
				diameter = baseRect.Height;
				SizeF sizeF = new SizeF(diameter, diameter);
				arc = new RectangleF( baseRect.Location, sizeF );
				path.AddArc( arc, 90, 180);
				arc.X = baseRect.Right-diameter;
				path.AddArc( arc, 270, 180);
			} else if( baseRect.Width < baseRect.Height ) { 
				// return vertical capsule
				diameter = baseRect.Width;
				SizeF sizeF = new SizeF(diameter, diameter);
				arc = new RectangleF( baseRect.Location, sizeF );
				path.AddArc( arc, 180, 180 );
				arc.Y = baseRect.Bottom-diameter;
				path.AddArc( arc, 0, 180 );
			} 
			else
				path.AddEllipse( baseRect );	// return circle 

			path.CloseFigure();

			return path; 
		} 

		#endregion

		#region Graphics Creation

		protected virtual void	InitializeGraphics()
		{
			m_brushSuccess = new SolidBrush( Color.FromArgb( 40, 160, 40 ) );
			m_brushFailed = new SolidBrush( Color.FromArgb( 200, 60, 60 ) );
		}

		protected virtual void	DisposeGraphics()
		{
			m_brushSuccess.Dispose();
			m_brushFailed.Dispose();
			if ( m_penGrid != Pens.Black )
				m_penGrid.Dispose();
		}

		#endregion

		#endregion

		#endregion

		#region EVENT HANDLERS

		Point	m_mousePositionOnTimer;
		private void timer1_Tick( object sender, EventArgs e ) {
			m_mousePositionOnTimer = PointToClient( Control.MousePosition );
		}

		#endregion
	}
}
