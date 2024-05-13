using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace PopLottie
{
	using FrameNumber = System.Single;	//	float

	enum AnimationFileType
	{
		Lottie,
		Svg,
	}

	public abstract class Animation : IDisposable
	{
		public abstract bool			IsStatic{get;}
		public abstract bool			HasTextLayers{get;}
		
		public abstract FrameNumber		TimeToFrame(TimeSpan Time,bool Looped);
		public abstract TimeSpan		FrameToTime(FrameNumber Frame);
		public abstract TimeSpan		Duration{get;}		//	could make this nullable, which would infer the animation is static
		public abstract	int				FrameCount{get;}	//	this, or duration, should be redundant

		
		static AnimationFileType?		PeekFileType(string FileContents)
		{
			//	todo: better hinting
			if ( FileContents.StartsWith("<svg") )
				return AnimationFileType.Svg;
			return null;
		}

		static Animation				AllocateAnimation(AnimationFileType Type,string FileContents)
		{
			switch (Type)
			{
				case AnimationFileType.Lottie:	return new LottieAnimation(FileContents);
				default:
					throw new Exception($"AllocateAnimation() Unhandled file type {Type}");
			}
		}

		public static Animation			Parse(string LottieJsonOrSvg)
		{
			//	try loading all formats, starting with a format we think
			//	it might be.
			//	Probably shouldn't bother trying the first type twice though
			var LoadTypes = new AnimationFileType?[]
			{
			PeekFileType(LottieJsonOrSvg),
			AnimationFileType.Lottie,
			AnimationFileType.Svg
			};

			string ParseErrors = null;
			foreach (var LoadType in LoadTypes )
			{
				if ( LoadType == null )
					continue;
				try
				{
					var Anim = AllocateAnimation(LoadType.Value,LottieJsonOrSvg);
					return Anim;
				}
				catch(Exception e)
				{
					ParseErrors += e.Message;
				}
			}
			
			throw new Exception($"Failed to load file as animation; {ParseErrors}");
		}

		public abstract void Dispose();
		
		public RenderCommands.AnimationFrame Render(TimeSpan PlayTime, Rect ContentRect,ScaleMode scaleMode)
		{
			//	get the time, move it to lottie-anim space and loop it
			var Frame = TimeToFrame(PlayTime,Looped:true);
			return Render( Frame, ContentRect, scaleMode );
		}
			
		public abstract RenderCommands.AnimationFrame Render(FrameNumber Frame, Rect ContentRect,ScaleMode scaleMode);
	}
}

