using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

//	we need to dynamically change the structure as we parse, so the built in json parser wont cut it
//	com.unity.nuget.newtonsoft-json
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;


//  this is actually the bodymovin spec
namespace PopLottie
{
	using FrameNumber = System.Single;	//	float

	//	spec is readable here
	//	https://lottiefiles.github.io/lottie-docs/breakdown/bouncy_ball/


	[Serializable] public struct AssetMeta
	{
	}


	[UnityEngine.Scripting.Preserve]
	class ValueCurveConvertor : JsonConverter<ValueCurve>
	{
		static float[] GetFloats(JToken TokenMaybe)
		{
			if ( TokenMaybe is JArray array )
			{
				return array.Select( GetValue ).ToArray();
			}
			else if ( TokenMaybe is JToken token )
			{
				var Value = GetValue(token);
				return new float[]{Value};
			}
			else
			{
				throw new Exception($"Failed to get float or array from expected float or array");
			}
		}
		static float GetValue(JToken Value)
		{
			if ( Value.Type == JTokenType.Integer )
				return (long)Value;
			if ( Value.Type == JTokenType.Float )
				return (float)Value;
			throw new Exception("Got javascript value which isnt a number");
		}
		
		public override void WriteJson(JsonWriter writer, ValueCurve value, JsonSerializer serializer) { throw new NotImplementedException(); }
		public override ValueCurve ReadJson(JsonReader reader, Type objectType, ValueCurve existingValue, bool hasExistingValue,JsonSerializer serializer)
		{
			var ThisObject = JObject.Load(reader);
			existingValue.x = GetFloats( ThisObject.GetValue("x") );
			existingValue.y = GetFloats( ThisObject.GetValue("y") );
			return existingValue;
		}
	}
	
	
	[Serializable]
	[JsonConverter(typeof(ValueCurveConvertor))]
	public struct ValueCurve
	{
		//	this can be an array or a single value, so ValueCurve has a custom convertor
		public float[]	x;	//	time X axis
		public float[]	y;	//	value Y axis
	}

	//	https://lottiefiles.github.io/lottie-docs/playground/json_editor/
	[Serializable] public class AnimatedVector
	{
		public int				a;
		public bool				Animated => a!=0;
		public bool				s;
		
		//	the vector .p(this) is split into components instead of arrays of values
		public bool				SplitVector => s;	
		public AnimatedVector	x;
		public AnimatedVector	y;
		
		//	keyframes when NOT split vector
		public Keyframed_FloatArray	k;
		
		public bool		IsStatic()
		{
			if ( !Animated )
				return true;
			
			//	we probe further, just in case some of the curves/keyframes are static
			//	with lerps between the same values, or only one frame number
			if ( !k.IsStatic() )
				return false;

			//	gr: only need to check x/y if split vector really...
			if ( !x.IsStatic() )
				return false;
			if ( !y.IsStatic() )
				return false;
			return true;
		}
		
		public float			GetValue(FrameNumber Frame)
		{
			if ( SplitVector )
			{
				return x.GetValueArray(Frame)[0];
			}
			return k.GetValue(Frame)[0];
		}
		
		
		public float[]			GetValueArray(FrameNumber Frame)
		{
			if ( SplitVector )
			{
				var v0 = x.GetValueArray(Frame)[0];
				var v1 = y.GetValueArray(Frame)[0];
				return new []{v0,v1};
			}
			return k.GetValue(Frame);
		}
		
		public Vector2			GetValueVec2(FrameNumber Frame)
		{
			var Values = GetValueArray(Frame);
			if ( Values == null || Values.Length == 0 )
				throw new Exception($"{GetType().Name}::GetValue(vec2) missing frames"); 

			//	1D scale... usually
			if ( Values.Length == 1 )
				return new Vector2(Values[0],Values[0]);
				
			return new Vector2(Values[0],Values[1]);
		}
	}


	//	gr: I've realised this is exactly the same struct as Frame_FloatArray
	[Serializable] public struct Frame_Float : IFrame
	{
		public ValueCurve	i;	//	ease in value
		public ValueCurve	o;	//	ease out value
		public float		t;	//	time
		public float[]		s;	//	value at time
		public float[]		e;	//	end value
		public FrameNumber	Frame => t;
		public bool			IsTerminatingFrame => s==null;
		
		bool				IsSameStartAndEndValues()
		{
			for ( int i=0;	i<s.Length;	i++ )
			{
				if ( s[i] != e[i] )
					return false; 
			}
			return true;
		}
		
		public bool			IsStatic()
		{
			if ( IsTerminatingFrame )
				return true;
				
			//	same start & end values? animating between nothing!
			if ( !IsSameStartAndEndValues() )
				return false;

			//	todo: can we detect static curves?
			return false;
		}
		
		public float		LerpTo(Frame_Float Next,float? Lerp)
		{
			float[] NextValues = Next.s;
			float[] PrevValues = this.s;

			if ( Lerp == null )
				return PrevValues[0];

			//	this happens on terminator frames
			if ( NextValues == null )
				if ( this.e != null )
					NextValues = this.e;

			if ( NextValues == null )
				NextValues = PrevValues;

			if ( PrevValues == null || NextValues == null )
				throw new Exception($"{GetType().Name}::Lerp prev or next frame values"); 

			//	lerp each member
			var Values = new float[s.Length];
			for ( int v=0;	v<Values.Length;	v++ )
				Values[v] = IFrame.Interpolate( v, PrevValues, NextValues, Lerp.Value, i, o );
			return Values[0];
		}
		
	}
	
	
	[Serializable] public struct Frame_FloatArray : IFrame
	{
		public ValueCurve	i;
		public ValueCurve	o;
		public int			h;
		public bool			HoldingFrame => h!=0;
		public float		t;	//	time
		public float[]		s;	//	start value
		public float[]		e;	//	end value
		public FrameNumber	Frame	=> t;
		public bool			IsTerminatingFrame => s==null;
		
		bool				IsSameStartAndEndValues()
		{
			for ( int i=0;	i<s.Length;	i++ )
			{
				if ( s[i] != e[i] )
					return false; 
			}
			return true;
		}
		
		public bool			IsStatic()
		{
			if ( IsTerminatingFrame )
				return true;
				
			//	same start & end values? animating between nothing!
			if ( !IsSameStartAndEndValues() )
				return false;

			//	todo: can we detect static curves?
			return false;
		}
			
		public float[]		LerpTo(Frame_FloatArray Next,float? Lerp)
		{
			float[] NextValues = Next.s;
			float[] PrevValues = this.s;
			if ( Lerp == null )
				return PrevValues;

			//	this happens on terminator frames
			if ( NextValues == null )
				if ( this.e != null )
					NextValues = this.e;

			if ( NextValues == null )
				NextValues = PrevValues;

			if ( PrevValues == null || NextValues == null )
				return null;
		
			//	lerp each member
			var Values = new float[s.Length];
			for ( int v=0;	v<Values.Length;	v++ )
				Values[v] = IFrame.Interpolate( v, PrevValues, NextValues, Lerp.Value, i, o );
			return Values;
		}
	}
	
	
	
	[UnityEngine.Scripting.Preserve]
	class KeyframedConvertor<KeyFramedType,FrameType> : JsonConverter<KeyFramedType> where KeyFramedType : IKeyframed<FrameType>
	{
		static float GetValue(JToken Value)
		{
			if ( Value.Type == JTokenType.Integer )
				return (long)Value;
			if ( Value.Type == JTokenType.Float )
				return (float)Value;
			throw new Exception("Got javascript value which isnt a number");
		}
		
		static List<float> GetValues(JArray ArrayOfNumbers)
		{
			var Numbers = new List<float>();
			foreach ( var Value in ArrayOfNumbers )
			{
				var ValueNumber = GetValue(Value);
				Numbers.Add(ValueNumber);
			}
			return Numbers;
		}
		
