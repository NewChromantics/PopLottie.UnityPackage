using System;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using System.Linq;
using UnityEditor.Graphs;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
#endif


//  basically a duplicate of
//      namespace Unity.VectorGraphics
//          internal static partial class InternalBridge
//  except not internal to that assembly
namespace VectorImageHack
{
	public static class InternalBridge
	{
		//  these bridge structs are from VectorUtils, which are just the same structs as VectorImage
		[Serializable]
		public struct VectorImageVertex//Bridge
		{
			public Vector3 position;
			public Color32 tint;
			public Vector2 uv;
			public uint settingIndex;
		}

		[Serializable]
		public struct GradientSettings//Bridge
		{
			public GradientType/*Bridge*/ gradientType;
			public AddressMode/*Bridge*/ addressMode;
			public Vector2 radialFocus;
			public RectInt location;
		}

		[Serializable]
		public enum GradientType//Bridge
		{
			Linear = 0,
			Radial = 1
		}

		[Serializable]
		public enum AddressMode//Bridge
		{
			Wrap = 0,
			Clamp = 1,
			Mirror = 2
		}
		
		//  copy of UnityEngine.UIElements.VectorImage but fields aren't all internal
		[Serializable]
		public class VectorImageUnlocked
		{
			[SerializeField]
			public int version = 0;
			[SerializeField]
			public Texture2D atlas = (Texture2D) null;
			[SerializeField]
			public VectorImageVertex[] vertices = (VectorImageVertex[]) null;
			[SerializeField]
			public ushort[] indices = (ushort[]) null;
			[SerializeField]
			public GradientSettings[] settings = (GradientSettings[]) null;
			[SerializeField]
			public Vector2 size = Vector2.zero;

			/// <summary>
			///   <para>The width of the vector image.</para>
			/// </summary>
			public float width => this.size.x;

			/// <summary>
			///   <para>The height of the vector image.</para>
			/// </summary>
			public float height => this.size.y;
		}
		
		 public static bool GetDataFromVectorImage(VectorImageUnlocked vi, ref Vector2[] vertices, ref UInt16[] indices, ref Vector2[] uvs, ref Color[] colors, ref Vector2[] settingIndices, ref GradientSettings/*Bridge*/[] settings, ref Texture2D texture, ref Vector2 size)
		{
			//var vi = o as VectorImageUnlocked;
			if (vi == null)
				return false;
			
			vertices = vi.vertices.Select(v => (Vector2)v.position).ToArray();
			indices = vi.indices;
			uvs = vi.vertices.Select(v => v.uv).ToArray();
			colors = vi.vertices.Select(v => (Color)v.tint).ToArray();
			settingIndices = vi.atlas != null ? vi.vertices.Select(v => new Vector2(v.settingIndex, 0)).ToArray() : null;
			texture = vi.atlas;
			size = vi.size;

			settings = vi.settings.Select(s => new GradientSettings/*Bridge*/() {
				gradientType = (GradientType/*Bridge*/)s.gradientType,
				addressMode = (AddressMode/*Bridge*/)s.addressMode,
				radialFocus = s.radialFocus,
				location = s.location
			}).ToArray();

			return true;
		}
	}
}



//  gr: this MUST be named the same as the filename
[Serializable]
public class LottieAsset : ScriptableObject
{
	public PopLottie.Animation	Animation => GetCachedAnimation();
	public string				Json;
	
	//	using this private variable means it wont be serialised, but will be cached during editor session
	//	reimport asset to wipe it
	Texture2D					ThumbnailCache;
	int							ThumbnailCacheFrameMs = 0;
	//	gr: can't save this (public) as it doesn't seem to serialise properly
	PopLottie.Animation			AnimationCache;
	
	PopLottie.Animation GetCachedAnimation()
	{
		if ( AnimationCache == null )
		{
			AnimationCache = PopLottie.Animation.Parse(Json);
		}
		return AnimationCache;
	}
	
	public void ClearPreviewCache()
	{
		ThumbnailCache = null;
	}
	
	public TimeSpan? GetDuration()
	{
		try
		{
			if ( Animation.IsStatic )
				return null;
			return Animation.Duration;
		}
		catch//(Exception e)
		{
			return TimeSpan.Zero;
		}
	}
	
	public int GetPreviewCacheFrameMs()
	{
		return ThumbnailCacheFrameMs;
	}
	
