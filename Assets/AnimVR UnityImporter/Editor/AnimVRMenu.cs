using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System;

public static class AnimVRMenu {

    public static string GetSelectedPathOrFallback()
    {
        string path = "Assets";

        foreach (UnityEngine.Object obj in Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets))
        {
            path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                path = Path.GetDirectoryName(path);
                break;
            }
        }
        return path;
    }

    [MenuItem("Assets/Create/AnimVR/Stage")]
    public static void CreateStageMenu()
    {
        CreateStageAtPath( FileUtils.NextAvailableFilename(GetSelectedPathOrFallback()+"/Untitled.stage") , Color.gray);
    }

    public static StageData CreateStageAtPath(string path, Color backgroundColor)
    {
        var result = new StageData();
        result.previewFrames = new byte[1][];
        result.previewFrames[0] = Texture2D.whiteTexture.EncodeToPNG();

        result.transform = new SerializableTransform();
        result.backgroundColor.C = backgroundColor;
        result.guid = Guid.NewGuid();

        var symbol = new SymbolData();
        var timeline = new TimeLineData();
        var frame = new FrameData();

        timeline.Frames.Add(frame);
        symbol.Playables.Add(timeline);
        result.Symbols.Add(symbol);

        AnimData.WriteToFile(path, result);

        AssetDatabase.Refresh();

        return result;
    }
}