		public override void WriteJson(JsonWriter writer, KeyFramedType value, JsonSerializer serializer) { throw new NotImplementedException(); }
		public override KeyFramedType ReadJson(JsonReader reader, Type objectType, KeyFramedType existingValue, bool hasExistingValue,JsonSerializer serializer)
		{
			if ( reader.TokenType == JsonToken.StartObject )
			{
				var ThisObject = JObject.Load(reader);
				existingValue.AddFrame( ThisObject, serializer );
			}
			else if ( reader.TokenType == JsonToken.StartArray )
			{
				var ThisArray = JArray.Load(reader);
				
				//	if this is an array of objects, it's an array of frames
				//	if not, this might just be a single frame of values
				var Element0 = ThisArray[0];
				if ( Element0.Type == JTokenType.Array || Element0.Type == JTokenType.Object )
				{
					foreach ( var Frame in ThisArray )
					{
						var FrameReader = new JTokenReader(Frame);
						var FrameObject = JObject.Load(FrameReader);
						existingValue.AddFrame( FrameObject, serializer );
					}
				}
				else
				{
					//	this is an array of values, so one frame
					existingValue.AddFrame(GetValues(ThisArray).ToArray());
				}
			}
			else if ( reader.TokenType == JsonToken.Integer || reader.TokenType == JsonToken.Float )
			{
				var Value = reader.Value;
				var Number = (reader.TokenType == JsonToken.Integer) ? (long)Value : (double)Value;
				existingValue.AddFrame(new float[]{(float)Number});
			}
			else 
			{
				//existingValue.ReadAnimatedOrNotAnimated(reader);
				Debug.LogWarning($"Decoding Frame_Float unhandled token type {reader.TokenType}");
			}
			if ( existingValue.FrameCount == 0 )
				throw new Exception($"Should never parse a keyframed value with no frames");
			return existingValue;
		}
	}

	//	making the json convertor simpler with a generic interface
	interface IKeyframed<T>
	{
		public void AddFrame(JObject Object,JsonSerializer Serializer);
		public void AddFrame(T Frame);
		public void AddFrame(float[] Values);
		public int FrameCount { get;}
	}
	
	public interface IFrame
	{
		public FrameNumber		Frame { get;}
		public bool				IsTerminatingFrame {get;}	//	if this frame is just an end frame with no values, we wont try and read them
		
		static float GetSlope(float aT,float aA1,float aA2)
		{
			static float A(float aA1, float aA2) { return 1.0f - 3.0f * aA2 + 3.0f * aA1; }
			static float B(float aA1, float aA2) { return 3.0f * aA2 - 6.0f * aA1; }
			static float C(float aA1) { return 3.0f * aA1; }

			return 3.0f * A(aA1, aA2) * aT * aT + 2.0f * B(aA1, aA2) * aT + C(aA1);
		}

		static float GetBezierValue(float p0,float p1,float p2,float p3,float Time)
		{
			float t = Time;
			//	https://morethandev.hashnode.dev/demystifying-the-cubic-bezier-function-ft-javascript
			return (1 - t) * (1 - t) * (1 - t) * p0
					+
					3 * (1 - t) * (1 - t) * t * p1
					+
					3 * (1 - t) * t * t * p2
					+
					t * t * t * p3;
		}


		static float Interpolate(float Prev,float Next,float Time,float? InX,float? InY,float? OutX,float? OutY)
		{
			//	from docs
			//	The y axis represents the value interpolation factor, a value of 0 represents the value at the current keyframe, a value of 1 represents the value at the next keyframe
			var LinearValue = Mathf.Lerp( Prev, Next, Time );
			
			
			float GetCurveX(float Start,float EaseOut,float EaseIn,float End,float Time)
			{
				//return GetBezierValue( Start, EaseOut, EaseIn, End, Time );
				//	https://github.com/Samsung/rlottie/blob/d40008707addacb636ff435236d31c694ce2b6cf/src/vector/vinterpolator.cpp#L86
				//	newton raphson iterating to find tighter point on the curve
				var aX = Time;
				var aGuessT = Time;
				for ( int it=0;	it<10;	it++ )
				{
					float CurrentX = GetBezierValue( Start, EaseOut, EaseIn, End, aGuessT ) - aX;
					float Slope = GetSlope( aGuessT, EaseOut, EaseIn );
					if ( Slope <= 0.0001f )
						break;
						
					aGuessT -= CurrentX / Slope;
				}
				return aGuessT;
			}
			
			
			if ( InX != null )
			{
				var Start = Vector2.zero;
				var EaseOut = new Vector2( OutX.Value, OutY.Value );
				var EaseIn = new Vector2( InX.Value, InY.Value );
				var End = Vector2.one;

				//	https://github.com/airbnb/lottie-ios/blob/41dfe7b0d8c3349adc7a5a03a1c6aaac8746433d/Sources/Private/Utility/Primitives/UnitBezier.swift#L36
				//	uses https://github.com/gnustep/libs-quartzcore/blob/master/Source/CAMediaTimingFunction.m#L204C13-L204C25
				//	solve time first
				float CurveTime = GetCurveX( Start.x, EaseOut.x, EaseIn.x, End.x, Time );
				
				//	solve value
				float CurveValue = GetBezierValue( Start.y, EaseOut.y, EaseIn.y, End.y, CurveTime );
				var FinalValue = Mathf.Lerp( Prev, Next, CurveValue );
				return FinalValue;
			}
			return LinearValue;
		}
		
		static float Interpolate(int Component,float[] Prev,float[] Next,float Time,ValueCurve? In,ValueCurve? Out)
		{
			if ( Component < 0 || Component >= Prev.Length )
				throw new Exception($"Interpolate out of bounds");
			var EaseInX = In?.x;
			//var EaseInY = In?.y;
			//var EaseOutX = Out?.x;
			//var EaseOutY = Out?.y;
			//	somtimes the curve has fewer components than the object... should this be linear for that component, or spread?
			var EaseComponent = Component;
			if ( EaseInX != null )
				EaseComponent = Mathf.Min( Component, EaseInX.Length-1 );
			return Interpolate( Prev[Component], Next[Component], Time, In?.x?[EaseComponent], In?.y?[EaseComponent], Out?.x?[EaseComponent], Out?.y?[EaseComponent] );
		}
	
		
		//	returns null for Time, if both are same frame
		static (FRAMETYPE,float?,FRAMETYPE) GetPrevNextFramesAtFrame<FRAMETYPE>(List<FRAMETYPE> Frames,FrameNumber TargetFrame) where FRAMETYPE : IFrame
		{
			if ( Frames == null || Frames.Count == 0 )
				throw new Exception("GetPrevNextFramesAtFrame missing frames");
			
			if ( Frames.Count == 1 )
				return (Frames[0],0,Frames[0]);
			
			//	find previous & next frames
			var PrevIndex = 0;
			for ( int f=0;	f<Frames.Count;	f++ )
			{
				var ThisFrame = Frames[f];
				//	terminating frames have no values
				if ( ThisFrame.IsTerminatingFrame )
				{
					if ( f != Frames.Count-1 )
						Debug.LogWarning($"Terminator frame in middle of sequence {f}/{Frames.Count-1}");
					break;
				}
				if ( ThisFrame.Frame > TargetFrame )
					break;
				PrevIndex = f;
			}
			var NextIndex = Mathf.Min(PrevIndex + 1, Frames.Count-1);
			var Prev = Frames[PrevIndex];
			var Next = Frames[NextIndex];

			//	allow some optimisations by inferring that there is nothing to lerp between
			if ( PrevIndex == NextIndex )
				return (Prev,null,Prev);

			//	get the lerp(time) between prev & next
			float Range(float Min,float Max,float Value)
			{
				if ( Max-Min <= 0 )
					return 0;
				return (Value-Min)/(Max-Min);
			}
			//var Lerp = Mathf.InverseLerp( Prev.Frame, Next.Frame, TargetFrame );
			var Lerp = Range( Prev.Frame, Next.Frame, TargetFrame );
			if ( Lerp < 0 )
				Lerp = 0;
			if ( Lerp > 1 )
				Lerp = 1;
				
			return (Prev,Lerp,Next);
		}
		
	}
	
	
		//	make this generic
	[JsonConverter(typeof(KeyframedConvertor<Keyframed_Float,Frame_Float>))]
	public struct Keyframed_Float : IKeyframed<Frame_Float>
	{
		List<Frame_Float>		Frames;
	
