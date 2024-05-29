using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
using Codice.Client.BaseCommands.BranchExplorer;
using PopLottie;

namespace PopLottie
{
	
}



//	todo: instead of drawing immediately in update
//		generate a mesh + uniforms and update a meshfilter & mesh renderer 
[ExecuteInEditMode]
public class LottieSdf : MonoBehaviour
{
	//	shader constants
	const int MAX_PATHMETAS = 50;
	const int MAX_PATHPOINTS = 500;
	const int PATH_TYPE_NULL = 0;
	const int PATH_TYPE_ELLIPSE = 1;
	const int PATH_TYPE_BEZIER = 2;
	const int PATH_DATAROW_META = 0;
	const int PATH_DATAROW_POSITION = 1;

	public LottieAsset			animationAsset;
	(PopLottie.Animation,int)?	animationCacheAndHash;
	new PopLottie.Animation		animation => GetCachedAnimation();	//	new to stop warning as MonoBehaviour already has a .animation member
	
	public Shader			shader;
	//Material				material => new Material(shader);
	public Material			material;
	DateTime?				PlaybackStartTimeUTC = null;
	TimeSpan				PlaybackTime => PlaybackStartTimeUTC?.Subtract(DateTime.UtcNow) * -1 ?? TimeSpan.Zero;
	TimeSpan				RenderAnimationTime => Application.isPlaying ? PlaybackTime : TimeSpan.FromSeconds(EditorPreviewTimeSecs);
	
	public bool				RenderDebug = false;
	[Range(0.01f,10)]
	public float			WorldSize = 1;
	[Range(0.001f,0.1f)]
	public float			ZSpacing = 0.001f;
	public bool				RenderFirstToLast = true;
	
	[Range(0,20)]
	public float			EditorPreviewTimeSecs = 0;
	
	void OnEnable()
	{
		PlaybackStartTimeUTC = DateTime.UtcNow;
		animationCacheAndHash = null;
	}

	PopLottie.Animation GetCachedAnimation()
	{
		if ( animationAsset == null )
		{
			animationCacheAndHash = null;
			return null;
		}

		//	does cache need invalidaton
		if ( animationCacheAndHash?.Item2 is int CachedHash )
		{
			//	asset has changed, invalidate cache
			if ( CachedHash != animationAsset.GetHashCode() )
			{
				animationCacheAndHash = null;
			}
		}
		
		if ( animationCacheAndHash == null )
		{
			var anim = animationAsset.Animation;
			
			animationCacheAndHash = (anim,animationAsset.GetHashCode());
		}
		return animationCacheAndHash?.Item1;
	}
	


	void Update()
	{
		var anim = animation;
		if ( anim == null )
			return;
			
		//	whilst the world size is a bit of a hint, the tesselator
		//	uses this size for quality, we should kinda work out a quality
		//	based on screen distance?
		//	we could also allow the user to specify a billboardy world box to fill
		var Width = WorldSize;
		var Height = WorldSize;
		var RenderRect = new Rect(0,0,Width,Height);
		var RenderTime = RenderAnimationTime;
		//Debug.Log($"Playing frame {RenderTime}");
		var Frame = anim.Render(RenderTime,RenderRect,ScaleMode.ScaleToFit);
	
		//	gr: don't really wanna do this on cpu! and instead see if we can
		//		dump the values into buffers for a shader
		var meshAndPathDatas = GenerateLayerMesh(Frame,Flip:true,this.ZSpacing, RenderDebug );
		var Mesh = meshAndPathDatas.Mesh;
		var VectorToLocalTransform = Matrix4x4.identity;
		
		//	alignment here
		if ( true )//Center )
		{
			var x = -Frame.CanvasRect.width / 2;
			var y = -Frame.CanvasRect.height / 2;
			VectorToLocalTransform = Matrix4x4.Translate( new Vector3(x,y) );
		}
	
		var RenderParams = new RenderParams(material);
		meshAndPathDatas.ApplyUniforms(material);
		var LocalToWorld = this.transform.localToWorldMatrix;
		var RenderObjectToWorld = LocalToWorld * VectorToLocalTransform;
		
		Graphics.RenderMesh(RenderParams,Mesh,0,RenderObjectToWorld);
		
		//Debug.Log($"Triangle count; {Vector.indices.Length/3} - size{Vector.size}");
	}
	
	public struct MeshAndUniforms
	{
		public Mesh				Mesh;
		public List<Vector4>	PathMetas;
		public List<Vector4>	PathPoints;
		
		public void				ApplyUniforms(Material material)
		{
			//	gr: pad out this array to stop unity baking the max size
			PathMetas.AddRange( new Vector4[MAX_PATHMETAS-PathMetas.Count] );
			PathPoints.AddRange( new Vector4[MAX_PATHPOINTS-PathPoints.Count] );
			material.SetVectorArray("PathMetas",PathMetas);
			material.SetVectorArray("PathPoints",PathPoints);
		}
	}
	
