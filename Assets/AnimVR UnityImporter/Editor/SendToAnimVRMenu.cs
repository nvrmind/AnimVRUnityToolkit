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

        var filename = Path.GetTempFileName();
        if (!BuildPipeline.BuildAssetBundle(Selection.activeGameObject,
            new Object[] { Selection.activeGameObject },
            filename, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.CollectDependencies | BuildAssetBundleOptions.CompleteAssets, BuildTarget.StandaloneWindows64))
        {
            UnityEngine.Debug.Log("AssetBundle build failed.");
            return;
        }

        var assetBytes = File.ReadAllBytes(filename);
        File.Delete(filename);

        WWWForm form = new WWWForm();
        form.AddBinaryData("assetbundle", assetBytes);

        WWW www = new WWW("http://localhost:62346/", form);

        while (!www.isDone) ;

        Debug.Log(www.error);
    }
}