	public Texture2D GetPreview(int Width,int Height,int FrameMs)
	{
		int MinSize = 400;

		//	clear old caches if we changed the min size
		//	gr: ThumbnaCache?.width here throws because variable is null??
		if ( ThumbnailCache != null )
		{
			var SizeChanged = ThumbnailCache?.width < MinSize || ThumbnailCache?.height < MinSize;
			var TimeChanged = ThumbnailCacheFrameMs!=FrameMs; 
			if ( TimeChanged || SizeChanged )
				ClearPreviewCache(); 
		}
		
		if ( ThumbnailCache != null )
		{
			//Debug.Log($"Using cache of {this.name} {ThumbnailCache.width}x{ThumbnailCache.height}");
			return ThumbnailCache;
		}
		
		//	"pre draw" previews have w/h as 1x1, i'm not sure what this is for...
		//	just always render a sensible size
		Width = Mathf.Max(MinSize,Width);
		Height = Mathf.Max(MinSize,Height);
		ThumbnailCache = RenderThumbnail(Width,Height, TimeSpan.FromMilliseconds(FrameMs) );
		ThumbnailCacheFrameMs = FrameMs;
		Debug.Log($"Rendering cache of {this.name} {ThumbnailCache.width}x{ThumbnailCache.height}");
		return ThumbnailCache;
	}

	Texture2D			RenderThumbnail(int Width,int Height,TimeSpan FrameTime)
	{
		//  render the asset to a painter to get a vector image
		//  then rasterise the vector
		var Anim = this.Animation;
		//  making the vector larger, results in a much higher quality tesselation
		//  when it renders, it shrinks to bounds, so we kinda want to scale this to
		//  the width/height
		var VectorScalar = 1;
		var RasterisedTextureScalar = 1;
		
		
		var RenderRect = new Rect(0,0,Width*VectorScalar,Height*VectorScalar);
		var Frame = Anim.Render(FrameTime,RenderRect,ScaleMode.ScaleToFit);

		//	render with sdf
		bool RenderWithSdfMethod = true;
		if ( RenderWithSdfMethod )
		{
			return RenderWithSdf( Frame, Width, Height, VectorScalar );
		}
		else
		{
			var Vector = PopLottie.AnimationMesh.GetImageMesh(Frame,RenderDebug:false);

			var RenderShader = Shader.Find("Unlit/VectorUI");
			Material renderMat = new Material(RenderShader);
			var Texture = RenderVectorImageToTexture2D(Vector,Width*RasterisedTextureScalar,Height*RasterisedTextureScalar,renderMat);
			//var Texture = RenderFrameToTexture2D(Frame,VectorScalar,Width,Height,renderMat);
			return Texture;
		}
	}
	
	static Texture2D RenderWithSdf(PopLottie.RenderCommands.AnimationFrame Frame,int Width,int Height,float VectorScale)
	{
		float ZSpacing = 0.0001f;
		var MeshAndUniforms = LottieSdf.GenerateLayerMesh( Frame, Flip:false, ZSpacing, IncludeDebug:true );

		void Render(RenderTexture rt,Color ClearColour)
		{
			//var MeshAndTransform = PopLottie.AnimationMesh.GetMesh(Frame,FrameQualityScalar,false);
			var Mesh = MeshAndUniforms.Mesh;
			
			var Transform = Matrix4x4.identity;
			Transform *= Matrix4x4.Scale( new Vector3(1/VectorScale,1/VectorScale,1));
			
			var AlignToCenter = false;
			if ( AlignToCenter )
			{
				var x = -Frame.CanvasRect.width / 2;
				var y = -Frame.CanvasRect.height / 2;
				Transform *= Matrix4x4.Translate( new Vector3(x,y) );
			}
			
			
			var RenderShader = Shader.Find("PopLottie/LottieSdfPath");
			Material RenderMat = new Material(RenderShader);
			MeshAndUniforms.ApplyUniforms(RenderMat);
			
			
			//	gr: for some reason, this doesn't work with icons...
			var cmdBuf = new UnityEngine.Rendering.CommandBuffer();
			cmdBuf.SetRenderTarget(rt);
			
			//cmdBuf.ClearRenderTarget(true, true, Color.magenta );
			//cmdBuf.SetViewport( new Rect(0,Height*0.1f,Width,Height) );
			
			cmdBuf.ClearRenderTarget(true, true, ClearColour );
			cmdBuf.DrawMesh(Mesh, Transform, RenderMat, 0);
			Graphics.ExecuteCommandBuffer(cmdBuf);
			/*
			var RenderParams = new RenderParams(RenderMat);
			GL.Clear(true, true, Color.red);
			Graphics.RenderMesh(RenderParams,Mesh,0,Transform);
			//Graphics.DrawMesh(Mesh,Transform,RenderMat,0);
			*/
			//var RenderParams = new RenderParams(mat);
			//Graphics.RenderMesh(RenderParams,Mesh,0,Transform);
			//Graphics.DrawMesh(Mesh,Transform,mat,0);
		}
		return RenderToTexture(Width,Height,1,Render);
	}
	
