using System;
using UnityEditor;
using UnityEngine;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine.Playables;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace ANIMVR {
    [CustomEditor(typeof(AnimVRImporter))]
    public class AnimVRImporterEditor : ScriptedImporterEditor {
        public override bool showImportedObject {
            get {
                return false;
            }
        }

        protected override bool useAssetDrawPreview {
            get {
                return true;
            }
        }

        public override string GetInfoString() {
            var importer = (serializedObject.targetObject as AnimVRImporter);
            return importer.InfoString;
        }

        public override bool HasPreviewGUI() {
            return true;
        }

        Shader shader;

        // Material
        SerializedProperty m_Materials;
        SerializedProperty m_ExternalObjects;

        public override void OnEnable() {
            // Material
            m_Materials = serializedObject.FindProperty("m_Materials");
            m_ExternalObjects = serializedObject.FindProperty("m_ExternalObjects");
        }



        public override void OnInspectorGUI() {
            var importer = serializedObject.targetObject as AnimVRImporter;
            var settings = importer.Settings;
            var serSettings = serializedObject.FindProperty(() => importer.Settings);
            var shaderProp = serSettings.FindPropertyRelative(() => settings.Shader);

            if (!shader) {
                Debug.Log("Reloading shader: " + shaderProp.stringValue);
                shader = Shader.Find(shaderProp.stringValue);
            }

            shader = EditorGUILayout.ObjectField(new GUIContent("Base Shader", "The shader to use for all materials."), shader, typeof(Shader), false) as Shader;

            shaderProp.stringValue = shader ? shader.name : "AnimVR/ImportedLine";

            var simplifyProperty = serSettings.FindPropertyRelative(() => settings.SimplifyFactor);
            EditorGUILayout.Slider(simplifyProperty, 0.0f, 10.0f);

            AddEnumProperty(serSettings.FindPropertyRelative(() => settings.DefaultWrapMode), "Default Wrap Mode", "The wrap mode to set the imported PlayableDirector to.", typeof(DirectorWrapMode));
            AddEnumProperty(serSettings.FindPropertyRelative(() => settings.AudioImport), "Audio Import Setting", "Set what part of the audio in the stage to import.", typeof(AudioImportSetting));

            if (((AudioImportSetting)serSettings.FindPropertyRelative(() => settings.AudioImport).intValue) == AudioImportSetting.ClipsAndTracks) {
                EditorGUILayout.HelpBox("This setting is experimental, use at your own risk!", MessageType.Warning);
            }

            AddBoolProperty(serSettings.FindPropertyRelative(() => settings.ImportCameras), "Import Cameras", "Import Camera Layers.");

            if (importer.needsAudioReimport) {
                if (GUILayout.Button(new GUIContent("Fixup audio clips"))) {
                    try {
                        AssetDatabase.StartAssetEditing();
                        foreach (string path in GetAssetPaths())
                            AssetDatabase.ImportAsset(path);
                    } finally {
                        AssetDatabase.StopAssetEditing();
                    }
                }
            }

            DoMaterialsGUI();


            if (importer.HasFades) {
                EditorGUILayout.HelpBox("This scene uses features that are not yet supported in the Unity Toolkit!\n - Fading layers", MessageType.Warning);
            }

            base.ApplyRevertGUI();
        }

        void DoMaterialsGUI() {
            serializedObject.UpdateIfRequiredOrScript();

            // hidden for multi-selection
            if (targets.Length == 1 && m_Materials.arraySize > 0) {
                GUILayout.Label(Styles.ExternalMaterialMappings, EditorStyles.boldLabel);

                // The list of material names is immutable, whereas the map of external objects can change based on user actions.
                // For each material name, map the external object associated with it.
                // The complexity comes from the fact that we may not have an external object in the map, so we can't make a property out of it
                for (int materialIdx = 0; materialIdx < m_Materials.arraySize; ++materialIdx) {
                    var id = m_Materials.GetArrayElementAtIndex(materialIdx);
                    var name = id.FindPropertyRelative("name").stringValue;
                    var type = id.FindPropertyRelative("type").stringValue;
                    var assembly = id.FindPropertyRelative("assembly").stringValue;

                    SerializedProperty materialProp = null;
                    Material material = null;
                    var propertyIdx = 0;

                    for (int externalObjectIdx = 0, count = m_ExternalObjects.arraySize; externalObjectIdx < count; ++externalObjectIdx) {
                        var pair = m_ExternalObjects.GetArrayElementAtIndex(externalObjectIdx);
                        var externalName = pair.FindPropertyRelative("first.name").stringValue;
                        var externalType = pair.FindPropertyRelative("first.type").stringValue;

                        if (externalName == name && externalType == type) {
                            materialProp = pair.FindPropertyRelative("second");
                            material = materialProp != null ? materialProp.objectReferenceValue as Material : null;
                            propertyIdx = externalObjectIdx;
                            break;
                        }
                    }

                    GUIContent nameLabel = EditorGUIUtility.TrTextContent(name);
                    nameLabel.tooltip = name;
                    if (materialProp != null) {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.ObjectField(materialProp, typeof(Material), nameLabel);
                        if (EditorGUI.EndChangeCheck()) {
                            if (materialProp.objectReferenceValue == null) {
                                m_ExternalObjects.DeleteArrayElementAtIndex(propertyIdx);
                            }
                        }
                    } else {
                        EditorGUI.BeginChangeCheck();
                        material = EditorGUILayout.ObjectField(nameLabel, material, typeof(Material), false) as Material;
                        if (EditorGUI.EndChangeCheck()) {
                            if (material != null) {
                                var newIndex = m_ExternalObjects.arraySize++;
                                var pair = m_ExternalObjects.GetArrayElementAtIndex(newIndex);
                                pair.FindPropertyRelative("first.name").stringValue = name;
                                pair.FindPropertyRelative("first.type").stringValue = type;
                                pair.FindPropertyRelative("first.assembly").stringValue = assembly;
                                pair.FindPropertyRelative("second").objectReferenceValue = material;
                            }
                        }
                    }
                }
            }
        }

        private string[] GetAssetPaths() {
            var targets = this.targets;
            string[] strArray = new string[targets.Length];
            for (int index = 0; index < targets.Length; ++index) {
                AssetImporter assetImporter = targets[index] as AssetImporter;
                strArray[index] = assetImporter.assetPath;
            }
            return strArray;
        }

        void AddObjectProperty<T>(SerializedProperty property, string text, string tooltip) where T : UnityEngine.Object {
            var orgValue = property.objectReferenceValue as T;
            var newValue = EditorGUILayout.ObjectField(new GUIContent(text, tooltip), orgValue, typeof(T), false) as T;
            property.objectReferenceValue = newValue;
        }

        private void AddBoolProperty(SerializedProperty porperty, string text, string tooltip) {
            var orgValue = porperty.boolValue;
            var newValue = EditorGUILayout.Toggle(new GUIContent(text, tooltip), orgValue);
            porperty.boolValue = newValue;
        }

        void AddEnumProperty(SerializedProperty porperty, string text, string tooltip, Type typeOfEnum) {
            Rect ourRect = EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginProperty(ourRect, GUIContent.none, porperty);

            int selectionFromInspector = porperty.intValue;
            string[] enumNamesList = System.Enum.GetNames(typeOfEnum);
            var actualSelected = EditorGUILayout.Popup(text, selectionFromInspector, enumNamesList);
            porperty.intValue = actualSelected;
            EditorGUI.EndProperty();
            EditorGUILayout.EndHorizontal();
        }

        void AddIntProperty(SerializedProperty porperty, string text, string tooltip) {
            var orgValue = porperty.intValue;
            var newValue = EditorGUILayout.IntField(new GUIContent(text, tooltip), orgValue);
            porperty.intValue = newValue;
        }


        static class Styles {
            public static GUIContent ExternalMaterialMappings = EditorGUIUtility.TrTextContent("Remapped Materials", "External materials to use for each embedded material.");
        }
    }
}