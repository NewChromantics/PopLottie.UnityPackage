using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UIElements = UnityEngine.UIElements;

namespace PopLottie
{
	public enum AnimationLineCap
	{
		Round = UIElements.LineCap.Round,
		Butt = UIElements.LineCap.Butt,
		Square = UIElements.LineCap.Butt
	}
	public enum AnimationLineJoin
	{
		Miter = UIElements.LineJoin.Miter,
		Round = UIElements.LineJoin.Round,
		Bevel = UIElements.LineJoin.Bevel
	}
	public enum AnimationFillRule
	{
		NonZero = UIElements.FillRule.NonZero,	//	Solid (OR)
		EvenOdd = UIElements.FillRule.OddEven,	//	overlapping shapes create holes (AND)
	}
	
	public enum TextJustify
	{
		//	gr: note these numbers match lottie's spec, if these ever want to be reused, make it abstract here
		Left = 0,
		Right = 1,
		Center = 2,
		JustifyWithLastLineLeft = 3,
		JustifyWithLastLineRight = 4,
		JustifyWithLastLineCenter = 5,
		JustifyWithLastLineFull = 6,
	}
	
	public struct ShapeStyle
	{
		public Color?				FillColour;
		public AnimationFillRule	FillRule;

		public Color?				StrokeColour;
		public float?				StrokeWidth;
		public AnimationLineCap		StrokeLineCap;
		public AnimationLineJoin	StrokeLineJoin;

		public bool		IsStroked => StrokeColour.HasValue;
		public bool		IsFilled => FillColour.HasValue;
		
		public void		MultiplyAlpha(float Multiplier)
		{
			if ( FillColour is Color fill )
			{
				fill.a *= Multiplier;
				FillColour = fill;
			}

			if ( StrokeColour is Color stroke )
			{
				stroke.a *= Multiplier;
				StrokeColour = stroke;
			}
		}
		
		//	gr: used by unity visual element to tint output
		public void TintColour(Color Tint)
		{
			if ( FillColour is Color fill )
			{
				fill *= Tint;
				FillColour = fill;
			}

			if ( StrokeColour is Color stroke )
			{
				stroke *= Tint;
				StrokeColour = stroke;
			}
		}
	}
	
	public struct AnimationText
	{
		public string		Text;
		public string		FontName;
		public float		FontSize;
		public Vector2		Position;
		public TextJustify	Justify;
		
		public AnimationText(String Text,String FontName,float FontSize,Vector2 Position)
		{
			this.Text = Text;
			this.FontName = FontName;
			this.FontSize = FontSize;
			this.Position = Position;
			this.Justify = TextJustify.Left;
		}
		

	}

	//	Currently optimised for UIToolkit VectorAPI 
	public static class RenderCommands
	{
		public struct BezierPoint
		{
			//	our mapping is for Painter2D which follows around
			//	so first entry is just [Start] of 2nd node
			public Vector2	ControlPointIn;
			public Vector2	ControlPointOut;
			public Vector2	End;
			
			public BezierPoint(Vector2 position)
			{
				this.End = position;
				this.ControlPointIn = position;
				this.ControlPointOut = position;
			}
			
			public BezierPoint(Vector2 End,Vector2 ControlPointIn,Vector2 ControlPointOut)
			{
				this.End = End;
				this.ControlPointIn = ControlPointIn;
				this.ControlPointOut = ControlPointOut;
			}
			
		}
		
		public struct Ellipse
		{
			public Vector2	Center;
			public Vector2	Radius;
		}
		
		
		
		public struct DebugPoint
		{
			public Vector2	Start;
			public Vector2?	End;			//	if true, draw handle here
			public int		Uid;			//	see if we can automatically do this, but different sizes so we see overlaps
			public float	HandleSize => 1.0f + ((float)Uid*0.3f);
			public Color	Colour;
			
			public DebugPoint(Vector2 Position,int Uid,Color Colour,Vector2? End=null)
			{
				this.Start = Position;
				this.Uid = Uid;
				this.Colour = Colour;
				this.End = End;
			}
		}
		
		//	todo: rename to AnimationPath
		public struct Path
		{
			public BezierPoint[]	BezierPath;
			public Vector3[]		LinearPath;
			public Ellipse?			EllipsePath;
			public AnimationText[]	TextPaths;
			public Rect?			Bounds => CalculateBounds();	//	cache this in future
			
