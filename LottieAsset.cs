using System;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using System.Linq;
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
	//public PopLottie.Animation  Animation;  //  cache
	public PopLottie.Animation  Animation => PopLottie.Animation.Parse(Json);
	public string               Json;
	
	public Texture2D            GetPreview(int Width,int Height)
	{
		//  render the asset to a painter to get a vector image
		//  then rasterise the vector
		var Anim = this.Animation;
		var Painter = new UnityEngine.UIElements.Painter2D();
		
		//  making the vector larger, results in a much higher quality tesselation
		//  when it renders, it shrinks to bounds, so we kinda want to scale this to
		//  the width/height
		var VectorScalar = 5;
		var RasterisedTextureScalar = 1;
		
		var RenderRect = new Rect(0,0,Width*VectorScalar,Height*VectorScalar);
		var Frame = Anim.Render(0.0f,RenderRect,ScaleMode.ScaleToFit);
		Frame.Render(Painter);
		
		var Vector = ScriptableObject.CreateInstance<VectorImage>();
		if ( !Painter.SaveToVectorImage(Vector) )
			throw new Exception("Failed to make vector image from painter");

		//  gr: VectorImage is serialisable... so can we access all the scriptable object's contents by serialising?
		var VectorJson = JsonUtility.ToJson(Vector);
		var VectorUnlocked = JsonUtility.FromJson<VectorImageHack.InternalBridge.VectorImageUnlocked>(VectorJson);

		var RenderShader = Shader.Find("Unlit/VectorUI");
		Material renderMat = new Material(RenderShader);
		var Texture = RenderVectorImageToTexture2D(VectorUnlocked,Width*RasterisedTextureScalar,Height*RasterisedTextureScalar,renderMat);

		return Texture;
	}
	
	
	 public static Texture2D RenderVectorImageToTexture2D(VectorImageHack.InternalBridge.VectorImageUnlocked o, int width, int height, Material mat, int antiAliasing = 1)
		{
			if (o == null)
				return null;

			if (width <= 0 || height <= 0)
				return null;

			RenderTexture rt = null;
			var oldActive = RenderTexture.active;

			var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) {
				msaaSamples = antiAliasing,
				sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear
			};

			rt = RenderTexture.GetTemporary(desc);
			RenderTexture.active = rt;
			
			Vector2[] vertices = null;
			UInt16[] indices = null;
			Vector2[] uvs = null;
			Color[] colors = null;
			Vector2[] settingIndices = null;
			Texture2D atlas = null;
			Vector2 size = Vector2.zero;
			VectorImageHack.InternalBridge.GradientSettings/*Bridge*/[] settings = null;
			if (VectorImageHack.InternalBridge.GetDataFromVectorImage(o, ref vertices, ref indices, ref uvs, ref colors, ref settingIndices, ref settings, ref atlas, ref size))
			{
				vertices = vertices.Select(v => new Vector2(v.x/size.x, 1.0f-v.y/size.y)).ToArray();
				//Texture2D atlasWithEncodedSettings = atlas != null ? BuildAtlasWithEncodedSettings(settings, atlas) : null;
				Texture2D atlasWithEncodedSettings = null;
				RenderFromArrays(vertices, indices, uvs, colors, settingIndices, atlasWithEncodedSettings, mat);
				Texture2D.DestroyImmediate(atlasWithEncodedSettings);
			}
			else
			{
				RenderTexture.active = oldActive;
				RenderTexture.ReleaseTemporary(rt);
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
	
	internal static void RenderFromArrays(Vector2[] vertices, UInt16[] indices, Vector2[] uvs, Color[] colors, Vector2[] settings, Texture2D texture, Material mat, bool clear = true)
	{
		mat.SetTexture("_MainTex", texture);
		mat.SetPass(0);

		if (clear)
			GL.Clear(true, true, Color.clear);

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
	public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
	{   
		var Asset = target as LottieAsset;
		var Texture = Asset.GetPreview(width,height);
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
		try
		{
			var Asset = target as LottieAsset;
			if ( Asset == null )
				throw new Exception("Null asset");

			var Preview = Asset.GetPreview( (int)r.width, (int)r.height );
			GUI.DrawTexture(r, Preview, ScaleMode.ScaleToFit);
		}
		catch(Exception e)
		{
			GUI.Box( r, e.Message, GUIStyle.none );
		}
	}
}
#endif
