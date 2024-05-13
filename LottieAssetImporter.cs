using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
#endif


/*
    This imports anything with a .lottie extension as a custom asset type.
    This isn't required by the UIToolkit element, but this does mean we can 
    treat this asset as a custom type, which then means we can do custom previews
    in the inspector
*/
//  gr: this MUST be named the same as the filename
#if UNITY_EDITOR
[ScriptedImporter(1, "lottie")]
public class LottieAssetImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var Json = System.IO.File.ReadAllText(ctx.assetPath);
        //var Animation = new PopLottie.Animation(Json);
        
        var animationContainer = ScriptableObject.CreateInstance<LottieAsset>();
        //animationContainer.Animation = Animation;
        animationContainer.Json = Json;
        
        //  cache the parsed animation to the asset
        ctx.AddObjectToAsset("PopLottie_Animation", animationContainer);
        ctx.SetMainObject(animationContainer);
        
    }
}
#endif

/*
#if UNITY_EDITOR
[CustomEditor(typeof(LottieImporter))]
public class ExampleEditor : UnityEditor.AssetImporters.ScriptedImporterEditor
{
    public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
    {
        Debug.Log($"RenderStaticPreview {assetPath}");
		return Texture2D.redTexture;
    }
}
#endif
*/


#if UNITY_EDITOR
[CustomEditor(typeof(LottieAssetImporter))]
//public class LottieAssetImporterEditor : Editor//UnityEditor.AssetImporters.ScriptedImporterEditor
public class LottieAssetImporterEditor : UnityEditor.AssetImporters.ScriptedImporterEditor
{
    public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
    {
        Debug.Log($"RenderStaticPreview {assetPath}");
		return Texture2D.redTexture;
    }
}
#endif
