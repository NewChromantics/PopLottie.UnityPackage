Shader "PopLottie/LottieSdfPath"
{
	Properties
	{
		Debug_StrokeScale ("Debug_StrokeScale", Range(0.0, 20.0)) = 1.0
		Debug_ForceStrokeMin ("Debug_ForceStrokeMin", Range(0.0, 1.0)) = 0.0
		Debug_AddStrokeAlpha ("Debug_AddStrokeAlpha", Range(0.0, 1.0)) = 0.0
		Debug_DistanceRepeats("Debug_DistanceRepeats", Range(0.0, 20.0)) = 0.0
		Debug_BezierDistanceOffset ("Debug_BezierDistanceOffset", Range(0.0, 1.0)) = 1.0
		AntialiasRadius("AntialiasRadius", Range(0,0.01) ) = 0.01
		[IntRange]BezierArcSteps("BezierArcSteps", Range(2,50) ) = 10
		[IntRange]RenderOnlyPath("RenderOnlyPath", Range(-1,20) ) = -1
	}
	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" }
		LOD 100
		Cull off	//	double sided
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
				//float2 QuadUv : TEXCOORD0;	//	not sure we need this, all shape info is gonna be in local space
				float4 FillColour : COLOR;
				float4 StrokeColour : TEXCOORD0;
				float4 StrokeWidth : TEXCOORD1;
				float4 ShapeMeta : TEXCOORD2;
			};

			struct v2f
			{
				float4 ClipPosition : SV_POSITION;
				float2 LocalPosition : TEXCOORD0;

				float4 OutsideColour : TEXCOORD1;
				float4 StrokeOutsideColour : TEXCOORD2;
				float4 StrokeInsideColour : TEXCOORD3;
				float4 FillColour : TEXCOORD4;
				float StrokeWidth : TEXCOORD5;
				float4 ShapeMeta : TEXCOORD6;
			};

			#define ENABLE_DEBUG_INVISIBLE	false
			#define OUTSIDE_COLOUR			(ENABLE_DEBUG_INVISIBLE ? float4(0,1,0,0.1) : float4(0,0,0,0) )
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
#define ENABLE_ANTIALIAS	true
#define DISCARD_IF_TRANSPARENT	false
#define DEBUG_DISTANCE_CLIP_OUTSIDE	false
#define DEBUG_POLYGON	false

			float AntialiasRadius;
			int BezierArcSteps;
			int RenderOnlyPath;
#define MIN_BEZIER_ARC_STEPS	3
#define BEZIER_ARC_STEPS	max( BezierArcSteps, MIN_BEZIER_ARC_STEPS )

			#define PATH_TYPE_NULL		0
			#define PATH_TYPE_ELLIPSE	1
			#define PATH_TYPE_BEZIER	2


#define MAX_PATHMETAS		50
#define MAX_PATHPOINTS		500	//	for beziers these are in batches of 3... start control control
			uniform float4 PathMetas[MAX_PATHMETAS];
			uniform float4 PathPoints[MAX_PATHPOINTS];