			public Path(IEnumerable<BezierPoint> BezierPath)
			{
				this.BezierPath = BezierPath.ToArray();
				this.LinearPath = null;
				this.EllipsePath = null;
				this.TextPaths = null;
			}
			public Path(Ellipse EllipsePath)
			{
				this.BezierPath = null;
				this.LinearPath = null;
				this.EllipsePath = EllipsePath;
				this.TextPaths = null;
			}
			public Path(AnimationText Text)
			{
				this.BezierPath = null;
				this.LinearPath = null;
				this.EllipsePath = null;
				this.TextPaths = new []{Text};
			}
			
			public static Path CreateRect(Vector2 Center,Vector2 Size,float? CornerRadius=null)
			{
				if ( CornerRadius.HasValue && CornerRadius.Value <= float.Epsilon )
					CornerRadius = null;
					
				var l = Center.x - (Size.x/2.0f);
				var r = Center.x + (Size.x/2.0f);
				var t = Center.y - (Size.y/2.0f);
				var b = Center.y + (Size.y/2.0f);
		
				//	slightly more complex...
				if ( CornerRadius is float cornerRadius )
				{
					
				}
				BezierPoint[] GetCornerBeziers(Vector2 Start,Vector2 Corner,Vector2 End)
				{
					//	todo: apply a lerp to the bezier points, they shouldn't go right to the corner
					//	https://nacho4d-nacho4d.blogspot.com/2011/05/bezier-paths-rounded-corners-rectangles.html
					//	magic number is lerp( Pos -> Corner, 0.55)
					var a = new BezierPoint( Start, ControlPointIn: Start, ControlPointOut: Corner );
					var b = new BezierPoint( End:End, ControlPointIn: Corner, ControlPointOut: End );
					return new BezierPoint[]{a,b};
				}
				
				if ( CornerRadius is float radius )
				{
					//	https://nacho4d-nacho4d.blogspot.com/2011/05/bezier-paths-rounded-corners-rectangles.html
					//	make angled corners, then extend the control points... a bit
					//	get inner values
					var lin = l + radius;
					var rin = r - radius;
					var tin = t + radius;
					var bin = b - radius;
					var tl = new Vector2(l,t);
					var tr = new Vector2(r,t);
					var br = new Vector2(r,b);
					var bl = new Vector2(l,b);
					var Points = new List<BezierPoint>();
					Points.AddRange( GetCornerBeziers( new Vector2(l,tin), tl, new Vector2(lin,t) ) );
					Points.AddRange( GetCornerBeziers( new Vector2(rin,t), tr, new Vector2(r,tin) ) );
					Points.AddRange( GetCornerBeziers( new Vector2(r,bin), br, new Vector2(rin,b) ) );
					Points.AddRange( GetCornerBeziers( new Vector2(lin,b), bl,  new Vector2(l,bin) ) );
					return new Path(Points);
				}
				else
				{
					var tl = new BezierPoint( new Vector2(l,t) );
					var tr = new BezierPoint( new Vector2(r,t) );
					var br = new BezierPoint( new Vector2(r,b) );
					var bl = new BezierPoint( new Vector2(l,b) );
					var Points = new BezierPoint[]{tl,tr,br,bl};
					return new Path(Points);
				}
			}
			
			
			public void				Render(UIElements.Painter2D Painter)
			{
				if ( EllipsePath is Ellipse e )
				{
					//	gr: unity doesn't support non uniform circles
					//	https://stackoverflow.com/a/20582153/355753
					var Radius = e.Radius.x;
					Painter.MoveTo(e.Center);
					Painter.Arc( e.Center, Radius, 0, 360 );
				}
				
				if ( BezierPath?.Length > 0 )
				{
					Painter.MoveTo(BezierPath[0].End);
					for ( var p=1;	p<BezierPath.Length;	p++ )
					{
						var Point = BezierPath[p];
						Painter.BezierCurveTo( Point.ControlPointIn, Point.ControlPointOut, Point.End ); 
					}
				}
				
				if ( LinearPath?.Length > 0 )
				{
					Painter.MoveTo(LinearPath[0]);
					for ( var p=1;	p<LinearPath.Length;	p++ )
					{
						var Point = LinearPath[p];
						Painter.LineTo( Point ); 
					}
				}
				
			}
			