		public int FrameCount => Frames.Count;

		public bool IsStatic()
		{
			//	shouldn't really have zero frames... this should error at constructoion
			if ( Frames.Count == 0 )
				return true;
			//	gr; if we only have 1 frame (or 1 +terminator?)
			//		and it has no curve... then we're static?
			bool OneFrame = Frames.Count == 1;
			if ( Frames.Count == 2 && Frames[1].IsTerminatingFrame )
				OneFrame = true;
			
			if ( OneFrame && Frames[0].IsStatic() )
				return true;
			
			//	todo: more extensive check, if all frames have same start & end values
			return false;
		}
		
		public void AddFrame(float[] Values)
		{
			var Frame = new Frame_Float();
			Frame.s = Values;
			Frame.t = -123;
			AddFrame(Frame);
		}

		public void AddFrame(JObject Object,JsonSerializer Serializer)
		{
			AddFrame( Object.ToObject<Frame_Float>(Serializer) );
		}
		
		public void	AddFrame(Frame_Float Frame)
		{
			Frames = Frames ?? new();
			Frames.Add(Frame);
		}
		
		public float GetValue(FrameNumber Frame)
		{
			if ( Frames == null || Frames.Count == 0 )
				throw new Exception($"{GetType().Name}::GetValue missing frames"); 
				
			var (Prev,Lerp,Next) = IFrame.GetPrevNextFramesAtFrame(Frames,Frame);
			return Prev.LerpTo( Next, Lerp );
		}
	}
	
	
		//	make this generic
	[JsonConverter(typeof(KeyframedConvertor<Keyframed_FloatArray,Frame_FloatArray>))]
	public struct Keyframed_FloatArray : IKeyframed<Frame_FloatArray>
	{
		List<Frame_FloatArray>		Frames;
		
		public int FrameCount => Frames.Count;

		public bool IsStatic()
		{
			//	shouldn't really have zero frames... this should error at constructoion
			if ( Frames.Count == 0 )
				return true;
			//	gr; if we only have 1 frame (or 1 +terminator?)
			//		and it has no curve... then we're static?
			bool OneFrame = Frames.Count == 1;
			if ( Frames.Count == 2 && Frames[1].IsTerminatingFrame )
				OneFrame = true;
			
			if ( OneFrame && Frames[0].IsStatic() )
				return true;
			
			//	todo: more extensive check, if all frames have same start & end values
			return false;
		}

		public void AddFrame(float[] Numbers)
		{
			var Frame = new Frame_FloatArray();
			Frame.s = Numbers;
			Frame.t = -123;	//	if being added here, it shouldnt be keyframed
			//Frame.e = new []{Number};
			AddFrame(Frame);
		}

		public void AddFrame(JObject Object,JsonSerializer Serializer)
		{
			AddFrame( Object.ToObject<Frame_FloatArray>(Serializer) );
		}
		
		public void	AddFrame(Frame_FloatArray Frame)
		{
			Frames = Frames ?? new();
			Frames.Add(Frame);
		}
		
		public float[] GetValue(FrameNumber Frame)
		{
			if ( Frames == null || Frames.Count == 0 )
				throw new Exception($"{GetType().Name}::GetValue missing frames"); 
			
			var (Prev,Lerp,Next) = IFrame.GetPrevNextFramesAtFrame(Frames,Frame);
			var LerpedValues = Prev.LerpTo(Next,Lerp);
			if ( LerpedValues == null || LerpedValues.Length == 0 )
				throw new Exception($"Lerping frames resulting in missing data");

			return LerpedValues;
		}
	}
	
	//	https://lottiefiles.github.io/lottie-docs/playground/json_editor/
	[Serializable] public struct AnimatedNumber
	{
		public int			a;
		public bool			Animated => a!=0;
		
		public Keyframed_Float	k;	//	frames
		
		public bool			IsStatic()
		{
			//	we could just look at .Animated, but potentially the frames/curves are static too
			if ( !Animated )
				return true;
			return k.IsStatic();
		}
		
		public float		GetValue(FrameNumber Frame)
		{
			return k.GetValue(Frame);
		}
	}
	
	
	[Serializable] public struct Bezier
	{
		public List<float[]>	i;	//	in-tangents
		public List<float[]>	o;	//	out-tangents
		public List<float[]>	v;	//	vertexes
		public bool		c;		//	docs say 0-1, but seems to always be true/false
		public bool		Closed => c;	//c == 1;

		public ControlPoint[]	GetControlPoints(PathTrim Trim)
		{
			var PointCount = v.Count;
			var Points = new ControlPoint[PointCount];
			for ( var Index=0;	Index<PointCount;	Index++ )
			{
				Points[Index].Position.x = v[Index][0];
				Points[Index].Position.y = v[Index][1];
				Points[Index].InTangent.x = i[Index][0];
				Points[Index].InTangent.y = i[Index][1];
				Points[Index].OutTangent.x = o[Index][0];
				Points[Index].OutTangent.y = o[Index][1];
			}
			
			//	todo: trim points
			//		need to work out where in the path we cut, then calc new control points
			//	if there's an offset... we need to calculate ALL of them?
			if ( Trim.IsDefault )
				return Points;

			//	gr: this doesn't wrap. if start > end, then it goes backwards
			//		but offset does make it wrap
			//	gr: the values are in path-distance (0-1), rather than indexes
			float StartTime = (Trim.Start + Trim.Offset);
			float EndTime = (Trim.End + Trim.Offset);
			if ( Trim.End < Trim.Start )
			{
				var Temp = StartTime;
				StartTime = EndTime;
				EndTime = Temp;
			}
			
			//	todo: slice up bezier. Unfortunetly as the offsets are in distance, not control points
			//		we have to calculate where to cut, but hopefully that still leaves just two cut segments and then originals inbetween
			return Points;
		}
		
		public struct ControlPoint
		{
			public Vector2	InTangent;
			public Vector2	OutTangent;
			public Vector2	Position;
		}
	}
	
	[Serializable] public struct AnimatedBezier
	{
		public int			a;
		public bool			Animated => a!=0;
		
		//	todo: keyframed beziers
		public Bezier		k;	//	frames
		public int			ix;	//	property index
		
		public bool			IsStatic()
		{
			if ( !Animated )
				return true;
			//	todo: keyframed beziers
			return true;
		}
		
		public Bezier		GetBezier(FrameNumber Frame)
		{
			return k;
		}
	}
	
	[Serializable] public struct AnimatedColour
	{
		public int			a;
		public bool			Animated => a!=0;
		
		//	todo: keyframed colours
		public float[]		k;	//	4 elements 0..1
		public int			ix;	//	property index
		
		public bool			IsStatic()
		{
			if ( !Animated )
				return true;
			//	todo: keyframed colours
			return true;
		}
		
		public Color		GetColour(FrameNumber Frame)
		{
			if ( Animated )
				Debug.LogWarning($"todo: keyframed colour");
			var Alpha = k.Length == 4 ? k[3] : 1;
			if ( k.Length < 3 )
			{
				Debug.LogWarning($"Colour with fewer than 3(${k.Length}) components");
				return Color.magenta;
			}
			return new Color(k[0],k[1],k[2],Alpha);
		}
	}

