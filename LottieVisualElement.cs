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
				if ( LottieAnimation.IsStatic )
				{
					SetAutoRedraw(null);
				}
				else
				{
					SetAutoRedraw( RedrawInterval );
				}
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
			
			//	gr: trigger a single redraw here for static animations (and an immediate first frame redraw)
			MarkDirtyRepaint();
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
		
	

	
		void GenerateVisualContent(MeshGenerationContext context)
		{
			//Debug.Log($"GenerateVisualContent({this.resourceFilename})");
			
			//  draw an error box if we're missing the animation
			//  gr: can we render text easily here?
			if ( LottieAnimation == null )
			{
				//	draw the content rect when animation missing
				if ( enableDebug )
					RenderCommands.AnimationFrame.DrawRectX( context.painter2D, contentRect, Color.red);
				return;
			}
			
			//	render immediately
			//	todo: turn this into something that gets commands
			//		then only regenerate commands if the time has changed
			try
			{
				//	render a small square if the animation is static
				if ( enableDebug )
				{
					if ( LottieAnimation.IsStatic )
					{
						var IsStaticRect = contentRect;
						IsStaticRect.x += 10;
						IsStaticRect.y += 10;
						IsStaticRect.width = 10;
						IsStaticRect.height = 10;
						RenderCommands.AnimationFrame.DrawRectX( context.painter2D, IsStaticRect, Color.red, 10 );
					}
				}
			
				var Time = GetTime();
				//	gr: this result is now cacheable
				var Frame = LottieAnimation.Render( Time, contentRect, CanvasScaleMode );
				
				//	apply some "pre render effects" (function named to match swift version)
				OnPreRender(ref Frame);
				
				Frame.Render(context.painter2D);
				
				if ( enableDebug )
				{
					Frame.RenderDebug(context.painter2D);
				}
			}
			catch(Exception e)
			{
				Debug.LogException(e);
				RenderCommands.AnimationFrame.DrawRectX( context.painter2D, contentRect, Color.magenta); 
			}
		}

		void OnVisualElementDirty(GeometryChangedEvent ev)
		{
			//	content rect changed
			//Debug.Log($"OnVisualElementDirty anim={this.LottieAnimation} resource={this.ResourceFilename}");
		}

		protected virtual void OnPreRender(ref RenderCommands.AnimationFrame Frame)
		{
			var UiStyle = this.resolvedStyle;
			
			//	user has non-default tint
			if ( UiStyle.unityBackgroundImageTintColor != Color.white )
			{
				//	change the style of all shapes
				var Shapes = Frame.Shapes;
				for ( var i=0;	i<Shapes.Count;	i++ )
				{
					//	can't modify struct in-place with list
					var Shape = Shapes[i];
					Shape.Style.TintColour(UiStyle.unityBackgroundImageTintColor);
					Shapes[i] = Shape;
				}
			}
		}

	}
}