	//	make a mesh with quads for every shape/layer
	//	todo: also need uniforms for instructions, colours etc for the shapes
	static public MeshAndUniforms GenerateLayerMesh(PopLottie.RenderCommands.AnimationFrame Frame,bool Flip,float ZSpacing,bool IncludeDebug)
	{
		var Indexes = new List<int>();

		//	gr: each quad is a shape. With a single style, and multiple paths (for holes)
		
		//	todo: turn all this into instancing data!
		//		first version just simpler for shader dev
		var LocalPositions = new List<Vector3>();
		var QuadUvs = new List<Vector2>();			//	texcoord0 0,0...1,1
		var FillColours = new List<Color>();
		var StrokeColours = new List<Vector4>();	//	texcoord1
		var StrokeWidths = new List<Vector4>();		//	texcoord2
		var ShapeMetas = new List<Vector4>();		//	texcoord3

		//	uniform data
		var PathMetas = new List<Vector4>();
		var PathPoints = new List<Vector4>();
		var FlipMult = Flip ? -1 : 1;
		
		Vector4 GetPathMeta(int PathType,int FirstPoint,int PointCount)
		{
			return new Vector4(PathType,FirstPoint,PointCount,0);
		}
		
		Vector4 GetShapeMeta(int FirstPath,int PathCount)
		{
			return new Vector4(FirstPath,PathCount,0,0);
		}

		void AddQuad(Rect? bounds,Vector4 ShapeMeta,int z,Color? FillColourMaybe,Color? StrokeColourMaybe,float StrokeWidth)
		{
			if ( bounds == null )
				return;
			var rect = new Rect( -1000,-1000,5000,5000 );//bounds.Value;
			var ClearColourA = new Color(0,1,1,0);
			var ClearColourB = new Color(0,0,0,0);
			var FillColour = FillColourMaybe ?? ClearColourA;
			var StrokeColour = StrokeColourMaybe ?? ClearColourB;
			
			var VertexIndex = LocalPositions.Count;
			float zf = z * ZSpacing;
			var tl = new Vector3( rect.xMin, rect.yMin*FlipMult, zf );
			var tr = new Vector3( rect.xMax, rect.yMin*FlipMult, zf );
			var br = new Vector3( rect.xMax, rect.yMax*FlipMult, zf );
			var bl = new Vector3( rect.xMin, rect.yMax*FlipMult, zf );
			LocalPositions.Add(tl);
			LocalPositions.Add(tr);
			LocalPositions.Add(br);
			LocalPositions.Add(bl);
			QuadUvs.Add( new Vector2(0,0) );
			QuadUvs.Add( new Vector2(1,0) );
			QuadUvs.Add( new Vector2(1,1) );
			QuadUvs.Add( new Vector2(0,1) );
			FillColours.Add(FillColour);
			FillColours.Add(FillColour);
			FillColours.Add(FillColour);
			FillColours.Add(FillColour);
			StrokeColours.Add(StrokeColour);
			StrokeColours.Add(StrokeColour);
			StrokeColours.Add(StrokeColour);
			StrokeColours.Add(StrokeColour);
			StrokeWidths.Add( new Vector4(StrokeWidth,StrokeWidth,StrokeWidth,StrokeWidth) );
			StrokeWidths.Add( new Vector4(StrokeWidth,StrokeWidth,StrokeWidth,StrokeWidth) );
			StrokeWidths.Add( new Vector4(StrokeWidth,StrokeWidth,StrokeWidth,StrokeWidth) );
			StrokeWidths.Add( new Vector4(StrokeWidth,StrokeWidth,StrokeWidth,StrokeWidth) );
			ShapeMetas.Add( ShapeMeta );
			ShapeMetas.Add( ShapeMeta );
			ShapeMetas.Add( ShapeMeta );
			ShapeMetas.Add( ShapeMeta );
			//	two triangles for quad
			Indexes.Add(VertexIndex+0);
			Indexes.Add(VertexIndex+1);
			Indexes.Add(VertexIndex+3);

			Indexes.Add(VertexIndex+1);
			Indexes.Add(VertexIndex+2);
			Indexes.Add(VertexIndex+3);
		}

		//	returns index of path point
		int AddPathPoint(Vector2 Position,bool ApplyFlip=true)
		{
			var Index = PathPoints.Count;
			if ( ApplyFlip ) 
			{
				Position.y = Frame.CanvasRect.yMax - Position.y;
			} 
			PathPoints.Add(new Vector4(Position.x, Position.y, 0, 0) );
			return Index;
		}

		//	returns Shape meta & bounds
		(Vector4,Rect?) WritePaths(IEnumerable<PopLottie.RenderCommands.Path> Paths)
		{
			var NewPathMetas = new List<Vector4>();
			Rect? Bounds = null;
			
			foreach (var Path in Paths )
			{
				//	gr: accumulate
				if ( Bounds == null )
					Bounds = Path.Bounds;
				if ( Path.Bounds is Rect NewBounds )
				{
					Rect BigRect = new();
					BigRect.xMin = Mathf.Min( NewBounds.xMin, Bounds.Value.xMin );
					BigRect.yMin = Mathf.Min( NewBounds.yMin, Bounds.Value.yMin );
					BigRect.xMax = Mathf.Max( NewBounds.xMax, Bounds.Value.xMax );
					BigRect.yMax = Mathf.Max( NewBounds.yMax, Bounds.Value.yMax );
					Bounds = BigRect;
				}
				
				if ( Path.EllipsePath is RenderCommands.Ellipse e )
				{
					var FirstIndexe = AddPathPoint( e.Center );
					AddPathPoint( e.Radius, false );
					NewPathMetas.Add( GetPathMeta( PATH_TYPE_ELLIPSE, FirstIndexe, 2 ) );
				}
				else if ( Path.BezierPath?.Length > 0 )
				{
					var FirstIndexb = AddPathPoint( Path.BezierPath[0].End );
					var LastIndex = FirstIndexb;

					for ( var p=1;	p<Path.BezierPath.Length;	p++ )
					{
						var NextPoint = Path.BezierPath[p];
						var PrevPoint = Path.BezierPath[p-1];
						var Start = PrevPoint.End;
						var ControlIn = NextPoint.ControlPointIn;
						var ControlOut = NextPoint.ControlPointOut;
						var End = NextPoint.End;
						AddPathPoint( ControlIn );
						AddPathPoint( ControlOut );
						LastIndex = AddPathPoint(End);
					}
					/*
					//	close path
					AddPathPoint( Path.BezierPath[Path.BezierPath.Length-1].End );
					AddPathPoint( Path.BezierPath[0].End );
					LastIndex = AddPathPoint( Path.BezierPath[0].End );
					*/
					int Countb = (LastIndex - FirstIndexb) + 1;
					NewPathMetas.Add( GetPathMeta( PATH_TYPE_BEZIER, FirstIndexb, Countb ) );
				}
				else
				{
					NewPathMetas.Add( GetPathMeta( PATH_TYPE_NULL, 0, 0 ) );
				}
			}
			
			var FirstPathIndex = PathMetas.Count;
			PathMetas.AddRange(NewPathMetas);
			var PathCount = NewPathMetas.Count;
			var ShapeMeta = GetShapeMeta( FirstPathIndex, PathCount );
			return (ShapeMeta,Bounds);
		}


		var z = 0;
		
		
		if ( IncludeDebug )
		{
			//	draw canvas
			var CanvasPath = RenderCommands.Path.CreateRect( Frame.CanvasRect.center, Frame.CanvasRect.size );
			var (ShapeMeta,Bounds) = WritePaths( new[] { CanvasPath} );
			var DebugFill = new Color(1,0,1,0.3f);
			AddQuad( Bounds, ShapeMeta, z, DebugFill, null, 0 );
			z++;
		}
		
		
		var RenderFirstToLast = true;
		var Shapes = RenderFirstToLast ? Frame.Shapes : Frame.Shapes.ToArray().Reverse();
		foreach (var Shape in Shapes)
		{
			var Fill = Shape.Style.FillColour;
			var Stroke = Shape.Style.StrokeColour;
			var StrokeWidth = Shape.Style.StrokeWidth;
			var Paths = RenderFirstToLast ? Shape.Paths : Shape.Paths.Reverse();
			var (ShapeMeta,Bounds) = WritePaths(Paths);
			
			AddQuad( Bounds, ShapeMeta, z, Fill, Stroke, StrokeWidth??0 );
			z++;
		}
		
		var Mesh = new Mesh();
		Mesh.SetVertices(LocalPositions);
		//Mesh.SetUVs(0,QuadUvs);
		Mesh.SetColors(FillColours);
		Mesh.SetUVs(0,StrokeColours);
		Mesh.SetUVs(1,StrokeWidths);
		Mesh.SetUVs(2,ShapeMetas);
		Mesh.SetTriangles(Indexes,0,true);
		
		Mesh.bounds = new Bounds( Frame.CanvasRect.center, Frame.CanvasRect.size );
		//Mesh.RecalculateBounds();
		
		var meshAndUniforms = new MeshAndUniforms();
		meshAndUniforms.Mesh = Mesh;
		meshAndUniforms.PathMetas = PathMetas;
		meshAndUniforms.PathPoints = PathPoints;
		return meshAndUniforms;
	}
}
