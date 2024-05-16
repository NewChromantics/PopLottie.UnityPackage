using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace PopLottie
{
	
}



//	todo: instead of drawing immediately in update
//		generate a mesh + uniforms and update a meshfilter & mesh renderer 
[ExecuteInEditMode]
public class LottieSdf : MonoBehaviour
{
	public LottieAsset			animationAsset;
	(PopLottie.Animation,int)?	animationCacheAndHash;
	PopLottie.Animation			animation => GetCachedAnimation();
	
	Shader					shader => Shader.Find("Unlit/VectorUI");
	Material				material => new Material(shader);
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
		var Mesh = GenerateLayerMesh(Frame,Flip:true);
		var VectorToLocalTransform = Matrix4x4.identity;
	
		var RenderParams = new RenderParams(material);
		var LocalToWorld = this.transform.localToWorldMatrix;
		var RenderObjectToWorld = LocalToWorld * VectorToLocalTransform;
		
		Graphics.RenderMesh(RenderParams,Mesh,0,RenderObjectToWorld);
		
		//Debug.Log($"Triangle count; {Vector.indices.Length/3} - size{Vector.size}");
	}
	
	//	make a mesh with quads for every shape/layer
	//	todo: also need uniforms for instructions, colours etc for the shapes
	Mesh GenerateLayerMesh(PopLottie.RenderCommands.AnimationFrame Frame,bool Flip)
	{
		var Indexes = new List<int>();
		//	todo: turn all this into instancing data!
		//		first version just simpler for shader dev
		var Positions = new List<Vector3>();
		var FillColours = new List<Color>();
		var StrokeColours = new List<Vector4>();	//	texcoord0
		var StrokeMetas = new List<Vector4>();		//	texcoord1	x=strokewidth
		
		void AddQuad(Rect? bounds,int z,Color? FillColourMaybe,Color? StrokeColourMaybe,float StrokeWidth)
		{
			if ( bounds == null )
				return;
			var rect = bounds.Value;
			
			var ClearColour = new Color(1,0,1,0.1f);
			var FillColour = FillColourMaybe ?? ClearColour;
			var StrokeColour = StrokeColourMaybe ?? ClearColour;
			var StrokeMeta = new Vector4( StrokeWidth, 0, 0, 0 );
			
			var VertexIndex = Positions.Count;
			float zf = z * ZSpacing;
			var FlipMult = Flip ? -1 : 1;
			var tl = new Vector3( rect.xMin, rect.yMin*FlipMult, zf );
			var tr = new Vector3( rect.xMax, rect.yMin*FlipMult, zf );
			var br = new Vector3( rect.xMax, rect.yMax*FlipMult, zf );
			var bl = new Vector3( rect.xMin, rect.yMax*FlipMult, zf );
			Positions.Add(tl);
			Positions.Add(tr);
			Positions.Add(br);
			Positions.Add(bl);
			FillColours.Add(FillColour);
			FillColours.Add(FillColour);
			FillColours.Add(FillColour);
			FillColours.Add(FillColour);
			StrokeColours.Add(StrokeColour);
			StrokeColours.Add(StrokeColour);
			StrokeColours.Add(StrokeColour);
			StrokeColours.Add(StrokeColour);
			StrokeMetas.Add(StrokeMeta);
			StrokeMetas.Add(StrokeMeta);
			StrokeMetas.Add(StrokeMeta);
			StrokeMetas.Add(StrokeMeta);
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
				AddQuad( Path.Bounds, z, Fill, Stroke, StrokeWidth??0 );
				z++;
			}
		}
		
		var Mesh = new Mesh();
		Mesh.SetVertices(Positions);
		Mesh.SetColors(FillColours);
		Mesh.SetUVs(0,StrokeColours);
		Mesh.SetUVs(1,StrokeMetas);
		Mesh.SetTriangles(Indexes,0,true);
		return Mesh;
	}
}