	internal enum ShapeType
	{
		Fill,
		Stroke,
		Transform,
		Group,
		Path,
		Ellipse,
		TrimPath,		//	path trimmer, to modify (trim) a sibling shape
		Rectangle,
		Merge,
	}
	
	//	https://lottiefiles.github.io/lottie-docs/layers/#layers
	enum LayerType
	{
		Precomposition = 0,
		SolidColour = 1,
		Image = 2,
		Empty = 3,
		Shape = 4,
		Text = 5,
		Audio = 6,
		VideoPlaceholder = 7,
		ImageSequence = 8,
		Video = 9,
		ImagePlaceholder = 10,
		Guide = 11,
		Adjustment = 12,
		Camera3D = 13,
		Light = 14,
		Data = 15,

		//Unknown = 999	//	gr: instead of throwing and complicating swift, have a dummy value
	}
	

	[UnityEngine.Scripting.Preserve]
	class ShapeConvertor : JsonConverter<ShapeWrapper>
	{
		public override void WriteJson(JsonWriter writer, ShapeWrapper value, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}
		
		Shape AllocateShape(ShapeType shapeType,JObject ShapeObject,JsonSerializer serializer)
		{
			switch (shapeType)
			{
				case ShapeType.Ellipse:		return ShapeObject.ToObject<ShapeEllipse>(serializer);
				case ShapeType.Fill:		return ShapeObject.ToObject<ShapeFillAndStroke>(serializer);
				case ShapeType.Stroke:		return ShapeObject.ToObject<ShapeFillAndStroke>(serializer);
				case ShapeType.Transform:	return ShapeObject.ToObject<ShapeTransform>(serializer);
				case ShapeType.Group:		return ShapeObject.ToObject<ShapeGroup>(serializer);
				case ShapeType.Path:		return ShapeObject.ToObject<ShapePath>(serializer);
				case ShapeType.TrimPath:	return ShapeObject.ToObject<ShapeTrimPath>(serializer);
				case ShapeType.Rectangle:	return ShapeObject.ToObject<ShapeRectangle>(serializer);
				case ShapeType.Merge:		return ShapeObject.ToObject<ShapeMerge>(serializer);
				
				default:
					throw new Exception($"AllocateShape Unhandled shape type {shapeType}");
			}
		}
		
		public override ShapeWrapper ReadJson(JsonReader reader, Type objectType, ShapeWrapper existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			var ShapeObject = JObject.Load(reader);
			var ShapeBase = new ShapeMetaOnly();
			ShapeBase.ty = ShapeObject["ty"].Value<String>();
			
			//	now based on type, serialise
			var OutputShape = AllocateShape(ShapeBase.Type,ShapeObject,serializer);
			existingValue.TheShape = OutputShape;
			
			return existingValue;
		}
	}
	
	[JsonConverter(typeof(ShapeConvertor))]
	[Serializable] struct ShapeWrapper 
	{
		public Shape		TheShape;
		public ShapeType	Type => TheShape.Type; 
		public bool			IsStatic()	{	return TheShape.IsStatic();	}
	}
		
	[Serializable]
	public struct TextStyle
	{
		public AnimatedNumber	sw;	//	stroke width
		public AnimatedColour	sc;	//	stroke colour
		public AnimatedNumber	sh;	//	stroke hue
		public AnimatedNumber	ss;	//	stroke saturation
		public AnimatedNumber	sb;	//	Stroke Brightness
		public AnimatedNumber	so;	//	Stroke Opacity
		public AnimatedColour	fc;	//	Fill Color
		public AnimatedNumber	fh;	//	Fill Hue
		public AnimatedNumber	fs;	//	Fill Saturation
		public AnimatedNumber	fb;	//	Fill Brightness
		public AnimatedNumber	fo;	//	Fill Opacity
		public AnimatedNumber	t;	//	Letter Spacing
		public AnimatedNumber	bl;	//	Blur
		public AnimatedNumber	ls;	//	Line spacing
	}

	[Serializable]
	public struct TextRangeSelector
	{
	}

	[Serializable]
	public struct TextRange
	{
		public String				nm;
		public TextRangeSelector	s;
		public TextStyle			a;
	}

	[Serializable]
	public struct TextDocument
	{
		public String	f;		//	font family
		public String	FontFamily => f;
		public float[]	fc;
		public Color	FillColour => new Color( fc[0], fc[1], fc[2] );
		public Color	GetFillColour(float Alpha) {	return new Color(fc[0], fc[1], fc[2], Alpha );	}

		public float[]	sc;
		public Color	StrokeColour => new Color( sc[0], sc[1], sc[2] );
		public Color	GetStrokeColour(float Alpha) {	return new Color(sc[0], sc[1], sc[2], Alpha );	}
		public float	sw;
		public float	StrokeWidth => sw;
		public bool		of;
		public bool		RenderStrokeAboveFill => of;
		public float	s;
		public float	FontSize => s;
		//	line height (distance between lines on multine or wrapped text)
		public float	lh;
		public float	LineHeight => lh;
		
		public float[]	sz;	//	size of containing text box
		public float[]	ps;	//	position of text box
		public String	t;	//	text seperated with \r newlines
		public String[]	TextLines => t.Split('\r');
		public int		j;
		public TextJustify	Justify => (TextJustify)j;
		
		
		public ShapeStyle		GetShapeStyle(float Alpha)
		{
			var Style = new ShapeStyle();
			Style.FillColour = this.GetFillColour(Alpha);
			Style.StrokeColour = this.GetStrokeColour(Alpha);
			Style.StrokeWidth = this.StrokeWidth;
			return Style;
		}
	}

	[Serializable]
	public struct TextDocumentKeyframe
	{
		public TextDocument		s;
		public TextDocument		Text => s;
		
		//	time (appearance only? where is end?)
		public float			t;
		public FrameNumber		Time => t;
	}

	[Serializable]
	public struct AnimatedTextDocument
	{
		public TextDocumentKeyframe[]	k;
		public TextDocumentKeyframe[]	Keyframes => k;
		public String					x;	//	expression
		public String					sid;	//	string id?
	}


	[Serializable]
	struct TextData
	{
		public AnimatedTextDocument	d;
	/*
		public TextRange[]			a;
		public TextAlignment		m;
		public TextFollowPath		p;
		*/
	}
	

	[Serializable] public abstract class Shape 
	{
		public int			ind;//	?
		public int			np;		//	number of properties
		public int			cix;	//	property index
		public int			ix;		//	property index
		public int			bm;		//	blend mode
		public String		nm;		// = "Lottie File"
		public String		Name => nm ?? "Unnamed";
		public String		mn;
		public String		MatchName => mn;
		public bool			hd;	//	i think sometimes this might an int. Newtonsoft is very strict with types
		public bool			Hidden => hd;
		public bool			Visible => !Hidden;
		public String		ty;	
		internal ShapeType	Type => ty switch
		{
			"gr" => ShapeType.Group,
			"sh" => ShapeType.Path,
			"fl" => ShapeType.Fill,
			"tr" => ShapeType.Transform,
			"st" => ShapeType.Stroke,
			"el" => ShapeType.Ellipse,
			"tm" => ShapeType.TrimPath,
			"rc" => ShapeType.Rectangle,
			"mm" => ShapeType.Merge,
			_ => throw new Exception($"Unknown shape type {ty}")
		};
		
		public abstract bool IsStatic();
	}
	
	//	only used in decoder to get base meta, but allows us to keep Shape abstract
	[Serializable] class ShapeMetaOnly : Shape
	{
		public override bool IsStatic()
		{
			throw new Exception($"An instance of this class should never get to a point where it's tested for being static");
		}
	}
	
