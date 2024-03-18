using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PopLottie
{
	public class LottieVisualElement : VisualElement, IDisposable
	{
		Animation	LottieAnimation;
		
		//	current auto-redraw scheduler causing element to re-draw
		IVisualElementScheduledItem	autoRedrawScheduler;

		string		ResourceFilename;
		public string	resourceFilename
		{
			get => ResourceFilename;
			set
			{
				ResourceFilename = value;
				LoadAnimation();
				MarkDirtyRepaint();
			}
		}

		bool		EnableDebug;
		public bool	enableDebug
		{
			get => EnableDebug;
			set
			{
				EnableDebug = value;
				MarkDirtyRepaint();
			}
		}
		
		ScaleMode			CanvasScaleMode;
		public ScaleMode	scaleMode
		{
			get => CanvasScaleMode;
			set
			{
				CanvasScaleMode = value;
				MarkDirtyRepaint();
			}
		}
		
		TimeSpan?	RedrawInterval;
		public uint	redrawIntervalMilliseconds
		{
			get => (uint)(RedrawInterval?.TotalMilliseconds ?? 0);
			set
			{
				if ( value <= 0 )
					RedrawInterval = null;
				else
					RedrawInterval = TimeSpan.FromMilliseconds(value);
				
				SetAutoRedraw(RedrawInterval);
			}
		}



		void LoadAnimation()
		{
			try
			{
				var _animationJson = Resources.Load<TextAsset>(ResourceFilename);
				if ( _animationJson == null )
					throw new Exception($"Text-Asset Resource not found at {ResourceFilename} (Do not include extension)");
				
				//	parse file
				LottieAnimation = new Animation(_animationJson.text);
				SetAutoRedraw( RedrawInterval );
			}
			catch ( Exception e)
			{
				Debug.LogException(e);
				Debug.LogError($"Failed to load animation {ResourceFilename}; {e.Message}");
				Dispose();
			}
		}
		
		public new class UxmlFactory : UxmlFactory<LottieVisualElement, UxmlTraits> { }
		public new class UxmlTraits : VisualElement.UxmlTraits
		{
			UxmlBoolAttributeDescription enableDebugAttribute = new()
			{
				name = "enable-Debug",
				defaultValue = false
			};
			UxmlStringAttributeDescription resourceFilenameAttribute = new()
			{
				name = "resource-Filename",
				defaultValue = "AnimationWithoutExtension"
			};
			UxmlUnsignedIntAttributeDescription redrawIntervalMillisecondsAttribute = new()
			{
				name = "redraw-Interval-Milliseconds",
				defaultValue = 15
			};
			UxmlEnumAttributeDescription<ScaleMode> scaleModeAttribute = new()
			{
				name = "scale-mode",
				defaultValue = ScaleMode.ScaleToFit
			};

			//public UxmlTraits() { }

			// Use the Init method to assign the value of the progress UXML attribute to the C# progress property.
			public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
			{
				base.Init(ve, bag, cc);
				
				(ve as LottieVisualElement).resourceFilename = resourceFilenameAttribute.GetValueFromBag(bag, cc);
				(ve as LottieVisualElement).enableDebug = enableDebugAttribute.GetValueFromBag(bag, cc);
				(ve as LottieVisualElement).redrawIntervalMilliseconds = redrawIntervalMillisecondsAttribute.GetValueFromBag(bag, cc);
				(ve as LottieVisualElement).scaleMode = scaleModeAttribute.GetValueFromBag(bag, cc);
			}
		}

		public LottieVisualElement()
		{
			RegisterCallback<GeometryChangedEvent>(OnVisualElementDirty);
			RegisterCallback<DetachFromPanelEvent>(c => { this.OnDetached(); });
			RegisterCallback<AttachToPanelEvent>(c => { this.OnAttached(); });

			generateVisualContent += GenerateVisualContent;
			
			SetAutoRedraw( TimeSpan.FromMilliseconds(10) );
		}
		
		//	pass null to stop auto animation
		void SetAutoRedraw(TimeSpan? RedrawInterval)
		{
			//	stop old scheduler
			if ( autoRedrawScheduler != null )
			{
				//	pause & null https://forum.unity.com/threads/understaning-schedule-ivisualelementscheduleditem.1125752/
				autoRedrawScheduler.Pause();
				autoRedrawScheduler = null;
			}
			
			//	auto play by repainting this element
			if ( RedrawInterval is TimeSpan interval )
			{
				var ItnervalMs = (long)interval.TotalMilliseconds;
				//Debug.Log($"Changing redraw interval to {ItnervalMs}ms");
				autoRedrawScheduler = schedule.Execute( MarkDirtyRepaint ).Every(ItnervalMs);
			}
		}
		
		void OnAttached()
		{
			LoadAnimation();
		}
		void OnDetached()
		{
			Dispose();
		}
		
		public void Dispose()
		{
			SetAutoRedraw(null);
			LottieAnimation?.Dispose();
			LottieAnimation = null;
		}
		
		public TimeSpan GetTime()
		{
			return TimeSpan.FromSeconds( Time.realtimeSinceStartup );
		}
		
	
		void DrawRectX(Painter2D painter2D,Rect rect,Color Colour,float LineWidth=1)
		{
			var TL = new Vector2( contentRect.xMin, contentRect.yMin );
			var TR = new Vector2( contentRect.xMax, contentRect.yMin );
			var BL = new Vector2( contentRect.xMin, contentRect.yMax );
			var BR = new Vector2( contentRect.xMax, contentRect.yMax );
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
	
		void GenerateVisualContent(MeshGenerationContext context)
		{
			//  draw an error box if we're missing the animation
			//  gr: can we render text easily here?
			if ( LottieAnimation == null )
			{
				//	draw the content rect when animation missing
				if ( enableDebug )
					DrawRectX( context.painter2D, contentRect, Color.red);
				return;
			}
			
			//	render immediately
			//	todo: turn this into something that gets commands
			//		then only regenerate commands if the time has changed
			try
			{
				var Time = GetTime();
				LottieAnimation.Render( Time, context.painter2D, contentRect, enableDebug, CanvasScaleMode );
			}
			catch(Exception e)
			{
				Debug.LogException(e);
				DrawRectX( context.painter2D, contentRect, Color.magenta); 
			}
		}

		void OnVisualElementDirty(GeometryChangedEvent ev)
		{
			//	content rect changed
			//Debug.Log($"OnVisualElementDirty anim={this.LottieAnimation} resource={this.ResourceFilename}");
		}


	}
}
