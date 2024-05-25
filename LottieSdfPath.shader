Shader "PopLottie/LottieSdfPath"
{
	Properties
	{
		Debug_StrokeScale ("Debug_StrokeScale", Range(1.0, 20.0)) = 1.0
		Debug_ForceStrokeMin ("Debug_ForceStrokeMin", Range(0.0, 1.0)) = 0.0
		Debug_AddStrokeAlpha ("Debug_AddStrokeAlpha", Range(0.0, 1.0)) = 0.0
		Debug_DistanceRepeats("Debug_DistanceRepeats", Range(0.0, 20.0)) = 0.0
		Debug_BezierDistanceOffset ("Debug_BezierDistanceOffset", Range(0.0, 1.0)) = 1.0
		
		WindingMax("WindingMax", Range(0.0, 900)) = 360.0
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
// Upgrade NOTE: excluded shader from DX11, OpenGL ES 2.0 because it uses unsized arrays
#pragma exclude_renderers d3d11 gles
// Upgrade NOTE: excluded shader from DX11 because it uses wrong array syntax (type[size] name)
#pragma exclude_renderers d3d11
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
				int PathDataCount : TEXCOORD5;
			};

			#define ENABLE_DEBUG_INVISIBLE	false
			#define OUTSIDE_COLOUR			(ENABLE_DEBUG_INVISIBLE ? float4(0,1,1,0.1) : float4(0,0,0,0) )
			#define NULL_PATH_COLOUR		(ENABLE_DEBUG_INVISIBLE ? float4(1,0,0,0.1) : float4(0,0,0,0) )
#define NULL_DISTANCE	999
#define DEBUG_CONTROLPOINT_SIZE	0.06
#define DEBUG_BEZIER_CONTROLPOINTS false
#define DEBUG_BEZIER_EDGES false

			float Debug_StrokeScale;
			float Debug_ForceStrokeMin;
			float Debug_AddStrokeAlpha;
			float Debug_BezierDistanceOffset;
float Debug_DistanceRepeats;
#define DEBUG_DISTANCE	(Debug_DistanceRepeats >= 1.0)

			//	todo: how to represent bezier paths...
			#define PATH_DATA_COUNT			300
			#define PATH_DATATYPE_UNINITIALISED	-1
			#define PATH_DATATYPE_NULL		0
			#define PATH_DATATYPE_ELLIPSE	1
			#define PATH_DATATYPE_BEZIER	2
			#define PATH_DATAROW_META		0
			#define PATH_DATAROW_POSITION	1


			float WindingMax;
			uniform float4x4 PathDatas[PATH_DATA_COUNT];

			struct DistanceAndWinding_t
			{
				float Distance;
				float WindingAngle;
			};

			v2f vert (appdata v)
			{
				v2f o;
				o.ClipPosition = UnityObjectToClipPos(v.LocalPosition);
				o.LocalPosition = v.LocalPosition;
				o.FillColour = v.FillColour;
				o.StrokeColour = v.StrokeColour;
				o.StrokeColour.w += Debug_AddStrokeAlpha;
				o.StrokeWidth = v.PathMeta.x;
				o.PathDataIndex = v.PathMeta.y;
				o.PathDataCount = v.PathMeta.z;
				o.StrokeWidth = Debug_ForceStrokeMin + (o.StrokeWidth * Debug_StrokeScale);
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
				
				//	gr: i believe this is the compacted version of lerp( a, lerp(b, c) ) )
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

			float2 GetBezierPoint(float2 a,float2 b,float2 c,float2 d,float t)
			{
				float2 ab = lerp( a, b, t );
				float2 bc = lerp( b, c, t );
				float2 cd = lerp( c, d, t );
				float2 ab_bc = lerp( ab, bc, t );
				float2 bc_cd = lerp( bc, cd, t );
				float2 abbc_bccd = lerp( ab_bc, bc_cd, t );
				return abbc_bccd;
			}

float angle(float2 p) {
    return atan2(p.y, p.x);
}
	float AngleDegreesOfVector(float2 v)
	{
		v = normalize(v);
		//	-PI...PI
		float Rad = atan2( v.y, v.x );
		//	to 0..2pi (circle)
		//if ( Rad < 0 )
		//	Rad += 2 * UNITY_PI;
		float Norm = Rad / (UNITY_PI*2.0f);
		float Deg = Norm * 360.f;
		return Deg;
	}


#define PI UNITY_PI

float angle(float2 p0, float2 p1) 
{
    float a =  angle(p1) - angle(p0);
    a = (a-PI) % (2.*PI);
	a-=PI;
    
    return a;
}
	#define EPSILON	0.001f
			//	what is the arc from a to b in angles
			float DegreesBetweenPoints(float2 a,float2 b,float2 Pivot)
			{
				//	if we're right on a point (ie, an edge) we want to avoid NaNs
				//	but I dont think we can calculate the angle...?
				if ( distance(a,Pivot) < EPSILON )	return 0;
				if ( distance(b,Pivot) < EPSILON )	return 0;

				float angletoa = AngleDegreesOfVector( a - Pivot );
				float angletob = AngleDegreesOfVector( b - Pivot );
				return angletob - angletoa;
			}

			//	brute force method to test GetBezierPoint() is correct and to test against
			DistanceAndWinding_t DistanceToCubic_Step(float2 Position,float2 a,float2 b,float2 c,float2 d/*,inout float WindingCount*/)
			{
				//	visualise bezier steps to make sure math above is right
				float Distance = NULL_DISTANCE;
//	gr: more steps 
				int Steps = 10;
				float2 FirstPos = GetBezierPoint( a, b, c, d, 0.0 );
				float2 PrevPos = FirstPos;

				float WindingAngle = 0;

				for ( int i=1;	i<Steps;	i++ )
				{
					float t = float(i) / float(Steps-1);
					float2 NextPos = GetBezierPoint( a, b, c, d, t );
					//float Distancet = distance( Position, Pointt );
					float2 PrevToNextDistance = DistanceToLine2( Position, PrevPos, NextPos );
					Distance = min( Distance, PrevToNextDistance );

					//if ( i < 3 )
					WindingAngle += DegreesBetweenPoints( PrevPos, NextPos, Position );
					
					PrevPos = NextPos;
				}
	
				//	gr: also need to include path closure for winding
				{
					//WindingAngle += DegreesBetweenPoints( PrevPos, FirstPos, Position );
				}

				DistanceAndWinding_t DistanceAndWinding;
				DistanceAndWinding.Distance = Distance;
				DistanceAndWinding.WindingAngle = WindingAngle;
				return DistanceAndWinding;
			}


			float DistanceToCubicMix(float2 Position,float2 Start,float2 ControlPointIn,float2 ControlPointOut,float2 End)
			{
				float abc = DistanceToQuadratic( Position, Start, ControlPointIn, ControlPointOut );
				//float bcd = DistanceToQuadratic( Position, ControlPointIn, ControlPointOut, End );
				//return bcd;
				//return min( abc, bcd );
				float abd = DistanceToQuadratic( Position, Start, ControlPointIn, End );
				return abc;
				return abd;
			}

			DistanceAndWinding_t DistanceToCubic(float2 Position,float2 Start,float2 ControlPointIn,float2 ControlPointOut,float2 End)
			{
				return DistanceToCubic_Step( Position, Start, ControlPointIn, ControlPointOut, End)
				//		- Debug_BezierDistanceOffset
				;
			}

			//	https://www.shadertoy.com/view/4sKyzW
			float GetCubicBezierSign(float2 Position,float2 Start,float2 ControlPointIn,float2 ControlPointOut,float2 End)
			{
				float2 p0 = Start;
				float2 p1 = ControlPointIn;
				float2 p2 = ControlPointOut;
				float2 p3 = End;
				float2 uv = Position;

				float2 tang1 = p0.xy - p1.xy;
				float2 tang2 = p2.xy - p3.xy;

				float2 nor1 = float2(tang1.y,-tang1.x);
				float2 nor2 = float2(tang2.y,-tang2.x);
				int n_ints = 0;	//	intersections

				if(p0.y < p1.y){
					if((uv.y<=p0.y) && (dot(uv-p0.xy,nor1)<0.)){
						n_ints++;
					}
				}
				else{
					if(!(uv.y<=p0.y) && !(dot(uv-p0.xy,nor1)<0.)){
						n_ints++;
					}
				}

				if(p2.y<p3.y){
					if(!(uv.y<=p3.y) && dot(uv-p3.xy,nor2)<0.){
						n_ints++;
					}
				}
				else{
					if((uv.y<=p3.y) && !(dot(uv-p3.xy,nor2)<0.)){
						n_ints++;
					}
				}

				if(n_ints==0 || n_ints==2 || n_ints==4){
					return 1.;
				}
				else{
					return -1.;
				}
			}

			

			DistanceAndWinding_t DistanceToCubicBezierSegment(float2 Position,float2 Start,float2 ControlPointIn,float2 ControlPointOut,float2 End)
			{
				float Distance = NULL_DISTANCE;
				float ControlPointDistance = NULL_DISTANCE;
				
				//	debug, draw control points
				if ( DEBUG_BEZIER_CONTROLPOINTS )
				{
					float2 Rad = float2(DEBUG_CONTROLPOINT_SIZE,DEBUG_CONTROLPOINT_SIZE);
					ControlPointDistance = min( ControlPointDistance, DistanceToEllipse( Position, Start, Rad ) );
					ControlPointDistance = min( ControlPointDistance, DistanceToEllipse( Position, ControlPointIn, Rad*0.5f ) );
					ControlPointDistance = min( ControlPointDistance, DistanceToEllipse( Position, ControlPointOut, Rad*0.5f ) );
					ControlPointDistance = min( ControlPointDistance, DistanceToEllipse( Position, End, Rad ) );
				}

				DistanceAndWinding_t BezierDistanceAndWinding = DistanceToCubic(Position,Start,ControlPointIn,ControlPointOut,End);
				float BezierDistance = BezierDistanceAndWinding.Distance;
				//float BezierDistance = cubic_bezier_dis(Position,Start,ControlPointIn,ControlPointOut,End );
				//float BezierDistance = DistanceToLine2(Position,Start,End);

				float EdgeDistance = NULL_DISTANCE;
				float ab = DistanceToLine2(Position,Start,ControlPointIn);
				float bc = DistanceToLine2(Position,ControlPointIn,ControlPointOut);
				float cd = DistanceToLine2(Position,ControlPointOut,End);


				EdgeDistance = min(EdgeDistance,ab);
				EdgeDistance = min(EdgeDistance,bc);
				EdgeDistance = min(EdgeDistance,cd);


				//float Sign = GetCubicBezierSign(Position,Start,ControlPointIn,ControlPointOut,End);
/*
				//	work out which side we're on...
				float2 lineDir = End - Start;
				float2 perpDir = normalize( float2(lineDir.y, -lineDir.x) );
				float2 dirToPt1 = normalize( Start - Position);
				bool Right = dot(perpDir, dirToPt1) < 0.0f;
				if ( Right )
				{
					BezierDistance -= Debug_BezierDistanceOffset;
				}
				else
				{
					//BezierDistance += Debug_BezierDistanceOffset;
				}
					*/
				//BezierDistance *= Sign;

				//float sgn = cubic_bezier_sign(uv,p0,p1,p2,p3);
				Distance = min( Distance, BezierDistance );
				if ( DEBUG_BEZIER_EDGES )
					Distance = min( Distance, EdgeDistance );
				Distance = min( Distance, ControlPointDistance );

				DistanceAndWinding_t DistanceAndWinding;
				DistanceAndWinding.Distance = Distance;
				DistanceAndWinding.WindingAngle = BezierDistanceAndWinding.WindingAngle;
				return DistanceAndWinding;
			}


			float DistanceToPath(float2 Position,int PathDataIndex,int PathDataCount,out int PathDataType)
			{
				float4x4 PathData0 = PathDatas[PathDataIndex];
				PathDataType = PathData0[PATH_DATAROW_META].x;

				if ( PathDataType == PATH_DATATYPE_ELLIPSE )
				{
					float2 EllipseCenter = PathData0[PATH_DATAROW_POSITION].xy;
					float EllipseRadius = PathData0[PATH_DATAROW_POSITION].z;
					return DistanceToEllipse( Position, EllipseCenter, EllipseRadius );
				}

				//	multiple segments need to accumulate winding angle to determine if we're inside
				//	gr: currently assuming all data is the same type... need to redo this data to handle mixed types with same styles
				if ( PathDataType == PATH_DATATYPE_BEZIER )
				{
					//	build up WindingAngle to determine if we've been ecompassed by a path (and therefore inside)
					float WindingAngle = 0;
					float MinDistance = NULL_DISTANCE;
//PathDataCount = min(PathDataCount,2);
					for ( int i=0;	i<PathDataCount;	i++ )
					{
						float4x4 PathData = PathDatas[PathDataIndex+i];
						float2 Start = PathData[PATH_DATAROW_POSITION].xy;
						float2 End = PathData[PATH_DATAROW_POSITION].zw;
						float2 ControlIn = PathData[PATH_DATAROW_POSITION+1].xy;
						float2 ControlOut = PathData[PATH_DATAROW_POSITION+1].zw;
						DistanceAndWinding_t SegmentResult = DistanceToCubicBezierSegment( Position, Start, ControlIn, ControlOut, End );
						MinDistance = min( MinDistance, SegmentResult.Distance );
						WindingAngle += SegmentResult.WindingAngle;
					}

					//WindingAngle = abs(WindingAngle) % 360;
					bool Inside = abs(WindingAngle) > WindingMax;
					//bool Inside = WindingAngle < -WindingMax;

					return Inside ? -MinDistance : MinDistance;
				}

				return NULL_DISTANCE;
			}

			bool not(bool3 bools)
			{
				return bool3( !bools[0], !bools[1], !bools[2] );
			}

			float LengthSquared(float2 Delta)
			{
				return dot(Delta,Delta);
			}

			#define MAX_POINTS	50
			//	https://www.shadertoy.com/view/wdBXRW
			float sdPolygon(float2 v[MAX_POINTS],float2 p,int PointCount)
			{
				//const int num = v.length();
				float distsq = LengthSquared(p-v[0]);
				float s = 1.0;
				//for( int i=0, j=num-1; i<num; j=i, i++ )
				for( int i=0; i<PointCount;	i++ )
				{
					int prev = ((i-1)+PointCount) % PointCount;
					// distance
					float2 DirToPrev = v[prev] - v[i];
					float2 DirToPoint = p - v[i];
					float2 e = DirToPrev;
					float2 w = DirToPoint;
					float DistanceToPrevSq = LengthSquared(DirToPrev);
					float ProjectionAlongLine = clamp( dot(w,e)/DistanceToPrevSq, 0.0, 1.0 );

					float2 b = w - e * ProjectionAlongLine;
					//float bdistsq = dot(b,b);
					float bdistsq = DistanceToLine2(p,v[prev],v[i] );	
					bdistsq *= bdistsq;

					distsq = min( distsq, bdistsq );

					// winding number from http://geomalgorithms.com/a03-_inclusion.html
					bool PointLowerThanThis = p.y>=v[i].y;
					bool PointHigherThanPrev = p.y < v[prev].y;
					bool3 cond = bool3( PointLowerThanThis, 
										PointHigherThanPrev, 
										e.x*w.y > e.y*w.x 
					);

					//if( all(cond) || all(not(cond)) )
					if ( cond[0]==cond[1] && cond[1] == cond[2] )
						s = -s;  
				}

				return s*sqrt(distsq);
			}


			float GetSdfPathDistance(v2f Input)
			{
				#define DEBUG_POLYGON	true
				if ( DEBUG_POLYGON )
				{
					float2 Points[MAX_POINTS];
					Points[0] = float2(0.1,0.1);
					Points[1] = float2(0.7,0.2);
					Points[2] = float2(0.9,0.5);
					Points[3] = float2(0.4,0.7);
					Points[4] = float2(0.1,0.9);
					return sdPolygon( Points, Input.LocalPosition, 5 );
				}
	
			

				int PathDataType = PATH_DATATYPE_UNINITIALISED;
				float Distance = DistanceToPath( Input.LocalPosition, Input.PathDataIndex, Input.PathDataCount, PathDataType);

				if ( PathDataType == PATH_DATATYPE_NULL )
				{
					return NULL_DISTANCE;
				}

				return Distance;
			}

			//	this function is the true result - no debug
			float4 GetSdfPathColour(v2f Input)
			{
				float Distance = GetSdfPathDistance(Input);
				if ( Distance >= NULL_DISTANCE )
				{
					//discard;
					return NULL_PATH_COLOUR;
				}

				//	whilst we have layers, we should do alpha blending instead of clipping
				//	really we need to do that anyway
				float HalfStrokeWidth = Input.StrokeWidth / 2.0f;

				//	todo: antialias/blend edges
				//	outside path
				if ( Distance > HalfStrokeWidth )
				{
					return OUTSIDE_COLOUR;
				}	

				//	within stroke
				if ( Distance > -HalfStrokeWidth )
					return Input.StrokeColour;

				//	within fill (typically negative number)
				return Input.FillColour;
			}

#define DEBUG_DISTANCE_CLIP_OUTSIDE	false
			fixed4 frag (v2f Input) : SV_Target
			{
				//	draw distance
				if ( DEBUG_DISTANCE )
				{
					float Distance = GetSdfPathDistance(Input);
					float RepeatStep = 1.0f / Debug_DistanceRepeats;
					float DistanceNorm = (abs(Distance) % RepeatStep ) / RepeatStep;
					float DistanceAlpha = 0.2;
					float4 Near = float4(1,1,1,DistanceAlpha);
					float4 Far = float4(0,0,0,DistanceAlpha);
					//	inside
					if ( Distance < 0 )
					{
						Near.xyz = float3(3,1,1);
						Far.xyz = float3(0,0,0);
					}
					else if ( DEBUG_DISTANCE_CLIP_OUTSIDE )
					{
						discard;
					}
					return lerp( Near, Far, DistanceNorm );
				}

				return GetSdfPathColour(Input);
			}
			ENDCG
		}
	}
}
