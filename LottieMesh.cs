using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;


namespace PopLottie
{
	public static class AnimationMesh
	{
		public static VectorImageHack.InternalBridge.VectorImageUnlocked	GetImageMesh(RenderCommands.AnimationFrame Frame,bool RenderDebug)
		{
			var Painter = new UnityEngine.UIElements.Painter2D();
		
			//  making the vector larger, results in a much higher quality tesselation
			//  when it renders, it shrinks to bounds, so we kinda want to scale this to
			//  the width/height
			var VectorScalar = 5;
			var RasterisedTextureScalar = 1;
			
			Frame.Render(Painter);
			if ( RenderDebug )
				Frame.RenderDebug(Painter);
		
			var Vector = ScriptableObject.CreateInstance<VectorImage>();
			if ( !Painter.SaveToVectorImage(Vector) )
				throw new Exception("Failed to make vector image from painter");

			//  gr: VectorImage is serialisable... so can we access all the scriptable object's contents by serialising?
			var VectorJson = JsonUtility.ToJson(Vector);
			var VectorUnlocked = JsonUtility.FromJson<VectorImageHack.InternalBridge.VectorImageUnlocked>(VectorJson);

			return VectorUnlocked;
		}
		
		public static Mesh	GetMesh(VectorImageHack.InternalBridge.VectorImageUnlocked Vector,bool FlipMesh)
		{
			var InputIndexes = Vector.indices;
			var InputVertexes = Vector.vertices;

			//	output
			var Positions = new Vector3[InputIndexes.Length];
			var Colours = new Color32[InputIndexes.Length];
			var OutputIndexes = new int[InputIndexes.Length];
			
			for (int i=0;	i<InputIndexes.Length;	i+=3)
			{
				var a = i+0;
				var b = i+1;
				var c = i+2;
				var va = InputVertexes[InputIndexes[a]];
				var vb = InputVertexes[InputIndexes[b]];
				var vc = InputVertexes[InputIndexes[c]];
				OutputIndexes[a] = a;
				OutputIndexes[b] = b;
				OutputIndexes[c] = c;
				
				var Pos2a = va.position;
				var Pos2b = vb.position;
				var Pos2c = vc.position;
				if ( FlipMesh )
				{
					Pos2a.y = Vector.height - Pos2a.y;
					Pos2b.y = Vector.height - Pos2b.y;
					Pos2c.y = Vector.height - Pos2c.y;
				}
				var Coloura = va.tint;
				var Colourb = vb.tint;
				var Colourc = vc.tint;
				
				Positions[a] = Pos2a;
				Positions[b] = Pos2b;
				Positions[c] = Pos2c;
				Colours[a] = Coloura;
				Colours[b] = Colourb;
				Colours[c] = Colourc;
			}
			
			var Output = new Mesh();
			Output.SetVertices(Positions);
			Output.SetColors(Colours);
			Output.SetTriangles(OutputIndexes,0);
			
			return Output;
		}
		
		
		//	FrameRectScale is the scale you rendered the AnimationFrame at if bigger than
		//	the desired output (ie. to improve tesselation quality)
		//	gr: consider putting this multiplier in the Render()->Painter2D but that's needless CPU work... 
		public static (Mesh,Matrix4x4)	GetMesh(RenderCommands.AnimationFrame Frame,float FrameRectScale,bool FlipMesh)
		{
			var Vector = GetImageMesh(Frame,RenderDebug:false);
			var Mesh = GetMesh(Vector,FlipMesh);
			
			//	undo our quality scalar
			var ScaleDownf = 1.0f / FrameRectScale;
			var ScaleDown = Matrix4x4.Scale( new Vector3(ScaleDownf,ScaleDownf,1) );
			//	gr: we need to do y = height - y, not *-1. This is current done in the vector->mesh stuff
			var Flip = Matrix4x4.Scale( new Vector3(1,1,1));
			
			//	Vector width/height is the tesselated output bounds, rather than canvas size, so this centering moves around
			//	the vertexes are also shifted to the top left of min/max... but we never know what that is
			//	so as it stands, we never know the real origin frame-to-frame
			//var Center = Matrix4x4.Translate( new Vector3(Vector.width*-0.5f,Vector.height*-0.5f,0) );
			//var Center = Matrix4x4.Translate( new Vector3(RenderRect.width*-0.5f,RenderRect.height*-0.5f,0) );
			var Center = Matrix4x4.Translate( new Vector3(0,0,0) );
			
			//var RenderObjectToWorld = Flip * ScaleDown * LocalToWorld;
			var RenderObjectToWorld = ScaleDown * Center * Flip;
			return (Mesh,RenderObjectToWorld);
		}
		
	}	
}



//	todo: instead of drawing immediately in update
//		generate a mesh + uniforms and update a meshfilter & mesh renderer 
[ExecuteInEditMode]
public class LottieMesh : MonoBehaviour
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
	[Range(1f,5000)]
	public float			QualityScalar = 100;
	
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
		var Width = WorldSize * QualityScalar;
		var Height = WorldSize * QualityScalar;
		var RenderRect = new Rect(0,0,Width,Height);
		var RenderTime = RenderAnimationTime;
		//Debug.Log($"Playing frame {RenderTime}");
		var Frame = anim.Render(RenderTime,RenderRect,ScaleMode.ScaleToFit);
	
		//	gr: don't really wanna do this on cpu! and instead see if we can
		//		dump the values into buffers for a shader
		var MeshAndTransform = PopLottie.AnimationMesh.GetMesh(Frame,QualityScalar,FlipMesh:true);
		var Mesh = MeshAndTransform.Item1;
		var VectorToLocalTransform = MeshAndTransform.Item2;
	
		var RenderParams = new RenderParams(material);
		var LocalToWorld = this.transform.localToWorldMatrix;
		var RenderObjectToWorld = LocalToWorld * VectorToLocalTransform;
		
		Graphics.RenderMesh(RenderParams,Mesh,0,RenderObjectToWorld);
		
		//Debug.Log($"Triangle count; {Vector.indices.Length/3} - size{Vector.size}");
	}
}