			public void				EnumDebugPoints(Action<DebugPoint> EnumDebugPoint)
			{
				if ( EllipsePath is Ellipse e )
				{
					EnumDebugPoint( new DebugPoint(e.Center,0,Color.green) );
					EnumDebugPoint( new DebugPoint(e.Center,1,Color.yellow, e.Center+e.Radius ) );
				}
				else if ( BezierPath?.Length > 0 )
				{
					foreach (var Point in BezierPath)
					{
						EnumDebugPoint( new DebugPoint(Point.End,0,Color.red,Point.ControlPointIn) );
						EnumDebugPoint( new DebugPoint(Point.End,1,Color.green) );
						EnumDebugPoint( new DebugPoint(Point.End,2,Color.cyan,Point.ControlPointOut) );
					}
				}
				else if ( LinearPath?.Length > 0 )
				{
					foreach (var Point in LinearPath)
					{
						EnumDebugPoint( new DebugPoint(Point,0,Color.green) );
					}
				}
			}
			
			//	returns null if there's nothing to calculate bounds from.
			//	this should go, a path should _always_ have some bounds, even for text layers, which ideally 
			//	would gather there size by here, but may not if we don't have glyph info.
			//	Really to solve this we need a glyph-provider system for the renderer (uitoolkit, shader, tesselator etc)
			public Rect? CalculateBounds()
			{
				float Minx = Single.MaxValue;
				float Miny = Single.MaxValue;
				float Maxx = Single.MinValue;
				float Maxy = Single.MinValue;
				bool AnySet = false;

				void Accumulate(float x,float y)
				{
					Minx = Mathf.Min( x, Minx );
					Miny = Mathf.Min( y, Miny );
					Maxx = Mathf.Max( x, Maxx );
					Maxy = Mathf.Max( y, Maxy );
					AnySet = true;
				}
				
				if ( EllipsePath is Ellipse e )
				{
					Accumulate( e.Center.x - e.Radius.x, e.Center.y - e.Radius.y );
					Accumulate( e.Center.x + e.Radius.x, e.Center.y + e.Radius.y );
				}
				
				//	gr: obviously not accurate for bezier paths
				if ( BezierPath?.Length > 0 )
				{
					foreach (var Point in BezierPath)
					{
						float Padding = 2.0f;
						Accumulate( Point.End.x-Padding, Point.End.y-Padding );
						Accumulate( Point.End.x+Padding, Point.End.y+Padding );
						Accumulate( Point.ControlPointIn.x-Padding, Point.ControlPointIn.y-Padding );
						Accumulate( Point.ControlPointIn.x+Padding, Point.ControlPointIn.y+Padding );
						Accumulate( Point.ControlPointOut.x-Padding, Point.ControlPointOut.y-Padding );
						Accumulate( Point.ControlPointOut.x+Padding, Point.ControlPointOut.y+Padding );
					}
				}
				
				if ( LinearPath?.Length > 0 )
				{
					foreach (var Point in LinearPath)
					{
						Accumulate( Point.x, Point.y );
					}
				}
				
				if ( !AnySet )
					return null;
					
				var Width = Maxx - Minx;
				var Height = Maxy - Miny;
				return new Rect( Minx, Miny, Width, Height );
			}
		}
		
		public struct AnimationFrame
		{
			public Rect			CanvasRect;	//	rect of the canvas of the animation, kept for debug
			public List<Shape>	Shapes;
			
			public void AddShape(Shape shape)
			{
				Shapes = Shapes ?? new ();
				Shapes.Add(shape);
			}
			
			public void		Render(UIElements.Painter2D Painter)
			{
				foreach (var Shape in Shapes)
				{
					Shape.Render(Painter);
				}
			}
			
			public List<AnimationText> GetTextPaths()
			{
				var Texts = new List<AnimationText>();
				
				foreach (var Shape in Shapes)
				{
					foreach (var Path in Shape.Paths)
					{
						if ( Path.TextPaths == null )
							continue;
						Texts.AddRange(Path.TextPaths);
					}
				}
				return Texts;
			}
			