	public static Texture2D RenderFrameToTexture2D(PopLottie.RenderCommands.AnimationFrame Frame,float FrameQualityScalar, int width, int height, Material mat, int antiAliasing = 1)
	{
		void Render(RenderTexture rt,Color ClearColour)
		{
			var MeshAndTransform = PopLottie.AnimationMesh.GetMesh(Frame,FrameQualityScalar,false);
			var Mesh = MeshAndTransform.Item1;
			var Transform = MeshAndTransform.Item2;
			
			//	gr: for some reason, this doesn't work with icons...
			var cmdBuf = new UnityEngine.Rendering.CommandBuffer();
			cmdBuf.SetRenderTarget(rt);
			cmdBuf.ClearRenderTarget(true, true, ClearColour );
			cmdBuf.DrawMesh(Mesh, Transform, mat, 0);
			Graphics.ExecuteCommandBuffer(cmdBuf);
			
			var RenderParams = new RenderParams(mat);
			//Graphics.RenderMesh(RenderParams,Mesh,0,Transform);
			//Graphics.DrawMesh(Mesh,Transform,mat,0);
		}
		return RenderToTexture(width,height,antiAliasing,Render);
	}
	
	public static Texture2D RenderToTexture(int width,int height,int AntiAliasingSamples,Action<RenderTexture,Color> Render)
	{
		if (width <= 0 || height <= 0)
			return null;

		RenderTexture rt = null;
		var oldActive = RenderTexture.active;

		var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) {
			msaaSamples = AntiAliasingSamples,
			sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear
		};

		rt = RenderTexture.GetTemporary(desc);
		RenderTexture.active = rt;
		var ClearColour = new Color(0,1,1,0);
		try
		{
			Render(rt,ClearColour);
		}
		catch(Exception e)
		{
			RenderTexture.active = oldActive;
			RenderTexture.ReleaseTemporary(rt);
			Debug.LogException(e);
			return null;
		}

		Texture2D copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
		copy.hideFlags = HideFlags.HideAndDontSave;
		copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
		copy.Apply();

		RenderTexture.active = oldActive;
		RenderTexture.ReleaseTemporary(rt);

		return copy;
	}
	
	 public static Texture2D RenderVectorImageToTexture2D(VectorImageHack.InternalBridge.VectorImageUnlocked o, int width, int height, Material mat, int antiAliasing = 1)
	{
		void Render(RenderTexture rt,Color ClearColour)
		{
			if (o == null)
				throw new Exception("Missing vector image");
			
			Vector2[] vertices = null;
			UInt16[] indices = null;
			Vector2[] uvs = null;
			Color[] colors = null;
			Vector2[] settingIndices = null;
			Texture2D atlas = null;
			Vector2 size = Vector2.zero;
			VectorImageHack.InternalBridge.GradientSettings/*Bridge*/[] settings = null;
			if (!VectorImageHack.InternalBridge.GetDataFromVectorImage(o, ref vertices, ref indices, ref uvs, ref colors, ref settingIndices, ref settings, ref atlas, ref size))
				throw new Exception($"Failed to get mesh data from vector");

			vertices = vertices.Select(v => new Vector2(v.x/size.x, 1.0f-v.y/size.y)).ToArray();
			//Texture2D atlasWithEncodedSettings = atlas != null ? BuildAtlasWithEncodedSettings(settings, atlas) : null;
			Texture2D atlasWithEncodedSettings = null;
			RenderFromArrays(vertices, indices, uvs, colors, settingIndices, atlasWithEncodedSettings, mat,ClearColour);
			Texture2D.DestroyImmediate(atlasWithEncodedSettings);
		}
		return RenderToTexture(width,height,antiAliasing,Render);
	}
	
	internal static void RenderFromArrays(Vector2[] vertices, UInt16[] indices, Vector2[] uvs, Color[] colors, Vector2[] settings, Texture2D texture, Material mat,Color ClearColour)
	{
		mat.SetTexture("_MainTex", texture);
		mat.SetPass(0);

		GL.Clear(true, true, ClearColour);

		GL.PushMatrix();
		GL.LoadOrtho();
		GL.Color(new Color(1, 1, 1, 1));
		GL.Begin(GL.TRIANGLES);
		for (int i = 0; i < indices.Length; ++i)
		{
			ushort index = indices[i];
			Vector2 vertex = vertices[index];
			Vector2 uv = uvs[index];
			GL.TexCoord2(uv.x, uv.y);
			if (settings != null)
			{
				var setting = settings[index];
				GL.MultiTexCoord2(2, setting.x, setting.y);
			}
			if (colors != null)
				GL.Color(colors[index]);
			GL.Vertex3(vertex.x, vertex.y, 0);
		}
		GL.End();
		GL.PopMatrix();

		mat.SetTexture("_MainTex", null);
	}
	
}


