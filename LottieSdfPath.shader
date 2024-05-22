Shader "PopLottie/LottieSdfPath"
{
	Properties
	{
		Debug_StrokeScale ("Debug_StrokeScale", Range(1.0, 20.0)) = 1.0
		Debug_ForceStrokeMin ("Debug_ForceStrokeMin", Range(0.0, 1.0)) = 0.0

		Debug_BezierDistanceOffset ("Debug_BezierDistanceOffset", Range(0.0, 1.0)) = 1.0
	}
	SubShader
	{
		Tags { "RenderType"="Transparent" }
		LOD 100
		Cull off   
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 LocalPosition : POSITION;
				float2 QuadUv : TEXCOORD0;	//	not sure we need this, all shape info is gonna be in local space
				float4 FillColour : COLOR;
				float4 StrokeColour : TEXCOORD1;
				float4 PathMeta : TEXCOORD2;
			};

			struct v2f
			{
				float4 ClipPosition : SV_POSITION;
				float2 LocalPosition : TEXCOORD0;
				float4 FillColour : TEXCOORD1;
				float4 StrokeColour : TEXCOORD2;
				float StrokeWidth : TEXCOORD3;
				int PathDataIndex : TEXCOORD4;
			};

			#define ENABLE_DEBUG_INVISIBLE	false
			#define OUTSIDE_COLOUR			(ENABLE_DEBUG_INVISIBLE ? float4(0,1,1,0.1) : float4(0,0,0,0) )
			#define NULL_PATH_COLOUR		(ENABLE_DEBUG_INVISIBLE ? float4(1,0,0,0.1) : float4(0,0,0,0) )
#define NULL_DISTANCE	999
#define DEBUG_CONTROLPOINT_SIZE	0.01
#define DEBUG_BEZIER_CONTROLPOINTS true

			float Debug_StrokeScale;
			float Debug_ForceStrokeMin;
			float Debug_BezierDistanceOffset;

			//	todo: how to represent bezier paths...
			#define PATH_DATA_COUNT			300
			#define PATH_DATATYPE_UNINITIALISED	-1
			#define PATH_DATATYPE_NULL		0
			#define PATH_DATATYPE_ELLIPSE	1
			#define PATH_DATATYPE_BEZIER	2
			#define PATH_DATAROW_META		0
			#define PATH_DATAROW_POSITION	1


			uniform float4x4 PathDatas[PATH_DATA_COUNT];



			v2f vert (appdata v)
			{
				v2f o;
				o.ClipPosition = UnityObjectToClipPos(v.LocalPosition);
				o.LocalPosition = v.LocalPosition;
				o.FillColour = v.FillColour;
				o.StrokeColour = v.StrokeColour;
				o.PathDataIndex = v.PathMeta.x;
				o.StrokeWidth = Debug_ForceStrokeMin + (v.PathMeta.y * Debug_StrokeScale);
				return o;
			}
	
			float DistanceToEllipse(float2 Position,float2 Center,float2 Radius)
			{
				float2 Delta = Position - Center;
				float Distance = length(Delta) - Radius;
				return Distance;
			}


			//	https://www.shadertoy.com/view/4sKyzW
			//#include "SignedDistanceCubicBezier.cginc"

			float TimeAlongLine2(float2 Position,float2 Start,float2 End)
			{
				float2 Direction = End - Start;
				float DirectionLength = length(Direction);
				float Projection = dot( Position - Start, Direction) / (DirectionLength*DirectionLength);
				
				return Projection;
			}

			float2 NearestToLine2(float2 Position,float2 Start,float2 End)
			{
				float Projection = TimeAlongLine2( Position, Start, End );
				
				//	past start
				Projection = max( 0, Projection );
				//	past end
				Projection = min( 1, Projection );
				
				//	is using lerp faster than
				//	Near = Start + (Direction * Projection);
				float2 Near = lerp( Start, End, Projection );
				return Near;
			}

			//	https://github.com/NewChromantics/PopCave/blob/be8766dd03eb46430bf4f8a906db86ed1973a360/PopCave/DrawLines.frag.glsl#L59
			float DistanceToLine2(float2 Position,float2 Start,float2 End)
			{
				float2 Near = NearestToLine2( Position, Start, End );
				return length( Near - Position );
			}

			float DistanceToQuadratic(float2 Position,float2 Start,float2 ControlPoint,float2 End)
			{
				float MaxDistance = 10;	//	gr: remove this
				float2 p0 = Position;
				float2 p1 = Start;
				float2 p2 = ControlPoint;
				float2 p3 = End;
				float bezier_curve_threshold = MaxDistance;
				
				//	gr: i believe this is the compacted version of lerp( a, lerp(b, lerp(c,d) ) )
				float a = p3.x - 2 * p2.x + p1.x;
				float b = 2 * (p2.x - p1.x);
				float c = p1.x - p0.x;
				float dx = b * b - 4.0f * a * c;
				
				//	derivitive on x
				//	gr: little difference here I think because of our point clamping
				if (dx < 0.0f)	return NULL_DISTANCE; 

				//	time values
				//	gr: this kinda feels like distance along line
				float t1 = (-b + sqrt(dx)) / (2 * a);
				float t2 = (-b - sqrt(dx)) / (2 * a);

				//	between 0...1 to be on the curve edge
				float TimeTolerance = 0;
				float TimeMin = -TimeTolerance;
				float TimeMax = 1+TimeTolerance;

				//	recalc the position clamped so then below the distance will cap and we get proper round distances
				//	may need to unclamp here to do different stroke ends?
				float t1clamped = clamp(t1,TimeMin,TimeMax);
				float t2clamped = clamp(t2,TimeMin,TimeMax);
				float2 xy1 = p1 + 2 * t1clamped * (p2 - p1) + t1clamped * t1clamped * (p3 - 2 * p2 + p1);
				float2 xy2 = p1 + 2 * t2clamped * (p2 - p1) + t2clamped * t2clamped * (p3 - 2 * p2 + p1);
				//	get y for each time
				float y1 = p1.y + 2 * t1 * (p2.y - p1.y) + t1 * t1 * (p3.y - 2 * p2.y + p1.y);
				float y2 = p1.y + 2 * t2 * (p2.y - p1.y) + t2 * t2 * (p3.y - 2 * p2.y + p1.y);


				bool ClampEnds = false;

				float Distance1 = NULL_DISTANCE;
				float Distance2 = NULL_DISTANCE;

				if ( (t1>=TimeMin && t1<=TimeMax) || !ClampEnds )
				{
					Distance1 = abs(p0.y - y1);
					Distance1 = distance(p0,xy1);
					Distance1 -= Debug_BezierDistanceOffset;
				}

				if ( (t2>=TimeMin && t2<=TimeMax) || !ClampEnds )
				{
					Distance2 = abs(p0.y-y2);
					Distance2 = distance(p0,xy2);
					Distance2 -= Debug_BezierDistanceOffset;
				}

				return min(Distance1,Distance2);
			}


			float DistanceToCubic(float2 Position,float2 Start,float2 ControlPointIn,float2 ControlPointOut,float2 End)
			{
				float abc = DistanceToQuadratic( Position, Start, ControlPointIn, ControlPointOut );
				//float bcd = DistanceToQuadratic( Position, ControlPointIn, ControlPointOut, End );
				//return bcd;
				//return min( abc, bcd );
				float abd = DistanceToQuadratic( Position, Start, ControlPointIn, End );
				return abc;
				return abd;
			}



			float DistanceToCubicBezierSegment(float2 Position,float2 Start,float2 ControlPointIn,float2 ControlPointOut,float2 End)
			{
				float Distance = NULL_DISTANCE;
				//	debug, draw control points
				if ( DEBUG_BEZIER_CONTROLPOINTS )
				{
					float2 Rad = float2(DEBUG_CONTROLPOINT_SIZE,DEBUG_CONTROLPOINT_SIZE);
					Distance = min( Distance, DistanceToEllipse( Position, Start, Rad ) );
					Distance = min( Distance, DistanceToEllipse( Position, ControlPointIn, Rad*0.5f ) );
					Distance = min( Distance, DistanceToEllipse( Position, ControlPointOut, Rad*0.5f ) );
					Distance = min( Distance, DistanceToEllipse( Position, End, Rad ) );
				}

				float BezierDistance = DistanceToCubic(Position,Start,ControlPointIn,ControlPointOut,End);
				//float BezierDistance = cubic_bezier_dis(Position,Start,ControlPointIn,ControlPointOut,End );
				//float BezierDistance = DistanceToLine2(Position,Start,End);

float EdgeDistance = NULL_DISTANCE;
				float ab = DistanceToLine2(Position,Start,ControlPointIn);
				float bc = DistanceToLine2(Position,ControlPointIn,ControlPointOut);
				float cd = DistanceToLine2(Position,ControlPointOut,End);


				EdgeDistance = min(EdgeDistance,ab);
				EdgeDistance = min(EdgeDistance,bc);
				EdgeDistance = min(EdgeDistance,cd);

				/*
				//	work out which side we're on...
				float2 lineDir = End - Start;
				float2 perpDir = float2(lineDir.y, -lineDir.x);
				float2 dirToPt1 = Start - Position;
				bool Right = dot(perpDir, dirToPt1) < 0.0f;
				if ( Right )
				{
					BezierDistance-= 0.01f;
				}
				else
				{
					BezierDistance+= 9.6f;
				}
					*/

				//float sgn = cubic_bezier_sign(uv,p0,p1,p2,p3);
				//Distance = min( Distance, BezierDistance );
				Distance = min( Distance, EdgeDistance );
				return Distance;
			}


			float DistanceToPath(float2 Position,float4x4 PathData,out int PathDataType)
			{
				PathDataType = PathData[PATH_DATAROW_META].x;
				if ( PathDataType == PATH_DATATYPE_ELLIPSE )
				{
					float2 EllipseCenter = PathData[PATH_DATAROW_POSITION].xy;
					float EllipseRadius = PathData[PATH_DATAROW_POSITION].z;
					return DistanceToEllipse( Position, EllipseCenter, EllipseRadius );
				}
				if ( PathDataType == PATH_DATATYPE_BEZIER )
				{
					float2 Start = PathData[PATH_DATAROW_POSITION].xy;
					float2 End = PathData[PATH_DATAROW_POSITION].zw;
					float2 ControlIn = PathData[PATH_DATAROW_POSITION+1].xy;
					float2 ControlOut = PathData[PATH_DATAROW_POSITION+1].zw;

					return DistanceToCubicBezierSegment( Position, Start, ControlIn, ControlOut, End );
				}

				return NULL_DISTANCE;
			}

			float DistanceToPath(float2 Position,int PathIndex,out int PathDataType)
			{
				float4x4 PathData = PathDatas[PathIndex];
				return DistanceToPath(Position,PathData,PathDataType);
			}

			fixed4 frag (v2f i) : SV_Target
			{
				int PathDataType = PATH_DATATYPE_UNINITIALISED;
				float Distance = DistanceToPath(i.LocalPosition,i.PathDataIndex,PathDataType);

				if ( PathDataType == PATH_DATATYPE_NULL )
				{
					Distance = -1;	//	fill the quad
					return NULL_PATH_COLOUR;
				}

				//	whilst we have layers, we should do alpha blending instead of clipping
				//	really we need to do that anyway
				float HalfStrokeWidth = i.StrokeWidth / 2.0f;
				//	todo: antialias/blend edges
				//	outside path
				if ( Distance > HalfStrokeWidth )
					return OUTSIDE_COLOUR;
				//	within stroke
				if ( Distance > -HalfStrokeWidth )
					return i.StrokeColour;

				//	within fill (typically negative number)
				//return i.FillColour;
				float DistanceT = (-Distance ) / 0.3f;
				float4 Red = float4(1,0,0,1);
				float4 Green = float4(0,0,1,1);
				return lerp( Red, Green, DistanceT );
			}
			ENDCG
		}
	}
}