#define MAX_POLYGON_POINTS	50
			
			struct ShapeMeta_t
			{
				int FirstPath;
				int PathCount;
			};

			struct PathMeta_t
			{
				int PathType;
				int FirstPoint;
				int PointCount;
			};

			struct Distance_t
			{
				float Distance;
				float Sign;
			};

			PathMeta_t GetPathMeta(int PathIndex)
			{
				PathMeta_t PathMeta;
				float4 PathData = PathMetas[PathIndex];
				PathMeta.PathType = PathData.x;
				PathMeta.FirstPoint = PathData.y;
				PathMeta.PointCount = PathData.z;
				return PathMeta;
			}

			ShapeMeta_t GetShapeMeta(float4 ShapeMeta)
			{
				ShapeMeta_t meta;
				meta.FirstPath = ShapeMeta.x;
				meta.PathCount = ShapeMeta.y;
				return meta;
			}


			v2f vert (appdata v)
			{
				v2f o;
				o.ClipPosition = UnityObjectToClipPos(v.LocalPosition);
				o.LocalPosition = v.LocalPosition;
				o.FillColour = v.FillColour;
				o.StrokeOutsideColour = v.StrokeColour;
				o.StrokeOutsideColour.w += Debug_AddStrokeAlpha;
				o.StrokeWidth = max( Debug_ForceStrokeMin, v.StrokeWidth.x );
				o.StrokeWidth *= Debug_StrokeScale;
				o.ShapeMeta = v.ShapeMeta;

				//	correct colours for antialias blending
				//	need to AA between
				//		outside & stroke
				//		stroke & fill
				//	to avoid haloing, invisible layers need to use next layer's rgb
				//	this logic will break a bit if we have neither stroke nor fill.
				bool HasStroke = o.StrokeWidth > 0 && o.StrokeOutsideColour.w > 0;
				bool HasFill = o.FillColour.w > 0;
				float4 RemoveAlpha = float4(1,1,1,0);
				float4 FillColour = HasFill ? o.FillColour : o.StrokeOutsideColour * RemoveAlpha;
				float4 StrokeColour = HasStroke ? o.StrokeOutsideColour : o.FillColour * RemoveAlpha;
				float4 OutsideColour = StrokeColour * RemoveAlpha;
				o.OutsideColour = OutsideColour;
				o.FillColour = FillColour;
				o.StrokeOutsideColour = StrokeColour;
				float AlphaBlend = o.StrokeOutsideColour.w;
				o.StrokeInsideColour = (o.StrokeOutsideColour * AlphaBlend) + (o.FillColour * (1-AlphaBlend) );
				
				if ( DEBUG_BEZIER_EDGES /*&& ENABLE_ANTIALIAS */)
				{
					o.StrokeOutsideColour = float4(1,0,1,1);
					o.StrokeInsideColour = float4(1,0,1,1);
					o.StrokeWidth = 0.01;
				}

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


			bool not(bool3 bools)
			{
				return bool3( !bools[0], !bools[1], !bools[2] );
			}

			float LengthSquared(float2 Delta)
			{
				return dot(Delta,Delta);
			}

			Distance_t DistanceToPolygonSegment(float2 Position,float2 Prev,float2 Next,float CurrentSign)
			{
				// distance
				float2 DirToPrev = Prev - Next;
				float2 DirToPoint = Position - Next;
				float2 e = DirToPrev;
				float2 w = DirToPoint;
				float DistanceToPrevSq = LengthSquared(DirToPrev);
				float ProjectionAlongLine = clamp( dot(w,e)/DistanceToPrevSq, 0.0, 1.0 );

				float2 b = w - e * ProjectionAlongLine;
				//float bdistsq = dot(b,b);
				float bdistsq = DistanceToLine2(Position,Prev,Next );	
				bdistsq *= bdistsq;

				float distsq = bdistsq;

				// winding number from http://geomalgorithms.com/a03-_inclusion.html
				bool PointLowerThanThis = Position.y>= Next.y;
				bool PointHigherThanPrev = Position.y < Prev.y;
				bool3 cond = bool3( PointLowerThanThis, 
									PointHigherThanPrev, 
									e.x*w.y > e.y*w.x 
				);

				Distance_t Result;
				Result.Sign = CurrentSign;
				Result.Distance = sqrt(distsq);

				//if( all(cond) || all(not(cond)) )
				if ( cond[0]==cond[1] && cond[1] == cond[2] )
					Result.Sign *= -1;
		
				return Result;
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
				/*	gr: this works, but is actually a little slower in practise!
					return (1 - t) * (1 - t) * (1 - t) * a
					+
					3 * (1 - t) * (1 - t) * t * b
					+
					3 * (1 - t) * t * t * c
					+
					t * t * t * d;
				*/
				//	brute force version for readability
				//	todo: use the common optimised version (already in c#! GetBezierValue()
				float2 ab = lerp( a, b, t );
				float2 bc = lerp( b, c, t );
				float2 cd = lerp( c, d, t );
				float2 ab_bc = lerp( ab, bc, t );
				float2 bc_cd = lerp( bc, cd, t );
				float2 abbc_bccd = lerp( ab_bc, bc_cd, t );
				return abbc_bccd;
			}


			//	brute force method to test GetBezierPoint() is correct and to test against
			Distance_t DistanceToCubic_Step(float2 Position,float2 a,float2 b,float2 c,float2 d)
			{
				//	visualise bezier steps to make sure math above is right
				float Distance = NULL_DISTANCE;
				int Steps = BEZIER_ARC_STEPS;
				float2 FirstPos = GetBezierPoint( a, b, c, d, 0.0 );
				float2 PrevPos = FirstPos;

				for ( int i=1;	i<Steps;	i++ )
				{
					float t = float(i) / float(Steps-1);
					float2 NextPos = GetBezierPoint( a, b, c, d, t );
					//float Distancet = distance( Position, Pointt );
					float2 PrevToNextDistance = DistanceToLine2( Position, PrevPos, NextPos );
					Distance = min( Distance, PrevToNextDistance );

					PrevPos = NextPos;
				}
	
				

				Distance_t DistanceAndWinding;
				DistanceAndWinding.Distance = Distance;
				DistanceAndWinding.Sign = 1;
				return DistanceAndWinding;
			}

			//	brute force method to test GetBezierPoint() is correct and to test against
			Distance_t DistanceToCubic_StepAsPolygon(float2 Position,float2 a,float2 b,float2 c,float2 d,float CurrentSign)
			{
				//	visualise bezier steps to make sure math above is right
				float Distance = NULL_DISTANCE;
				float2 FirstPos = GetBezierPoint( a, b, c, d, 0.0 );
				float2 PrevPos = FirstPos;

				for ( int i=1;	i<BEZIER_ARC_STEPS;	i++ )
				{
					float t = float(i) / float(BEZIER_ARC_STEPS-1);
					float2 NextPos = GetBezierPoint( a, b, c, d, t );
					//float Distancet = distance( Position, Pointt );
					
					//float2 PrevToNextDistance = DistanceToLine2( Position, PrevPos, NextPos );
					//Distance = min( Distance, PrevToNextDistance );

					//if ( i < 3 )
					//WindingAngle += DegreesBetweenPoints( PrevPos, NextPos, Position );

					Distance_t DistAndFlip = DistanceToPolygonSegment( Position, PrevPos, NextPos, CurrentSign );

					Distance = min( Distance, DistAndFlip.Distance );
					CurrentSign = DistAndFlip.Sign;
					
					PrevPos = NextPos;
				}
	
				//	gr: also need to include path closure for winding
/* this closes just this arc! dont do it!
				{
					//WindingAngle += DegreesBetweenPoints( PrevPos, FirstPos, Position );
					DistanceAndWinding_t DistAndFlip = DistanceToPolygonSegment( Position, PrevPos, FirstPos );

					Distance = min( Distance, DistAndFlip.Distance );
					if ( DistAndFlip.WindingAngle > 0 )
						Sign = -Sign;

				}
*/
				Distance_t DistanceAndWinding;
				DistanceAndWinding.Distance = Distance;
				DistanceAndWinding.Sign = CurrentSign;
				return DistanceAndWinding;
			}

			Distance_t DistanceToCubic(float2 Position,float2 Start,float2 ControlPointIn,float2 ControlPointOut,float2 End,float CurrentSign)
			{
				return DistanceToCubic_StepAsPolygon( Position, Start, ControlPointIn, ControlPointOut, End, CurrentSign)
				//return DistanceToCubic_Step( Position, Start, ControlPointIn, ControlPointOut, End)
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

			

			Distance_t DistanceToCubicBezierSegment(float2 Position,float2 Start,float2 ControlPointIn,float2 ControlPointOut,float2 End,float CurrentSign)
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

				Distance_t BezierDistanceAndWinding = DistanceToCubic(Position,Start,ControlPointIn,ControlPointOut,End, CurrentSign );
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
				{
					Distance = min( Distance, EdgeDistance );
					Distance_t Result;
					Result.Distance = EdgeDistance;
					Result.Sign = 1;
					return Result;
				}

				Distance = min( Distance, ControlPointDistance );

				Distance_t DistanceAndWinding;
				DistanceAndWinding.Distance = Distance;
				DistanceAndWinding.Sign = BezierDistanceAndWinding.Sign;
				return DistanceAndWinding;
			}

			float DistanceToPath(float2 Position,PathMeta_t PathMeta)
			{
				if ( PathMeta.PathType == PATH_TYPE_ELLIPSE )
				{
					float2 EllipseCenter = PathPoints[PathMeta.FirstPoint+0].xy;
					float EllipseRadius = PathPoints[PathMeta.FirstPoint+1].x;
					return DistanceToEllipse( Position, EllipseCenter, EllipseRadius );
				}


				//	multiple segments need to accumulate winding angle to determine if we're inside
				//	gr: currently assuming all data is the same type... need to redo this data to handle mixed types with same styles
				if ( PathMeta.PathType == PATH_TYPE_BEZIER )
				{
					float MinDistance = NULL_DISTANCE;
					float CurrentSign = 1;

					//	data is first point, then batches of controlin+controlout+end
					float2 End = PathPoints[PathMeta.FirstPoint+0];
					for ( int i=1;	i<PathMeta.PointCount;	i+=3 )
					{
						int p = PathMeta.FirstPoint+i;
						float2 Start = End;
						float2 ControlIn = PathPoints[p+0].xy;
						float2 ControlOut = PathPoints[p+1].xy;
						End = PathPoints[p+2].xy;
						Distance_t SegmentResult = DistanceToCubicBezierSegment( Position, Start, ControlIn, ControlOut, End, CurrentSign );
						MinDistance = min( MinDistance, SegmentResult.Distance );
						CurrentSign = SegmentResult.Sign;
					}

					//	fill if sign says we're inside the path
					MinDistance *= CurrentSign;
					return MinDistance;
				}

				return NULL_DISTANCE;
			}

			float DistanceToShape(float2 Position,ShapeMeta_t ShapeMeta)
			{
				//	need to count how many shapes we're in to do odd/even holes
				//	unity says uint is faster for modulous - we could keep flipping a bool and avoid it entirely
				uint OverlapCount = 0;
				bool IsHole = false;

				float Distance = NULL_DISTANCE;
				for ( int p=0;	p<ShapeMeta.PathCount;	p++ )
				{
					int PathIndex = (RenderOnlyPath==-1) ? ShapeMeta.FirstPath+p : RenderOnlyPath;

					PathMeta_t PathMeta = GetPathMeta(PathIndex);
					float PathDistance = DistanceToPath(Position,PathMeta);
					
					//	gr: this comparison with 0 may need to include stroke radius
					if ( PathDistance <= 0 )
					{
						OverlapCount++;
						IsHole = !IsHole;
					}
					Distance = min( Distance, PathDistance );
				}

				//if ( (OverlapCount % 2) == 0 )
				if ( !IsHole )
				{
					//	inside becomes hole
					if ( Distance < 0 )
					{
						//	gr: this breaks antialiasing as the signed distance now jumps
						//		does this need to be something like (distance * -1) + Stroke?
						Distance *= -1;
					}
				}

				return Distance;
			}


			//	unit test polygon test
			//	https://www.shadertoy.com/view/wdBXRW
			float sdPolygon(float2 v[MAX_POLYGON_POINTS],float2 p,int PointCount)
			{
				//const int num = v.length();
				//float distsq = LengthSquared(p-v[0]);
				float dist = distance(p, v[0]);
				float Sign = 1.0;

				for( int i=0; i<PointCount;	i++ )
				{
					int prev = ((i-1)+PointCount) % PointCount;
			
					Distance_t DistAndFlip = DistanceToPolygonSegment( p, v[prev], v[i], Sign );
					dist = min( dist, DistAndFlip.Distance );
					Sign = DistAndFlip.Sign;
				}

				return dist * Sign;
			}


			float GetSdfPathDistance(v2f Input)
			{
				if ( DEBUG_POLYGON )
				{
					float2 Points[MAX_POLYGON_POINTS];
					Points[0] = float2(0.1,0.1);
					Points[1] = float2(0.7,0.2);
					Points[2] = float2(0.9,0.5);
					Points[3] = float2(0.4,0.7);
					Points[4] = float2(0.1,0.9);
					return sdPolygon( Points, Input.LocalPosition, 5 );
				}
	
				ShapeMeta_t ShapeMeta = GetShapeMeta(Input.ShapeMeta);
				float Distance = DistanceToShape( Input.LocalPosition, ShapeMeta );

				return Distance;
			}

			float4 Antialias(float4 Outside,float4 Inside,float EdgeDistance,float Distance)
			{
				//	this radius/threshold needs to be related to final screen output
				//	whereas distance is in quad-space
				float Time = smoothstep( EdgeDistance-AntialiasRadius, EdgeDistance+AntialiasRadius, Distance );
				return lerp( Inside, Outside, Time );

				if ( Distance > EdgeDistance )
					return Outside;
				else 
					return Inside;
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

				//	no AA, hard simple distance -> colour
				if ( !ENABLE_ANTIALIAS )
				{
					if ( Distance > HalfStrokeWidth )
					{
						return OUTSIDE_COLOUR;
					}

					//	outer stroke (stroke overlapping outside)
					if ( Distance >=0 )
						return Input.StrokeOutsideColour;

					//	inner stroke (stroke overlapping fill)
					if ( Distance > -HalfStrokeWidth )
						return Input.StrokeInsideColour;

					//	within fill (typically negative number)
					return Input.FillColour;
				}
				
				float4 Colour = Input.OutsideColour;

				//	gr: this also needs to blend, a semitransparent stroke should overlap the fill
				//		so this should really blend fill to edge, then put stroke on top... antialiased

				//	outside -> outer stroke
				Colour = Antialias( Colour, Input.StrokeOutsideColour, HalfStrokeWidth, Distance );
				Colour = Antialias( Colour, Input.StrokeInsideColour, 0, Distance );
				//	inner stroke -> fill
				Colour = Antialias( Colour, Input.FillColour, -HalfStrokeWidth, Distance );
				
				return Colour;
			}

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

				float4 Colour = GetSdfPathColour(Input);
				if ( DISCARD_IF_TRANSPARENT && Colour.w <= 0 )
					discard;
				return Colour;
			}
			ENDCG
		}
	}
}
