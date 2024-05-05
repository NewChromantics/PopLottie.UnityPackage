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
	
	public struct ShapeStyle
	{
		public Color?				FillColour;
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

	//	Currently optimised for UIToolkit VectorAPI 
	public static class RenderCommands
	{
		public struct BezierPoint
		{
			public Vector2	ControlPointIn;
			public Vector2	ControlPointOut;
			public Vector2	Position;
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
		
		public struct Path
		{
			public BezierPoint[]	BezierPath;
			public Vector3[]		LinearPath;
			public Ellipse?			EllipsePath;
			
			public Path(IEnumerable<BezierPoint> BezierPath)
			{
				this.BezierPath = BezierPath.ToArray();
				this.LinearPath = null;
				this.EllipsePath = null;
			}
			public Path(Ellipse EllipsePath)
			{
				this.BezierPath = null;
				this.LinearPath = null;
				this.EllipsePath = EllipsePath;
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
				else if ( BezierPath?.Length > 0 )
				{
					Painter.MoveTo(BezierPath[0].Position);
					for ( var p=1;	p<BezierPath.Length;	p++ )
					{
						var Point = BezierPath[p];
						Painter.BezierCurveTo( Point.ControlPointIn, Point.ControlPointOut, Point.Position ); 
					}
				}
				else if ( LinearPath?.Length > 0 )
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
						EnumDebugPoint( new DebugPoint(Point.Position,0,Color.red,Point.ControlPointIn) );
						EnumDebugPoint( new DebugPoint(Point.Position,1,Color.green) );
						EnumDebugPoint( new DebugPoint(Point.Position,2,Color.cyan,Point.ControlPointOut) );
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
				
				void DrawRect(Rect rect,Color Colour)
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
				DrawRect(this.CanvasRect, new Color(0,0,1,0.1f) );
				
				foreach (var Shape in Shapes)
				{
					foreach (var Path in Shape.Paths)
					{
						Path.EnumDebugPoints(DrawDebugPoint);
					}
				}
			}
			
		}
		
		
		
		public struct Shape
		{
			//	these paths are renderered sequentially to cause holes
			public Path[]				Paths;
			public ShapeStyle			Style;
			
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
					Painter.Fill(UIElements.FillRule.OddEven);
				}
				
				Painter.ClosePath();
			}
			
			public void				RenderDebug(UIElements.Painter2D Painter)
			{
				
			}
			
		}
	}
}
	
	