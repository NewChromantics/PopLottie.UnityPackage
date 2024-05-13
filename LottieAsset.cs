using System;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
#endif

//  gr: this MUST be named the same as the filename
[Serializable]
public class LottieAsset : ScriptableObject
{
    //public PopLottie.Animation  Animation;  //  cache
    public PopLottie.Animation  Animation => PopLottie.Animation.Parse(Json);
    public string               Json;
    
    public Texture2D            GetPreview(int Width,int Height)
    {
       
        var Texture = new Texture2D (Width, Height);
        Texture.name = $"LottieAsset Preview";
        var Pixels = Texture.GetPixels();
        for ( var i=0;  i<Pixels.Length;    i++ )
        {
            Pixels[i] = Color.yellow;
        }
        Texture.SetPixels(Pixels);
        Texture.Apply(true);
		return Texture;

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
