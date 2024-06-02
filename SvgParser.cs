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
			
			
			
			void AddLayer(SceneNode Node)
			{
				
			}
			AddLayer(SvgScene.Scene.Root);

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

