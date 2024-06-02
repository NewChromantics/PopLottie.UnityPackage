using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

//	we need to dynamically change the structure as we parse, so the built in json parser wont cut it
//	com.unity.nuget.newtonsoft-json
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VectorGraphics;
using Object = UnityEngine.Object;


namespace PopLottie
{
	using FrameNumber = System.Single;	//	float


	public class SvgAnimation : Animation
	{
		public override bool	IsStatic => true;
		public override bool	HasTextLayers => false;
		
		SVGParser.SceneInfo		SvgScene;

		public SvgAnimation(string FileContents)
		{
			using(TextReader ContentsReader = new StringReader(FileContents))
			{ 
				SvgScene = Unity.VectorGraphics.SVGParser.ImportSVG(ContentsReader,ViewportOptions.PreserveViewport);
			}
		}
		
		
		public override TimeSpan	Duration => TimeSpan.Zero;
		public override int			FrameCount => 0;
		public override FrameNumber	TimeToFrame(TimeSpan Time,bool Looped)
		{
			return 0;
		}
		public override TimeSpan	FrameToTime(FrameNumber Frame)
		{
			return TimeSpan.Zero;
		}

		public override void Dispose()
		{
		}
			
		public override RenderCommands.AnimationFrame Render(FrameNumber Frame, Rect ContentRect,ScaleMode scaleMode)
		{
			var (RootTransform,OutputCanvasRect) = LottieAnimation.DoRootTransform( SvgScene.SceneViewport, ContentRect, scaleMode );

			var OutputFrame = new RenderCommands.AnimationFrame();
			OutputFrame.CanvasRect = OutputCanvasRect;

			void AddRenderShape(RenderCommands.Shape NewShape)
			{
				OutputFrame.AddShape(NewShape);
			}
			
			
			
			void AddLayer(SceneNode Node,Transformer LayerTransform)
			{
				if ( Node.Shapes != null )
				{
					foreach (var NodeShape in Node.Shapes )
					{
						RenderCommands.Path ContourToPath(BezierContour contour)
						{
							//	gr: these are CUBIC https://docs.unity3d.com/Packages/com.unity.vectorgraphics@1.0/api/Unity.VectorGraphics.BezierSegment.html
							//		even though SVG is quadratic and cubic
							List<RenderCommands.BezierPoint> Points = new();
							var FirstPoint = new RenderCommands.BezierPoint( LayerTransform.LocalToWorldPosition(contour.Segments[0].P0) );
							Points.Add(FirstPoint);
							
							//	https://docs.unity3d.com/Packages/com.unity.vectorgraphics@2.0/api/Unity.VectorGraphics.BezierPathSegment.html
							for ( int s=0;	s<contour.Segments.Length;	s++ )
							{
								var Segment = contour.Segments[s];
								var NextSegment = contour.Segments[(s+1)%contour.Segments.Length];
								//var Start = LayerTransform.LocalToWorldPosition(Segment.P0);
								var ControlIn = LayerTransform.LocalToWorldPosition(Segment.P1);
								var ControlOut = LayerTransform.LocalToWorldPosition(Segment.P2);
								var End = LayerTransform.LocalToWorldPosition(NextSegment.P0);
								var Point = new RenderCommands.BezierPoint(End,ControlIn,ControlOut);
								Points.Add(Point);
							}
							if ( contour.Closed )
							{
								//	todo; handle this
							}
							return new RenderCommands.Path(Points);
						}
						var OutShape = new RenderCommands.Shape();
						OutShape.Paths = NodeShape.Contours.Select(ContourToPath).ToArray();
						
						OutShape.Style = new ShapeStyle();
						OutShape.Style.StrokeColour = Color.yellow;
						if ( NodeShape.Fill is SolidFill solidFill )
						{
							OutShape.Style.FillColour = solidFill.Color;
						}
						else
						{
							OutShape.Style.FillColour = Color.magenta;
						}
						AddRenderShape( OutShape );
					}
				}

				if ( Node.Children != null )
				{
					foreach (var Child in Node.Children )
					{
						AddLayer( Child, LayerTransform );
					}
				}
			}
			AddLayer(SvgScene.Scene.Root,RootTransform);

			if ( false )
			{
				var Center = Vector2.zero;
				var Size = new Vector2(SvgScene.SceneViewport.width/2,SvgScene.SceneViewport.height/2);
				var Radius = Size.x * 0.10f;
				var StrokeWidth = Size.x * 0.05f;
				Center = RootTransform.LocalToWorldPosition(Center);
				Size = RootTransform.LocalToWorldSize(Size);
				Radius = RootTransform.LocalToWorldSize(Radius);
				StrokeWidth = RootTransform.LocalToWorldSize(StrokeWidth);
				
				var TestRect = RenderCommands.Path.CreateRect( Center, Size, Radius );
				var TestShape = new RenderCommands.Shape();
				TestShape.Name = "Test";
				TestShape.Paths = new []{TestRect};
				TestShape.Style = new ShapeStyle();
				TestShape.Style.FillColour = Color.red;
				TestShape.Style.StrokeColour = Color.yellow;
				TestShape.Style.StrokeWidth = StrokeWidth;
				AddRenderShape( TestShape );
			}
			return OutputFrame;
		}
		
	}
	
}

