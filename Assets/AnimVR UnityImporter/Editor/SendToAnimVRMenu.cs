using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public static class SendToAnimVRMenu {

    [MenuItem("Assets/Send To AnimVR", false, -49)]
    static void SendToAnimVR()
    {
        if(!Selection.activeGameObject)
        {
            UnityEngine.Debug.Log("No GameObject selected.");
            return;
        }

        var prevRenderingPath = PlayerSettings.stereoRenderingPath;
        var prevVRSupport = PlayerSettings.virtualRealitySupported;
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
        PlayerSettings.virtualRealitySupported = true;

        var filename = Path.GetTempFileName();
#pragma warning disable CS0618 // Type or member is obsolete
        if (!BuildPipeline.BuildAssetBundle(Selection.activeGameObject,
            new Object[] { Selection.activeGameObject },
            filename, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.CollectDependencies | BuildAssetBundleOptions.CompleteAssets, BuildTarget.StandaloneWindows64))
#pragma warning restore CS0618 // Type or member is obsolete
        {
            UnityEngine.Debug.Log("AssetBundle build failed.");
            return;
        }

        /*if (false)
        {
            var assetBytes = File.ReadAllBytes(filename);

            WWWForm form = new WWWForm();
            form.AddBinaryData("assetbundle", assetBytes);

            WWW www = new WWW("http://localhost:62346/", form);

            while (!www.isDone) ;

            if (string.IsNullOrEmpty(www.error))
            {
                Debug.Log("Success!");
            }
            else
            {
                Debug.LogError(www.error);
            }
        }
        else*/
        {
            WWW www = new WWW("http://localhost:62346/?path=" + WWW.EscapeURL(filename));

            while (!www.isDone) ;

            if (string.IsNullOrEmpty(www.error))
            {
                Debug.Log("Success!");
            }
            else
            {
                Debug.LogError(www.error);
            }
        }
        PlayerSettings.virtualRealitySupported = prevVRSupport;
        PlayerSettings.stereoRenderingPath = prevRenderingPath;

        File.Delete(filename);
    }
}
