using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
		Rect?					OverrideViewport = null;

		public SvgAnimation(string FileContents)
		{
			using(TextReader ContentsReader = new StringReader(FileContents))
			{
				//	default viewport with windowwidth/height just puts stuff in the wrong place, lets just parse viewbox ourselves
				//	,windowWidth:100,windowHeight:100
				SvgScene = Unity.VectorGraphics.SVGParser.ImportSVG(ContentsReader,ViewportOptions.OnlyApplyRootViewBox);
				
				//	gr: if the svg has no width/height, it comes through as 0,0,0,0 in the parser, even with a view box
				//		unity's svg asset shrinks the rect to bounds of the output.
				//		what we can do though, is just extract the viewbox and use that
				if ( SvgScene.SceneViewport.width <= 0 || SvgScene.SceneViewport.height <= 0 )
				{
					//	<svg ... viewBox="0 0 9000.0001 699.99997">
					var ViewboxPattern = "viewBox=\"([^\"]+)\"";
					var Reg = new Regex(ViewboxPattern);
					var Result = Reg.Match(FileContents);
					if ( !Result.Success )
					{
						Debug.LogWarning($"Svg has no viewport (zero width/height) and found no viewbox");
						return;
					}
					try
					{
						//if ( Result.Groups.Count != 5 )
						//	throw new Exception($"Expected 5 group hits in viewbox regex");

						var MatchString = Result.Groups[1].Value;
						var ValueStrings = MatchString.Split(null);	//	gr: null here means whitespace
						var ValueFloats = ValueStrings.Select( float.Parse ).ToArray();
						var x = ValueFloats[0];
						var y = ValueFloats[1];
						var w = ValueFloats[2];
						var h = ValueFloats[3];
						//	gr: we cannot overwrite this!
						this.OverrideViewport = new Rect(x,y,w,h);
					}
					catch(Exception e)
					{
						Debug.LogError($"Svg has no viewport (zero width/height) and error extracting viewbox; {e.Message}");
						return;
					}
				}
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
		
		static AnimationLineCap GetLineCap(PathEnding ending)
		{
			switch (ending)
			{
				default:
				case PathEnding.Chop:	return AnimationLineCap.Butt;
				case PathEnding.Round:	return AnimationLineCap.Round;
				case PathEnding.Square:	return AnimationLineCap.Square;
			}
		}
		
		static AnimationLineJoin GetLineJoin(PathEnding ending)
		{
			switch (ending)
			{
				default:
				case PathEnding.Chop:	return AnimationLineJoin.Miter;
				case PathEnding.Round:	return AnimationLineJoin.Round;
				case PathEnding.Square:	return AnimationLineJoin.Bevel;
			}
		}
			
		public override RenderCommands.AnimationFrame Render(FrameNumber Frame, Rect ContentRect,ScaleMode scaleMode)
		{
			var AssetViewport = OverrideViewport ?? SvgScene.SceneViewport;
			var (RootTransform,OutputCanvasRect) = LottieAnimation.DoRootTransform( AssetViewport, ContentRect, scaleMode );

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
							OutShape.Style.FillColour = null;
						}
						
						if ( NodeShape.PathProps.Stroke is Stroke stroke )
						{
							var Thickness = stroke.HalfThickness * 2.0f;
							Thickness = LayerTransform.LocalToWorldSize(Thickness);
							OutShape.Style.StrokeWidth = Thickness;
							OutShape.Style.StrokeColour = stroke.Color;
							OutShape.Style.StrokeLineCap = GetLineCap(NodeShape.PathProps.Tail);
							OutShape.Style.StrokeLineJoin = GetLineJoin(NodeShape.PathProps.Head);	//	should be corners really!
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

#if false
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
			#endif
			return OutputFrame;
		}
		
	}
	
}