			public void		RenderDebug(UIElements.Painter2D Painter)
			{
				void DrawDebugPoint(DebugPoint Point)
				{
					var WorldStart = Point.Start;
					Vector2? WorldEnd = Point.End;
					
					Painter.lineWidth = 0.2f;
					Painter.strokeColor = Point.Colour;
					Painter.BeginPath();
					Painter.MoveTo( WorldStart );
					if ( WorldEnd is Vector2 end )
					{
						Painter.LineTo( end );
						Painter.Arc( end, Point.HandleSize, 0.0f, 360.0f);
					}
					else
					{
						Painter.Arc( WorldStart, Point.HandleSize, 0.0f, 360.0f);
					}
					Painter.Stroke();
					Painter.ClosePath();
				}
				
				DrawRect( Painter, this.CanvasRect, new Color(0,0,1,0.1f) );
				
				foreach (var Shape in Shapes)
				{
					foreach (var Path in Shape.Paths)
					{
						Path.EnumDebugPoints(DrawDebugPoint);
						
						//	draw X for text boxes
						foreach (var Text in Path.TextPaths ?? Array.Empty<AnimationText>())
						{
							//	to be accurate we need glyphs, but this is just for debug
							var TextWidth = Text.FontSize * 0.8f;
							TextWidth *= Text.Text.Length;
							var TextHeight = Text.FontSize;
							
							Rect TextRect = new Rect( Text.Position, new Vector2(TextWidth,TextHeight) );
							//	do justification
							//	position is baseline
							TextRect.y -= TextHeight;
							if ( Text.Justify == TextJustify.Center )
								TextRect.x -= TextWidth/2.0f; 
							
							DrawRectX( Painter, TextRect, Color.cyan );
						}
					}
				}
			}
			
			static public void DrawRect(UIElements.Painter2D Painter,Rect rect,Color Colour)
			{
				Painter.BeginPath();
				var tl = rect.min;
				var tr = new Vector2(rect.xMax,rect.yMin);
				var br = rect.max;
				var bl = new Vector2(rect.xMin,rect.yMax);
				Painter.MoveTo( tl );
				Painter.LineTo( tr );
				Painter.LineTo( br );
				Painter.LineTo( bl );
				Painter.LineTo( tl );
				Painter.fillColor = Colour;
				Painter.Fill();
				Painter.ClosePath();
			}

			static public void DrawRectX(UIElements.Painter2D painter2D,Rect rect,Color Colour,float LineWidth=1)
			{
				var TL = new Vector2( rect.xMin, rect.yMin );
				var TR = new Vector2( rect.xMax, rect.yMin );
				var BL = new Vector2( rect.xMin, rect.yMax );
				var BR = new Vector2( rect.xMax, rect.yMax );
				painter2D.BeginPath();
				painter2D.MoveTo( TL );
				painter2D.LineTo( TR );
				painter2D.LineTo( BR );
				painter2D.LineTo( BL );
				painter2D.LineTo( TL );
				painter2D.LineTo( BR );
				painter2D.MoveTo( BL );
				painter2D.LineTo( TR );
				painter2D.ClosePath();
				painter2D.lineWidth = LineWidth;
				painter2D.strokeColor = Colour;
				painter2D.Stroke();
			}
			
		}
		
		
		//	rename to AnimationShape
		public struct Shape
		{
			//	these paths are rendered sequentially to cause holes
			public Path[]				Paths;
			public ShapeStyle			Style;
			public string				Name;

			public Shape(Path[] Paths,string Name,ShapeStyle Style)
			{
				this.Paths = Paths;
				this.Name = Name;
				this.Style = Style;
			}
			
			public void				Render(UIElements.Painter2D Painter)
			{
				Painter.BeginPath();

				foreach (var Path in Paths)
				{
					Path.Render(Painter);
				}
				
				if ( Style.StrokeColour is Color strokeColour )
				{
					Painter.strokeColor = strokeColour;
					Painter.lineWidth = Style.StrokeWidth ?? 0;
					Painter.lineCap = (UIElements.LineCap)Style.StrokeLineCap;
					Painter.lineJoin = (UIElements.LineJoin)Style.StrokeLineJoin;
					Painter.Stroke();
				}
				
				if ( Style.FillColour is Color fillColour )
				{
					Painter.fillColor = fillColour;
					Painter.Fill((UIElements.FillRule)Style.FillRule);
				}
				
				Painter.ClosePath();
			}
			
			
		}
	}
}
	
	