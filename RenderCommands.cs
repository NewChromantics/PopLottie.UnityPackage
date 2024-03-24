using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace PopLottie
{
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
			
			public void				Render(UnityEngine.UIElements.Painter2D Painter)
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
		}
		
		public struct Shape
		{
			//	these paths are renderered sequentially to cause holes
			public Path[]			Paths;
			public Color?			FillColour;
			public Color?			StrokeColour;
			public float			StrokeWidth;
			
			public void					Render(UnityEngine.UIElements.Painter2D Painter)
			{
				Painter.BeginPath();

				foreach (var Path in Paths)
				{
					Path.Render(Painter);
				}
				
				if ( StrokeColour is Color strokeColour )
				{
					Painter.strokeColor = strokeColour;
					Painter.lineWidth = StrokeWidth;
					Painter.Stroke();
				}
				
				if ( FillColour is Color fillColour )
				{
					Painter.fillColor = fillColour;
					Painter.Fill(UnityEngine.UIElements.FillRule.OddEven);
				}
				
				Painter.ClosePath();
			}
			
		}
	}
}
	
	