	[Serializable] public class ShapeMerge : Shape
	{
		public int		mm;
		public int		MergeMode => mm;	//	todo: enum
		
		public override bool	IsStatic()	
		{
			return true;
		}
	}
	
	[Serializable] public class ShapeRectangle : Shape
	{
		public AnimatedVector p;
		public AnimatedVector s;
		public AnimatedVector r;
		public AnimatedVector Center => p;
		public AnimatedVector Size => s;
		public AnimatedVector CornerRadius => r;
		
		public override bool	IsStatic()	
		{
			if ( !p.IsStatic() )
				return false;
			if ( !s.IsStatic() )
				return false;
			if ( !r.IsStatic() )
				return false;
			return true;
		}
	}
	
	[Serializable] public class ShapePath : Shape
	{
		public AnimatedBezier	ks;	//	bezier for path
		public AnimatedBezier	Path_Bezier => ks;
		
		public override bool	IsStatic()	
		{
			return Path_Bezier.IsStatic();
		}
	}
	
	public struct PathTrim
	{
		public float	Start;
		public float	End;
		public float	Offset;
		
		public bool		IsDefault => Start==0f && End==1f;
		public static PathTrim GetDefault()
		{
			PathTrim Default;
			Default.Start = 0;
			Default.Offset = 0;
			Default.End = 1f;
			return Default;
		}
	}
	
	[Serializable] public class ShapeTrimPath : Shape
	{
		public AnimatedNumber	s;	//	segment start
		public AnimatedNumber	e;	//	segment end
		public AnimatedNumber	o;	//	offset
		public int				m;
		public int				TrimMultipleShapes => m;
		
		public override bool	IsStatic()
		{
			if ( !s.IsStatic() )
				return false;
			if ( !e.IsStatic() )
				return false;
			if ( !o.IsStatic() )
				return false;
			return true;
		}
		
		public PathTrim			GetTrim(FrameNumber Frame)
		{
			//	https://lottiefiles.github.io/lottie-docs/shapes/#trim-path
			//	start & end is 0-100%, offset is an angle up to 360
			var Trim = PathTrim.GetDefault();
			Trim.Start = s.GetValue(Frame) / 100f;
			Trim.End = e.GetValue(Frame) / 100f;
			Trim.Offset = o.GetValue(Frame) / 360f;	
			return Trim;
		}
	}
		
				
	[Serializable] public class ShapeFillAndStroke : Shape 
	{
		public AnimatedColour	c;	//	colour
		public AnimatedNumber	o;
		public AnimatedNumber	Opacity => o;

		//	Fill
		public AnimatedColour	Fill_Colour => c;
		public int				r;	//	fill rule
		public AnimationFillRule	FillRule => r switch
		{
			1 => AnimationFillRule.NonZero,
			2 => AnimationFillRule.EvenOdd,
			_ => AnimationFillRule.NonZero
		};
		
		//	Stroke
		public AnimatedNumber	w;	//	width
		public int				lc;
		public int				lj;
		public AnimatedColour	Stroke_Colour => c;
		public AnimatedNumber	Stroke_Width => w;
		public AnimationLineCap	LineCap => lc switch
		{
			1 => AnimationLineCap.Butt,
			2 => AnimationLineCap.Round,
			3 => AnimationLineCap.Square,
			_ => AnimationLineCap.Butt,
		};
		public AnimationLineJoin	LineJoin => lj switch
		{
			1 => AnimationLineJoin.Miter,
			2 => AnimationLineJoin.Round,
			3 => AnimationLineJoin.Bevel,
			_ => AnimationLineJoin.Miter,
		};
		
		public override bool	IsStatic()
		{
			if ( !Fill_Colour.IsStatic() )
				return false;
			if ( !Stroke_Colour.IsStatic() )
				return false;
			if ( !Stroke_Width.IsStatic() )
				return false;
			return true;
		}
		
		public float			GetStrokeWidth(FrameNumber Frame)
		{
			var Value = w.GetValue(Frame);
			return Value;
		}
		public Color			GetColour(FrameNumber Frame)
		{
			var Opacity = o.GetValue(Frame) / 100.0f;
			var Colour = c.GetColour(Frame);
			Colour.a *= Opacity;
			return Colour;
		}
	}
		
		
	[Serializable] public class ShapeTransform : Shape 
	{
		//	transform
		public AnimatedVector	p;	//	translation
		public AnimatedVector	a;	//	anchor
		
		//	gr: not parsing as mix of animated & not
		public AnimatedVector	s;	//	scale
		public AnimatedVector	r;	//	rotation
		public AnimatedNumber?	o;	//	opacity
		
		public override bool	IsStatic()
		{
			if ( !p.IsStatic() )	
				return false;
			if ( !a.IsStatic() )	
				return false;
			if ( !s.IsStatic() )	
				return false;
			if ( !r.IsStatic() )	
				return false;
			if ( o is AnimatedNumber opactity ) 
				if ( !opactity.IsStatic() )	
					return false;
			return true;
		}
		
		public Transformer	GetTransformer(FrameNumber Frame)
		{
			var Anchor = a.GetValueVec2(Frame);
			var Position = p.GetValueVec2(Frame);
			var FullScale = new Vector2(100,100);
			var Scale = s.GetValueVec2(Frame) /FullScale;
			var Rotation = r.GetValue(Frame);
			return new Transformer( Position, Anchor, Scale, Rotation );
		}
		
		public float GetAlpha(FrameNumber Frame)
		{
			if ( o is AnimatedNumber opacity )
			{
				var Opacity = opacity.GetValue(Frame);
				float Alpha = Opacity / 100.0f;
				return Alpha;
			}
			return 100.0f;
		}
	}
	
	
	[Serializable] public class ShapeEllipse : Shape 
	{
		public AnimatedVector	s;
		public AnimatedVector	p;
		public AnimatedVector	Size => s;	
		public AnimatedVector	Center => p;	
		
		public override bool	IsStatic()
		{
			if ( !Size.IsStatic() )
				return false;
			if ( !Center.IsStatic() )
				return false;
			return true;
		}
		
		
	}
	
	


	//	struct ideally, but to include pointer to parent, can't be a struct
	public class Transformer
	{
		public Transformer	Parent = null;
		Vector2				Scale = Vector2.one;
		Vector2				Translation;
		Vector2				Anchor;
		float				RotationDegrees;
		
		public Transformer()
		{
		}
		
		public Transformer(Vector2 Translation,Vector2 Anchor,Vector2 Scale,float RotationDegrees)
		{
			this.Translation = Translation;
			this.Anchor = Anchor;
			this.Scale = Scale;
			this.Parent = null;
			this.RotationDegrees = RotationDegrees;
		}

		Vector2	LocalToParentPosition(Vector2 LocalPosition)
		{
			//	0,0 anchor and 0,0 translation is topleft
			//	20,0 anchor and 0,0 position, makes 0,0 offscreen (-20,0) 
			//	anchor 20, pos 100, makes 0,0 at 80,0
			//	scale applies after offset
			LocalPosition -= Anchor;
			LocalPosition = Quaternion.AngleAxis(RotationDegrees, Vector3.forward) * LocalPosition;
			//	apply rotation here
			LocalPosition *= Scale;
			LocalPosition += Translation;
			return LocalPosition;
		}
		
		Vector2	LocalToParentSize(Vector2 LocalSize)
		{
			LocalSize *= Scale;
			return LocalSize;
		}
		
		public Vector2	LocalToWorldPosition(Vector2 LocalPosition)
		{
			var ParentPosition = LocalToParentPosition(LocalPosition);
			var WorldPosition = ParentPosition;
			if ( Parent is Transformer parent )
			{
				WorldPosition = parent.LocalToWorldPosition(ParentPosition);
			}
			return WorldPosition;
		}
		public Rect	LocalToWorldPosition(Rect LocalRect)
		{
			var ParentMin = LocalToParentPosition(LocalRect.min);
			var ParentMax = LocalToParentPosition(LocalRect.max);
			var WorldMin = ParentMin;
			var WorldMax = ParentMax;
			if ( Parent is Transformer parent )
			{
				WorldMin = parent.LocalToWorldPosition(ParentMin);
				WorldMax = parent.LocalToWorldPosition(ParentMax);
			}
			return Rect.MinMaxRect( WorldMin.x, WorldMin.y,WorldMax.x,WorldMax.y);
		}
		