#if UNITY_EDITOR
[CustomEditor(typeof(LottieAsset))]
//public class LottieAssetImporterEditor : Editor//UnityEditor.AssetImporters.ScriptedImporterEditor
public class LottieAssetEditor : UnityEditor.Editor
{
	//  gr: this seems to be the icon!
	//	w x h  = 128x128
	public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
	{   
		var Asset = target as LottieAsset;
		var Texture = Asset.GetPreview(width,height, FrameMs:0);
		return Texture;
	}
	
	//  new API lets us use UIToolkit
	/*  gr; but it breaks badly
	public override VisualElement CreateInspectorGUI()
	{
		VisualElement Inspector = new VisualElement();
		var AnimationElement = new PopLottie.LottieVisualElement();
		var Asset = target as LottieAsset;
		AnimationElement.Animation = Asset.Animation;
		Inspector.Add((new Label("This is a custom Inspector")));
		Inspector.Add(AnimationElement);
		return Inspector;
	}
	*/
}
#endif


#if UNITY_EDITOR
[CustomPreview(typeof(LottieAsset))]
public class MyPreview : ObjectPreview
{
	public override bool HasPreviewGUI()
	{
		return true;
	}

	public override void OnPreviewGUI(Rect r, GUIStyle background)
	{
		base.OnPreviewGUI(r, background);
		
		var TextStyle = new GUIStyle(GUI.skin.box);
		TextStyle.normal.textColor = Color.white;
		TextStyle.normal.background = Texture2D.blackTexture;
		try
		{
			var Asset = target as LottieAsset;
			if ( Asset == null )
				throw new Exception("Null asset");

			int ElementHeight = 20;
			int ElementMargin = 5;
			int ElementWidth = (int)Mathf.Min( r.width, 200 ) - ElementMargin - ElementMargin;

			var ElementRect = r;
			ElementRect.height = ElementHeight;
			ElementRect.width = ElementWidth;
			ElementRect.y = r.yMax - ElementHeight - ElementMargin;
			ElementRect.x += ElementMargin;
			
			int FrameMs = Asset.GetPreviewCacheFrameMs();
			
			if ( Asset.GetDuration() is TimeSpan Duration )
			{
				float FrameSecs = GUI.HorizontalSlider( ElementRect, FrameMs/1000f, 0, (float)Duration.TotalSeconds );
				FrameMs = (int)(FrameSecs * 1000f);
				ElementRect.y -= ElementRect.height + ElementMargin;
			
				GUI.Label( ElementRect, $"{FrameSecs:0.00}/{Duration.TotalSeconds:0.00} secs", TextStyle );
				ElementRect.y -= ElementRect.height + ElementMargin;
			}
			else
			{
				GUI.Label( ElementRect, $"(Static)", TextStyle );
				ElementRect.y -= ElementRect.height + ElementMargin;
			}
			
			if ( GUI.Button(ElementRect, "Clear Preview Cache") )
				Asset.ClearPreviewCache();
			ElementRect.y -= ElementRect.height + ElementMargin;
			
			var PreviewTexture = Asset.GetPreview( (int)r.width, (int)r.height, FrameMs );
			GUI.DrawTexture(r, PreviewTexture, ScaleMode.ScaleAndCrop);
			
		}
		catch(Exception e)
		{
			GUI.Box( r, e.Message, TextStyle );
		}
	}
}
#endif
