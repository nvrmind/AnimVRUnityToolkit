using System;
using UnityEditor;
using UnityEngine;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine.Playables;

namespace ANIMVR
{
    [CustomEditor(typeof(AnimVRImporter))]
    public class AnimVRImporterEditor : ScriptedImporterEditor
    {
        public override bool showImportedObject
        {
            get
            {
                return false;
            }
        }

        protected override bool useAssetDrawPreview
        {
            get
            {
                return true;
            }
        }

        public override string GetInfoString()
        {
            var importer = (serializedObject.targetObject as AnimVRImporter);
            return importer.InfoString;
        }

        public override bool HasPreviewGUI()
        {
            return true;
        }

        Shader shader;


        public override void OnInspectorGUI()
        {
            var importer = serializedObject.targetObject as AnimVRImporter;
            var settings = importer.Settings;
            var serSettings = serializedObject.FindProperty(() => importer.Settings);

            var shaderProp = serSettings.FindPropertyRelative(() => settings.Shader);

            if (!shader)
            {
                Debug.Log("Reloading shader: " + shaderProp.stringValue);
                shader = Shader.Find(shaderProp.stringValue);
            }

            shader = EditorGUILayout.ObjectField(new GUIContent("Base Shader", "The shader to use for all materials."), shader, typeof(Shader), false) as Shader;

            shaderProp.stringValue = shader ? shader.name : "AnimVR/Standard";

            AddEnumProperty(serSettings.FindPropertyRelative(() => settings.DefaultWrapMode), "Default Wrap Mode", "The wrap mode to set the imported PlayableDirector to.", typeof(DirectorWrapMode));
            AddEnumProperty(serSettings.FindPropertyRelative(() => settings.AudioImport), "Audio Import Setting", "Set what part of the audio in the stage to import.", typeof(AudioImportSetting));

            if(((AudioImportSetting)serSettings.FindPropertyRelative(() => settings.AudioImport).intValue) == AudioImportSetting.ClipsAndTracks)
            {
                EditorGUILayout.HelpBox("This setting is experimental, use at your own risk!", MessageType.Warning);
            }

            AddBoolProperty(serSettings.FindPropertyRelative(() => settings.ImportCameras), "Import Cameras", "Import Camera Layers.");

            if (importer.needsAudioReimport)
            {
                if (GUILayout.Button(new GUIContent("Fixup audio clips")))
                {
                    try
                    {
                        AssetDatabase.StartAssetEditing();
                        foreach (string path in GetAssetPaths())
                            AssetDatabase.ImportAsset(path);
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                    }
                }
            }


            if(importer.HasFades)
            {
                EditorGUILayout.HelpBox("This scene uses features that are not yet supported in the Unity Toolkit!\n - Fading layers", MessageType.Warning);
            }

            base.ApplyRevertGUI();
        }
        
        private string[] GetAssetPaths()
        {
            var targets = this.targets;
            string[] strArray = new string[targets.Length];
            for (int index = 0; index < targets.Length; ++index)
            {
                AssetImporter assetImporter = targets[index] as AssetImporter;
                strArray[index] = assetImporter.assetPath;
            }
            return strArray;
        }

        void AddObjectProperty<T>(SerializedProperty property, string text, string tooltip) where T : UnityEngine.Object
        {
            var orgValue = property.objectReferenceValue as T;
            var newValue = EditorGUILayout.ObjectField(new GUIContent(text, tooltip), orgValue, typeof(T), false) as T;
            property.objectReferenceValue = newValue;
        }
        
        private void AddBoolProperty(SerializedProperty porperty, string text, string tooltip)
        {
            var orgValue = porperty.boolValue;
            var newValue = EditorGUILayout.Toggle(new GUIContent(text, tooltip), orgValue);
            porperty.boolValue = newValue;
        }

        void AddEnumProperty(SerializedProperty porperty, string text, string tooltip, Type typeOfEnum)
        {
            Rect ourRect = EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginProperty(ourRect, GUIContent.none, porperty);

            int selectionFromInspector = porperty.intValue;
            string[] enumNamesList = System.Enum.GetNames(typeOfEnum);
            var actualSelected = EditorGUILayout.Popup(text, selectionFromInspector, enumNamesList);
            porperty.intValue = actualSelected;
            EditorGUI.EndProperty();
            EditorGUILayout.EndHorizontal();
        }

        void AddIntProperty(SerializedProperty porperty, string text, string tooltip)
        {
            var orgValue = porperty.intValue;
            var newValue = EditorGUILayout.IntField(new GUIContent(text, tooltip), orgValue);
            porperty.intValue = newValue;
        }
    }
}