		public Vector2	LocalToWorldSize(Vector2 LocalSize)
		{
			var ParentSize = LocalToParentSize(LocalSize);
			var WorldSize = ParentSize;
			if ( Parent is Transformer parent )
			{
				WorldSize = parent.LocalToWorldSize(ParentSize);
			}
			return WorldSize;
		}
		public float	LocalToWorldSize(float LocalSize)
		{
			//	expected to be used in 1D cases anyway
			var Size2 = new Vector2(LocalSize,LocalSize);
			Size2 = LocalToWorldSize(Size2);
			return Size2.x;
		}
		
	}

	[Serializable] class ShapeGroup: Shape 
	{
		public List<ShapeWrapper>		it;	//	children
		public IEnumerable<Shape>		ChildrenFrontToBack => it.Select( sw => sw.TheShape );
		public IEnumerable<Shape>		ChildrenBackToFront => ChildrenFrontToBack.Reverse();
		
		public override bool	IsStatic()
		{
			foreach (var Child in it )
			{
				if ( !Child.IsStatic() )
					return false;
			}
			return true;
		}

		Shape				GetChild(ShapeType MatchType)
		{
			//	handle multiple instances
			foreach (var s in it)//Children)
			{
				if ( s.Type == MatchType )
					return s.TheShape;
			}
			return null;
		}
		public Transformer		GetTransformer(FrameNumber Frame)
		{
			var Transform = GetChild(ShapeType.Transform) as ShapeTransform;
			if ( Transform == null )
				return new Transformer();
			return Transform.GetTransformer(Frame);
		}
		
		//	this comes from the transform, but we're just not keeping it with it
		public float		GetAlpha(FrameNumber Frame)
		{
			var Transform = GetChild(ShapeType.Transform) as ShapeTransform;
			if ( Transform == null )
				return 1.0f;
			return Transform.GetAlpha(Frame);
		}
		
		public PathTrim	GetPathTrim(FrameNumber Frame)
		{
			var TrimShape = GetChild(ShapeType.TrimPath) as ShapeTrimPath;
			if ( TrimShape != null )
			{
				return TrimShape.GetTrim(Frame);
			}
			return PathTrim.GetDefault();
		}
		
		public ShapeStyle?		GetShapeStyle(FrameNumber Frame)
		{
			var Fill = GetChild(ShapeType.Fill) as ShapeFillAndStroke;
			var Stroke = GetChild(ShapeType.Stroke) as ShapeFillAndStroke;
			var Style = new ShapeStyle();
			if ( Fill != null )
			{
				Style.FillColour = Fill.GetColour(Frame);
				Style.FillRule = Fill.FillRule;
			}
			if ( Stroke != null )
			{
				Style.StrokeColour = Stroke.GetColour(Frame);
				Style.StrokeWidth = Stroke.GetStrokeWidth(Frame);
				Style.StrokeLineCap = Stroke.LineCap;
				Style.StrokeLineJoin = Stroke.LineJoin;
			}
			if ( Fill == null && Stroke == null )
				return null;
			return Style;
		}

	}
	

	
	[Serializable]
	struct LayerMeta	//	shape layer
	{
		public bool		IsVisible(FrameNumber Frame)
		{
			if ( Frame < FirstKeyFrame )
				return false;
			if ( Frame > LastKeyFrame )
				return false;
			/*
			if ( Time < StartTime )
				return false;
				*/
			return true;
		}
		
		public bool IsStatic()
		{
			if ( !Transform.IsStatic() )
				return false;
			
			//	gr: if we have a parent, we need to know if that parent is static...
			//	todo: provide a way to tell! (do we need to iterate the tree twice? :/)
			if ( HasParent )
			{
				//if ( !ParentIsStatic )
				return false;
			}

			foreach (var Shape in shapes)
			{
				if ( !Shape.IsStatic() )
					return false;
			}
			return true;
		}
	
		public float				ip;
		public int					FirstKeyFrame => (int)ip;	//	visible after this
		public float				op;	//	= 10
		public int					LastKeyFrame => (int)op;		//	invisible after this (time?)
		
		public String				nm;// = "Lottie File"
		public String				Name => nm ?? "Unnamed";

		public String				refId;
		public String				ResourceId => refId ?? "";
		public int					ind;
		public int					LayerId => ind;	//	for parenting
		public int?					parent;
		public bool					HasParent => parent !=null;
		
		public float				st;
		public double				StartTime => st;

		public int					ddd;
		public bool					ThreeDimensions => ddd == 3;
		public int					ty;
		public LayerType			LayerType => (LayerType)ty;
		public int					sr;
		public ShapeTransform		ks;
		public ShapeTransform		Transform=>ks;	//	gr: this is not really a shape, but has same properties & interface (all the derived parts in ShapeTransform)
		public int					ao;
		public bool					AutoOrient => ao != 0;
		
		public int					bm;
		public int					BlendMode => bm;

		//	shape-group layer
		public ShapeWrapper[]		shapes;
		public ShapeWrapper[]		ShapeChildren => shapes ?? Array.Empty<ShapeWrapper>();
		public IEnumerable<Shape>	ChildrenFrontToBack => ShapeChildren.Select( sw => sw.TheShape );
		public IEnumerable<Shape>	ChildrenBackToFront => ChildrenFrontToBack.Reverse();
		
		//	text layers
		public TextData?			t;
		public TextData?			Text => t;
	}
	
	[Serializable]
	public struct MarkerMeta
	{/*
		public var cm : String
		public var id : String		{ return Name }
		public var Name : String	{	return cm	}
		public var tm : Int
		public var Frame : Int	{	return tm	}
		public var dr : Int
		*/
	}
	
		
	[Serializable]
	struct Root
	{
		const float			DefaultFramesPerSecond = 60;

		public TimeSpan	FrameToTime(FrameNumber Frame)
		{
			Frame -= FirstKeyFrame;
			return TimeSpan.FromSeconds(Frame/ FramesPerSecond);
		}
		//	gr: output is really float, but trying int for simplicity for a moment...
		public FrameNumber		TimeToFrame(TimeSpan Time,bool Looped)
		{
			var Duration = this.Duration.TotalSeconds;
			if ( Duration <= 0 )
				return 0;
			var TimeSecs = Looped ? TimeSpan.FromSeconds(Time.TotalSeconds % Duration) : TimeSpan.FromSeconds(Mathf.Min((float)Time.TotalSeconds,(float)Duration));
			var Frame = (TimeSecs.TotalSeconds * FramesPerSecond);
			Frame += FirstKeyFrame;
			return (FrameNumber)Frame;
		}
		
		public bool IsStatic()
		{
			//	if we have only one frame... we must be static?
			if ( this.FirstKeyFrame >= this.LastKeyFrame )
				return true;
			
			//	look for any non static layers
			foreach (var layer in layers )
			{
				if ( !layer.IsStatic() )
					return false;
			}
			return true;
		}
		
		public bool HasAnyTextLayers()
		{
			foreach (var layer in Layers )
			{
				if ( layer.LayerType == LayerType.Text )
					return true;
			}
			return false;
		}
		

