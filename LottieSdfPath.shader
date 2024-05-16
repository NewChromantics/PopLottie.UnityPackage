Shader "PopLottie/LottieSdfPath"
{
	Properties
	{
		Debug_StrokeScale ("Debug_StrokeScale", Range(1.0, 50.0)) = 1.0
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
				o.StrokeWidth = v.PathMeta.y * Debug_StrokeScale;
				return o;
			}
	
			float DistanceToEllipse(float2 Position,float2 Center,float2 Radius)
			{
				float2 Delta = Position - Center;
				float Distance = length(Delta) - Radius;
				return Distance;
			}

			//	https://www.shadertoy.com/view/4sKyzW
			float DistanceToCubicBezierSegment(float2 Position,float2 Start,float2 ControlPointIn,float2 ControlPointOut,float End)
			{
				float Distance = NULL_DISTANCE;
				//	debug, draw control points
				if ( DEBUG_BEZIER_CONTROLPOINTS )
				{
					float2 Rad = float2(DEBUG_CONTROLPOINT_SIZE,DEBUG_CONTROLPOINT_SIZE);
					Distance = min( Distance, DistanceToEllipse( Position, Start, Rad ) );
					Distance = min( Distance, DistanceToEllipse( Position, ControlPointIn, Rad*0.5f ) );
					Distance = min( Distance, DistanceToEllipse( Position, ControlPointOut, Rad*0.5f ) );
					//Distance = min( Distance, DistanceToEllipse( Position, End, Rad ) );
				}



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
					//return NULL_PATH_COLOUR;
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
				return i.FillColour;
			}
			ENDCG
		}
	}
}
