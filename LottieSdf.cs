using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
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
	const int PATH_DATA_COUNT = 300;
	const int PATH_DATATYPE_NULL = 0;
	const int PATH_DATATYPE_ELLIPSE = 1;
	const int PATH_DATATYPE_BEZIER = 2;
	const int PATH_DATAROW_META = 0;
	const int PATH_DATAROW_POSITION = 1;

	public LottieAsset			animationAsset;
	(PopLottie.Animation,int)?	animationCacheAndHash;
	PopLottie.Animation			animation => GetCachedAnimation();
	
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
		var meshAndPathDatas = GenerateLayerMesh(Frame,Flip:true);
		var Mesh = meshAndPathDatas.Mesh;
		var VectorToLocalTransform = Matrix4x4.identity;
	
		var RenderParams = new RenderParams(material);
		//	gr: pad out this array to stop unity baking the max size
		meshAndPathDatas.PathDatas.AddRange( new Matrix4x4[PATH_DATA_COUNT-meshAndPathDatas.PathDatas.Count] );
		material.SetMatrixArray("PathDatas",meshAndPathDatas.PathDatas);
		
		var LocalToWorld = this.transform.localToWorldMatrix;
		var RenderObjectToWorld = LocalToWorld * VectorToLocalTransform;
		
		Graphics.RenderMesh(RenderParams,Mesh,0,RenderObjectToWorld);
		
		//Debug.Log($"Triangle count; {Vector.indices.Length/3} - size{Vector.size}");
	}
	
	struct MeshAndPathDatas
	{
		public Mesh				Mesh;
		public List<Matrix4x4>	PathDatas;	//	just 16 floats
	}
	
	//	make a mesh with quads for every shape/layer
	//	todo: also need uniforms for instructions, colours etc for the shapes
	MeshAndPathDatas GenerateLayerMesh(PopLottie.RenderCommands.AnimationFrame Frame,bool Flip)
	{
		var Indexes = new List<int>();
		
		//	todo: turn all this into instancing data!
		//		first version just simpler for shader dev
		var LocalPositions = new List<Vector3>();
		var QuadUvs = new List<Vector2>();	//	0,0...1,1
		var FillColours = new List<Color>();
		var StrokeColours = new List<Vector4>();	//	texcoord0
		var PathMetas = new List<Vector4>();		//	texcoord1	x=PathIndex	y=strokewidth
		
		//	uniform data
		var PathDatas = new List<Matrix4x4>();
		var FlipMult = Flip ? -1 : 1;
		
		void AddQuad(Rect? bounds,int PathIndex,int z,Color? FillColourMaybe,Color? StrokeColourMaybe,float StrokeWidth)
		{
			if ( bounds == null )
				return;
			var rect = bounds.Value;
			
			var ClearColour = new Color(1,0,1,0.1f);
			var FillColour = FillColourMaybe ?? ClearColour;
			var StrokeColour = StrokeColourMaybe ?? ClearColour;
			var PathMeta = new Vector4( PathIndex, StrokeWidth, 0, 0 );
			
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
			PathMetas.Add(PathMeta);
			PathMetas.Add(PathMeta);
			PathMetas.Add(PathMeta);
			PathMetas.Add(PathMeta);
			//	two triangles for quad
			Indexes.Add(VertexIndex+0);
			Indexes.Add(VertexIndex+1);
			Indexes.Add(VertexIndex+3);

			Indexes.Add(VertexIndex+1);
			Indexes.Add(VertexIndex+2);
			Indexes.Add(VertexIndex+3);
		}

		var z = 0;
		var Shapes = RenderFirstToLast ? Frame.Shapes : Frame.Shapes.ToArray().Reverse();
		foreach (var Shape in Shapes)
		{
			var Fill = Shape.Style.FillColour;
			var Stroke = Shape.Style.StrokeColour;
			var StrokeWidth = Shape.Style.StrokeWidth;
			var Paths = RenderFirstToLast ? Shape.Paths : Shape.Paths.Reverse();
			foreach (var Path in Paths )
			{
				List<Matrix4x4> PathPathDatas = new ();
				//	make path data
				if ( Path.EllipsePath is RenderCommands.Ellipse e )
				{
					var PathData = new Matrix4x4();
					PathData.SetRow(PATH_DATAROW_META, new Vector4(PATH_DATATYPE_ELLIPSE,0,0,0) );
					PathData.SetRow(PATH_DATAROW_POSITION, new Vector4( e.Center.x, e.Center.y*FlipMult, e.Radius.x, e.Radius.y ) );
					PathPathDatas.Add(PathData);
				}
				else if ( Path.BezierPath?.Length > 0 )
				{
					foreach ( var Point in Path.BezierPath )
					{
						var PathData = new Matrix4x4();
						PathData.SetRow(PATH_DATAROW_META, new Vector4(PATH_DATATYPE_BEZIER,0,0,0) );
						PathData.SetRow(PATH_DATAROW_POSITION+0, new Vector4( Point.Position.x, Point.Position.y*FlipMult, 0, 0 ) );
						PathData.SetRow(PATH_DATAROW_POSITION+1, new Vector4( Point.ControlPointIn.x, Point.ControlPointIn.y*FlipMult, Point.ControlPointOut.x, Point.ControlPointOut.y*FlipMult ) );
						PathPathDatas.Add(PathData);
					}
				}
				else
				{
					var PathData = new Matrix4x4();
					PathData.SetRow(PATH_DATAROW_META, new Vector4(PATH_DATATYPE_NULL,0,0,0) );
					PathPathDatas.Add(PathData);
				}
				
				foreach ( var SubPathData in PathPathDatas )
				{
					var PathIndex = PathDatas.Count;
					PathDatas.Add(SubPathData);
			
					AddQuad( Path.Bounds, PathIndex, z, Fill, Stroke, StrokeWidth??0 );
				}
				z++;
			}
		}
		
		var Mesh = new Mesh();
		Mesh.SetVertices(LocalPositions);
		Mesh.SetColors(FillColours);
		Mesh.SetUVs(0,QuadUvs);
		Mesh.SetUVs(1,StrokeColours);
		Mesh.SetUVs(2,PathMetas);
		Mesh.SetTriangles(Indexes,0,true);
		
		var MeshAndPath = new MeshAndPathDatas();
		MeshAndPath.Mesh = Mesh;
		MeshAndPath.PathDatas = PathDatas;
		return MeshAndPath;
	}
}