		public string	v;	//"5.9.2"
		public float	fr;
		public float	FramesPerSecond => fr <= 0 ? DefaultFramesPerSecond : fr;
		public float	ip;
		public int		FirstKeyFrame => (int)ip;
		public TimeSpan	FirstKeyFrameTime => FrameToTime(FirstKeyFrame);
		public float	op;	//	= 10
		public int		LastKeyFrame => (int)op;
		public TimeSpan	LastKeyFrameTime => FrameToTime(LastKeyFrame);
		public TimeSpan	Duration => LastKeyFrameTime - FirstKeyFrameTime;
		public int		w;//: = 100
		public int		h;//: = 100
		public String	nm;// = "Lottie File"
		public String	Name => nm ?? "Unnamed";
		public int		ddd;	// = 0	//	not sure what this is, but when it's 3 "things are reversed"
			
		public AssetMeta[]	assets;
		public LayerMeta[]	layers;
		public LayerMeta[]	LayersFrontToBack => layers;
		public LayerMeta[]	LayersBackToFront => LayersFrontToBack.Reverse().ToArray();
		public MarkerMeta[]	markers;

		public AssetMeta[]	Assets => assets ?? Array.Empty<AssetMeta>();
		public LayerMeta[]	Layers => layers ?? Array.Empty<LayerMeta>();
		public MarkerMeta[]	Markers => markers ?? Array.Empty<MarkerMeta>();
	}
	
	public class LottieAnimation : Animation
	{
		Root					lottie;
		bool					IsStaticCache;
		bool					HasTextLayersCache;
		public override bool	IsStatic => IsStaticCache;
		public override bool	HasTextLayers => HasTextLayersCache;

		public LottieAnimation(string FileContents)
		{
			//	gr: can't use built in, as the structure changes depending on contents, and end up with clashing types
			//lottie = JsonUtility.FromJson<Root>(FileContents);
			//	can't use the default deserialiser, because for some reason, the parser misses out parsing
			//	[ {}, {} ] 
			//lottie = Newtonsoft.Json.JsonConvert.DeserializeObject<Root>(FileContents);
			
			//	we CAN parse with generic parser!
			var Parsed = JObject.Parse(FileContents);
			
			JsonSerializer serializer = new JsonSerializer();
			
			
			lottie = (Root)serializer.Deserialize(new JTokenReader(Parsed), typeof(Root));
			IsStaticCache = lottie.IsStatic();
			HasTextLayersCache = lottie.HasAnyTextLayers();
			//Debug.Log($"Decoded lottie ok x{lottie.layers.Length} layers; static={IsStatic}");
		}
		
		public override TimeSpan	Duration => lottie.Duration;
		public override int			FrameCount => lottie.LastKeyFrame-lottie.FirstKeyFrame;
		public override FrameNumber	TimeToFrame(TimeSpan Time,bool Looped)
		{
			return lottie.TimeToFrame(Time,Looped);
		}
		public override TimeSpan	FrameToTime(FrameNumber Frame)
		{
			return lottie.FrameToTime(Frame);
		}

		public override void Dispose()
		{
			lottie = default;
		}
		
		static public (Transformer,Rect) DoRootTransform(Rect AssetCanvasRect,Rect ContentRect,ScaleMode scaleMode)
		{
			if ( AssetCanvasRect.width <= 0 || AssetCanvasRect.height <= 0 )
			{
				Debug.LogWarning($"Vector with Canvas size {AssetCanvasRect.x},{AssetCanvasRect.y}->{AssetCanvasRect.width}x{AssetCanvasRect.height}. Reverting to 0,0,100,100");
				AssetCanvasRect = new Rect(0,0,100,100);
			}
			
			//	scale-to-canvas transformer
			float ExtraScale = 1;	//	for debug zooming
			var ScaleToCanvasWidth = (ContentRect.width / AssetCanvasRect.width)*ExtraScale;
			var ScaleToCanvasHeight = (ContentRect.height / AssetCanvasRect.height)*ExtraScale;
			bool Stretch = scaleMode == ScaleMode.StretchToFill;
			
			//	todo: handle scale + crop (scale up)
			//	todo: fit height or width, whichever is smaller
			bool FitHeight = ScaleToCanvasHeight <= ScaleToCanvasWidth;
			if ( scaleMode == ScaleMode.ScaleAndCrop )
				FitHeight = !FitHeight;
			
			var ScaleToCanvasUniform = FitHeight ? ScaleToCanvasHeight : ScaleToCanvasWidth;
			var ScaleToCanvas = Stretch ? new Vector2( ScaleToCanvasWidth, ScaleToCanvasHeight ) : new Vector2( ScaleToCanvasUniform, ScaleToCanvasUniform );
			
			//	gr: work this out properly....
			bool CenterAlign = true;
			Transformer RootTransformer = new Transformer( ContentRect.min, Vector2.zero, ScaleToCanvas, 0f );
			var OutputCanvasRect = RootTransformer.LocalToWorldPosition(AssetCanvasRect);
			if ( CenterAlign )
			{
				var Centering = (ContentRect.max - OutputCanvasRect.max)/2f;
				RootTransformer = new Transformer( ContentRect.min + Centering, Vector2.zero, ScaleToCanvas, 0f );
				//	re-write canvas to make sure this is correct
				OutputCanvasRect = RootTransformer.LocalToWorldPosition(AssetCanvasRect);
			}
			return (RootTransformer,OutputCanvasRect);
		}
		
		public override RenderCommands.AnimationFrame Render(FrameNumber Frame, Rect ContentRect,ScaleMode scaleMode)
		{
			//Debug.Log($"Time = {Time.TotalSeconds} ({lottie.FirstKeyframe.TotalSeconds}...{lottie.LastKeyframe.TotalSeconds})");
			//	work out the placement of the canvas - all the shapes are in THIS canvas space
			Rect LottieCanvasRect = new Rect(0,0,lottie.w,lottie.h);

			var (RootTransformer,OutputCanvasRect) = DoRootTransform( LottieCanvasRect, ContentRect, scaleMode );

			var OutputFrame = new RenderCommands.AnimationFrame();
			OutputFrame.CanvasRect = OutputCanvasRect;

			void AddRenderShape(RenderCommands.Shape NewShape)
			{
				OutputFrame.AddShape(NewShape);
			}
			
			
			List<RenderCommands.Path> CurrentPaths = new();
			void BeginShape()
			{
				//	clean off old shape
				if ( CurrentPaths.Count != 0 )
					throw new Exception("Finished off old shape?");
			}
			void FinishLayerShape()
			{
				//	clean off old shape
				if ( CurrentPaths.Count != 0 )
				{
					throw new Exception("Finished off old shape?");
				}
			}
			
			void RenderText(TextData Text,Transformer ParentTransform,float LayerAlpha,string LayerName)
			{
				foreach ( var TextFrame in Text.d.Keyframes )
				{
					var Style = TextFrame.Text.GetShapeStyle(LayerAlpha);
					Style.StrokeWidth = ParentTransform.LocalToWorldSize(Style.StrokeWidth??0);

					var Paths = new List<RenderCommands.Path>();

					//	gr: need to generate a transform specifically for glyphs here hmm
					var LinePosition = new Vector2(0,0);
					var FontSize = ParentTransform.LocalToWorldSize(TextFrame.Text.FontSize);
					foreach ( var Line in TextFrame.s.TextLines )
					{
						var WorldPosition = ParentTransform.LocalToWorldPosition(LinePosition);
						var TextPath = new AnimationText(Text: Line, FontName: TextFrame.Text.FontFamily, FontSize: FontSize, Position: WorldPosition );
						TextPath.Justify = TextFrame.s.Justify;
						var Path = new RenderCommands.Path(TextPath);
						Paths.Add( Path );
						//	gr: need to scale this too?
						LinePosition.y += TextFrame.Text.LineHeight;
					}
					var Shape = new RenderCommands.Shape(Paths: Paths.ToArray(), Name:LayerName, Style:Style);
					AddRenderShape(Shape);
				}
			}

			void RenderGroup(ShapeGroup Group,Transformer ParentTransform,float LayerAlpha)
			{
				//	run through sub shapes
				var Children = Group.ChildrenBackToFront;

				//	elements (shapes) in the layer may be in the wrong order, so need to pre-extract style & transform
				var GroupTransform = Group.GetTransformer(Frame);
				GroupTransform.Parent = ParentTransform;
				var GroupStyleMaybe = Group.GetShapeStyle(Frame);
				var GroupStyle = GroupStyleMaybe ?? new ShapeStyle();
				var GroupAlpha = Group.GetAlpha(Frame) * LayerAlpha;
				GroupStyle.MultiplyAlpha(GroupAlpha);
				
				void AddPath(RenderCommands.Path NewPath)
				{
					CurrentPaths.Add(NewPath);
				}
				
				
				void FinishShape()
				{
					var ShapeStyle = GroupStyle;
					var StrokeWidth = GroupTransform.LocalToWorldSize( GroupStyle.StrokeWidth ?? 1 );
					ShapeStyle.StrokeWidth = StrokeWidth;
					
					var NewShape = new RenderCommands.Shape();
					NewShape.Paths = CurrentPaths.ToArray();
					NewShape.Style = ShapeStyle;
					
					CurrentPaths = new();
					AddRenderShape(NewShape);
				}
				
				void RenderChild(Shape Child)
				{
					//	force visible with debug
					if ( !Child.Visible )
						return;
				
					if ( Child is ShapePath path )
					{
						var Trim = Group.GetPathTrim(Frame);
						var Bezier = path.Path_Bezier.GetBezier(Frame);
						var Points = Bezier.GetControlPoints(Trim);
						var RenderPoints = new List<RenderCommands.BezierPoint>();
						
						void CurveToPoint(Bezier.ControlPoint Point,Bezier.ControlPoint PrevPoint)
						{
							//	gr: working out this took quite a bit of time.
							//		the cubic bezier needs 4 points; Prev(start), tangent for first half of line(start+out), tangent for 2nd half(end+in), and the end
							var cp0 = PrevPoint.Position + PrevPoint.OutTangent;
							var cp1 = Point.Position + Point.InTangent;
							
							var VertexPosition = GroupTransform.LocalToWorldPosition(Point.Position);
							var ControlPoint0 = GroupTransform.LocalToWorldPosition(cp0);
							var ControlPoint1 = GroupTransform.LocalToWorldPosition(cp1);
							
							var BezierPoint = new RenderCommands.BezierPoint();
							BezierPoint.ControlPointIn = ControlPoint0;
							BezierPoint.ControlPointOut = ControlPoint1;
							BezierPoint.End = VertexPosition;
							RenderPoints.Add(BezierPoint);
						}
						
						for ( var p=0;	p<Points.Length;	p++ )
						{
							var PrevIndex = (p==0 ? Points.Length-1 : p-1);
							var Point = Points[p];
							var PrevPoint = Points[PrevIndex];
							var VertexPosition = GroupTransform.LocalToWorldPosition(Point.Position);
							//	skipping first one gives a more solid result, so wondering if
							//	we need to be doing a mix of p and p+1...
							if ( p==0 )
							{
								var BezierPoint = new RenderCommands.BezierPoint();
								BezierPoint.ControlPointIn = VertexPosition;
								BezierPoint.ControlPointOut = VertexPosition;
								BezierPoint.End = VertexPosition;
								RenderPoints.Add(BezierPoint);
							}
							else
								CurveToPoint(Point,PrevPoint);
						}
						
						if ( Bezier.Closed && Points.Length > 1 )
						{
							if ( Trim.IsDefault )
							CurveToPoint( Points[0], Points[Points.Length-1] );
						}
						
						AddPath( new RenderCommands.Path(RenderPoints) );
					}
					if ( Child is ShapeEllipse ellipse )
					{
						var RenderEllipse = new RenderCommands.Ellipse();
						var EllipseSize = GroupTransform.LocalToWorldSize(ellipse.Size.GetValueVec2(Frame));
						var LocalCenter = ellipse.Center.GetValueVec2(Frame);
						var EllipseCenter = GroupTransform.LocalToWorldPosition(LocalCenter);
						
						RenderEllipse.Center = EllipseCenter;
						//	gr: this appears to be diameter, not radius
						RenderEllipse.Radius = EllipseSize * 0.5f;
						AddPath( new RenderCommands.Path(RenderEllipse) );
					}
					
					if ( Child is ShapeRectangle rectangle )
					{
						var Center = GroupTransform.LocalToWorldPosition(rectangle.Center.GetValueVec2(Frame));
						var Size = GroupTransform.LocalToWorldSize(rectangle.Size.GetValueVec2(Frame));
						var CornerRadius = GroupTransform.LocalToWorldSize(rectangle.CornerRadius.GetValue(Frame));
						
						//	create a path, in the shape of a rectangle!
						//	this is generic, so for future format support, the generic code is in the output shape code
						var Path = RenderCommands.Path.CreateRect( Center, Size, CornerRadius );
						AddPath( Path );
					}
			
					if ( Child is ShapeGroup subgroup )
					{
						try
						{
							RenderGroup(subgroup,GroupTransform,GroupAlpha);
						}
						catch(Exception e)
						{
							Debug.LogException(e);
						}
					}
				}
				
				//	gr: we need to break paths when styles change
				//		but if we have layer->shape->group->group->shape we need to NOT break paths
				if ( GroupStyleMaybe.HasValue )
				{
					BeginShape();
				}
				
				foreach ( var Child in Children )
				{
					try
					{
						RenderChild(Child);
					}
					catch(Exception e)
					{
						Debug.LogException(e);
					}
				}
				
				if ( GroupStyleMaybe.HasValue )
				{
					FinishShape();
				}
			}
		
			//	layers go front to back
			foreach ( var Layer in lottie.LayersBackToFront )
			{
				if ( !Layer.IsVisible(Frame) )
					continue;
				
				Transformer ParentTransformer = RootTransformer;
				if ( Layer.parent.HasValue )
				{
					var ParentLayers = lottie.layers.Where( l => l.LayerId == Layer.parent.Value ).ToArray();
					if ( ParentLayers.Length != 1 )
					{
						Debug.LogWarning($"Too few or too many parent layers for {Layer.Name} (parent={Layer.parent})");
					}
					else
					{
						var ParentLayerTransform = ParentLayers[0].Transform.GetTransformer(Frame);
						ParentLayerTransform.Parent = ParentTransformer;
						ParentTransformer = ParentLayerTransform; 
					}
				}
				
				var LayerTransform = Layer.Transform.GetTransformer(Frame);
				LayerTransform.Parent = ParentTransformer;
				var LayerOpacity = Layer.Transform.GetAlpha(Frame);
				
				//	skip hidden layers
				if ( LayerOpacity <= 0 )
				{
					continue;
				}

				BeginShape();
				
				//	if Layer.type == LayerType.Text
				if ( Layer.Text is TextData text )
				{
					try
					{
						RenderText( text, LayerTransform, LayerOpacity, Layer.Name );
						//RenderGroup(group,LayerTransform,LayerOpacity);
					}
					catch(Exception e)
					{
						Debug.LogException(e);
					}
				}
				
				//	gr: if Layer.type == LayerType.Shape
				//	render the shape
				foreach ( var Shape in Layer.ChildrenBackToFront )
				{
					try
					{
						if ( Shape is ShapeGroup group )
						{
							RenderGroup(group,LayerTransform,LayerOpacity);
						}
						else
						{
							//	it's possible to have ungrouped shapes!
							//RenderChild(Shape);
							Debug.Log($"Not a group... {typeof(Shape)}");
						}
					}
					catch(Exception e)
					{
						Debug.LogException(e);
					}
				}
				FinishLayerShape();
			}
			
			return OutputFrame;
		}
		
	}
	
}

