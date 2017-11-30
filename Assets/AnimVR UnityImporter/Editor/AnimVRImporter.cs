using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.Playables;
using System.Collections.Generic;
using UnityEditor.Experimental.AssetImporters;
using System.Linq;
using System.IO;
using System;

/// TODO:
/// - Setup audio sources
/// - Camera FOV
/// 

#if UNITY_2017_3_OR_NEWER
namespace ANIMVR
{
    public enum AudioImportSetting
    {
        None,
        ClipsOnly,
        ClipsAndTracks
    }

    [Serializable]
    public struct AnimVRImporterSettings
    {
        public bool ApplyStageTransform;
        public DirectorWrapMode DefaultWrapMode;
        public AudioImportSetting AudioImport;
        public bool ImportCameras;
        public bool UnlitByDefault;
        public string Shader;
    }

    [ScriptedImporter(1, "stage")]
    public class AnimVRImporter : ScriptedImporter
    {
        public AnimVRImporterSettings Settings = new AnimVRImporterSettings()
        {
            ApplyStageTransform = false,
            DefaultWrapMode = DirectorWrapMode.None,
            AudioImport = AudioImportSetting.ClipsOnly,
            ImportCameras = true,
            UnlitByDefault = true,
            Shader = "AnimVR/Standard"
        };

        public bool needsAudioReimport;

        public Texture2D PreviewTexture;
        public string InfoString;

        Transform rootTransform, stageTransform;

        [NonSerialized]
        StageData stage;

        PlayableDirector director;
        Animator animator;

        Dictionary<AudioDataPool.AudioPoolKey, AudioClip> savedClips;

        Material materialToUse;

        int totalVertices;
        int totalLines;

        static Dictionary<AnimVR.LoopType, TimelineClip.ClipExtrapolation> LOOP_MAPPING = new Dictionary<AnimVR.LoopType, TimelineClip.ClipExtrapolation>()
        {
            {AnimVR.LoopType.Hold, TimelineClip.ClipExtrapolation.Hold },
            {AnimVR.LoopType.Loop, TimelineClip.ClipExtrapolation.Loop },
            {AnimVR.LoopType.OneShot, TimelineClip.ClipExtrapolation.None }
        };

        public override void OnImportAsset(AssetImportContext ctx)
        {
            stage = AnimData.LoadFromFile(ctx.assetPath);
            if(stage == null)
            {
                return;
            }
            
            MeshUtils.SimplifyStage(stage);

            savedClips = new Dictionary<AudioDataPool.AudioPoolKey, AudioClip>();
            totalVertices = 0;
            totalLines = 0;

            PreviewTexture = new Texture2D(1, 1);
            PreviewTexture.LoadImage(stage.previewFrames[0], false);
            PreviewTexture.Apply();

            if (Settings.Shader == null) Settings.Shader = "AnimVR/Standard";

            materialToUse = new Material(Shader.Find(Settings.Shader));
            materialToUse.SetFloat("_Unlit", Settings.UnlitByDefault ? 1 : 0);
            materialToUse.SetFloat("_Gamma", PlayerSettings.colorSpace == ColorSpace.Gamma ? 1.0f : 2.2f);
            materialToUse.name = "BaseMaterial";

            needsAudioReimport = false;

            var stageObj = GenerateUnityObject(stage, ctx);

            ctx.AddObjectToAsset("BaseMaterial", materialToUse);

            InfoString = "FPS: " + stage.fps + ", " + stage.timelineLength + " frames \n" 
                         + totalVertices + " verts, " + totalLines + " lines";

            savedClips = null;
            stage = null;
        }

        public GameObject GenerateUnityObject(StageData stage, AssetImportContext ctx)
        {
            var stageObj = new GameObject(stage.name);

            ctx.AddObjectToAsset(stage.name, stageObj, PreviewTexture);
            ctx.SetMainObject(stageObj);

            stageTransform = rootTransform = stageObj.transform;

            director = stageObj.AddComponent<PlayableDirector>();
            director.extrapolationMode = Settings.DefaultWrapMode;

            var timelineAsset = TimelineAsset.CreateInstance<TimelineAsset>();
            timelineAsset.name = stage.name + "_Timeline";
            timelineAsset.editorSettings.fps = stage.fps;
            timelineAsset.durationMode = TimelineAsset.DurationMode.FixedLength;
            timelineAsset.fixedDuration = stage.timelineLength  * 1.0 / stage.fps;

            ctx.AddObjectToAsset(timelineAsset.name, timelineAsset);
            director.playableAsset = timelineAsset;

            animator = stageObj.AddComponent<Animator>();

            foreach(var symbol in stage.Symbols)
            {
                var symbolObj = GenerateUnityObject(symbol, 0, ctx, timelineAsset, null, stageObj.transform);
                symbolObj.transform.SetParent(stageObj.transform, false);
                if(Settings.ApplyStageTransform)
                {
                    symbolObj.transform.localPosition += stage.transform.pos;
                    symbolObj.transform.localRotation *= stage.transform.rot;

                    var scl = symbolObj.transform.localScale;
                    var s = stage.transform.scl;
                    symbolObj.transform.localScale = new Vector3(scl.x * s.x, scl.y * s.y, scl.z * s.z);
                }
            }
            //hacky fix cause fixed duration from code is broken
            timelineAsset.fixedDuration = (stage.timelineLength -1) * 1.0 / stage.fps;

            
            
            return stageObj;
        }

        public GameObject GenerateUnityObject(PlayableData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            if (playable is SymbolData) return GenerateUnityObject(playable as SymbolData, frameOffset, ctx, parentTimeline, parentTrack, parent);
            else if (playable is TimeLineData) return GenerateUnityObject(playable as TimeLineData, frameOffset, ctx, parentTimeline, parentTrack, parent);
// No audio support on Mac
#if UNITY_EDITOR_WIN
            else if (playable is AudioData && Settings.AudioImport != AudioImportSetting.None) return GenerateUnityObject(playable as AudioData, frameOffset, ctx, parentTimeline, parentTrack, parent);
#endif
            else if (playable is CameraData && Settings.ImportCameras) return GenerateUnityObject(playable as CameraData, frameOffset, ctx, parentTimeline, parentTrack, parent);
            else if (playable is StaticMeshData) return GenerateUnityObject(playable as StaticMeshData, frameOffset, ctx, parentTimeline, parentTrack, parent);
            else return null;
        }

        public GameObject MakePlayableBaseObject(PlayableData playable, Transform parent)
        {
            var playableObj = new GameObject(playable.displayName ?? "Layer");
            playableObj.transform.parent = parent;
            playableObj.transform.localPosition = playable.transform.pos.V3;
            playableObj.transform.localRotation = playable.transform.rot.Q;
            playableObj.transform.localScale = playable.transform.scl.V3;

            playableObj.SetActive(playable.isVisible);

            return playableObj;
        }

        public GameObject GenerateUnityObject(SymbolData symbol, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            var symbolObj = MakePlayableBaseObject(symbol, parent);

            var path = AnimationUtility.CalculateTransformPath(symbolObj.transform, stageTransform);

            var prevDirector = director;
            var prevRootTransform = rootTransform;
            var prevAnimator = animator;


            // Top level symbol doesn't need to group stuff
            if (parent != stageTransform)
            {
                int minPlayableStart = symbol.Playables.Min(p => p.AbsoluteTimeOffset);
                int frameLength = symbol.Playables.Max(p => p.AbsoluteTimeOffset + p.GetFrameCount(stage.fps) - minPlayableStart);

                int frameStart = symbol.AbsoluteTimeOffset + minPlayableStart;

                director = symbolObj.AddComponent<PlayableDirector>();
                var timelineAsset = TimelineAsset.CreateInstance<TimelineAsset>();
                timelineAsset.name = path + "_Timeline";
                timelineAsset.editorSettings.fps = stage.fps;
                timelineAsset.durationMode = TimelineAsset.DurationMode.BasedOnClips;
                timelineAsset.fixedDuration = frameLength / stage.fps;

                ctx.AddObjectToAsset(timelineAsset.name, timelineAsset);
                director.playableAsset = timelineAsset;

                animator = symbolObj.AddComponent<Animator>();

                var controlTrack = parentTimeline.CreateTrack<AnimVR.Timeline.AnimControlTrack>(null, symbolObj.name);
                ctx.AddObjectToAsset(path + "_Control", controlTrack);

                var controlClip = controlTrack.CreateDefaultClip();
                controlClip.displayName = symbolObj.name;
                controlClip.start = frameStart / stage.fps;
                controlClip.duration = frameLength / stage.fps;

                typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(controlClip, LOOP_MAPPING[symbol.LoopIn], null);
                typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(controlClip, LOOP_MAPPING[symbol.LoopOut], null);

                var controlAsset = controlClip.asset as AnimVR.Timeline.AnimControlPlayableAsset;
                controlAsset.name = symbolObj.name;
                prevDirector.SetGenericBinding(controlAsset, symbolObj);

                ctx.AddObjectToAsset(path + "_ControlAsset", controlAsset);

                parentTimeline = timelineAsset;
                rootTransform = symbolObj.transform;

                frameOffset = -minPlayableStart;
            }

            foreach (var playbale in symbol.Playables)
            {
                if (playbale.isVisible)
                {
                    GenerateUnityObject(playbale, frameOffset, ctx, parentTimeline, null, symbolObj.transform);
                }
            }

            director = prevDirector;
            rootTransform = prevRootTransform;
            animator = prevAnimator;

            return symbolObj;
        }

        public GameObject GenerateUnityObject(TimeLineData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            var playableObj = MakePlayableBaseObject(playable, parent);
            var pathForName = AnimationUtility.CalculateTransformPath(playableObj.transform, stageTransform);

            // GROUP
            var groupTrack = parentTimeline.CreateTrack<GroupTrack>(parentTrack, playable.displayName);
            ctx.AddObjectToAsset(pathForName + "_GroupTrack", groupTrack);

            // ANIMATION
            var animationTrack = parentTimeline.CreateTrack<AnimVRTrack>(groupTrack, pathForName + "_animation");
            ctx.AddObjectToAsset(pathForName + "_animation", animationTrack);

            var animationClip = animationTrack.CreateDefaultClip();
            animationClip.duration = (playable.GetFrameCount(stage.fps) -1) / stage.fps;
            animationClip.start = (playable.AbsoluteTimeOffset + frameOffset) / stage.fps;

            typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(animationClip, LOOP_MAPPING[playable.LoopIn], null);
            typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(animationClip, LOOP_MAPPING[playable.LoopOut], null);

            var animAsset = animationClip.asset as AnimVRFramesAsset;
            animAsset.FPS = stage.fps;

            director.SetGenericBinding(animAsset, playableObj);
            ctx.AddObjectToAsset(pathForName + "_activeAsset", animAsset);

            // ACTIVATION
            var frameTrack = parentTimeline.CreateTrack<ActivationTrack>(groupTrack, pathForName + "_track");
            ctx.AddObjectToAsset(pathForName + "_track", frameTrack);
            director.SetGenericBinding(frameTrack, playableObj);

            var frameClip = frameTrack.CreateDefaultClip();
            frameClip.start = playable.LoopIn != AnimVR.LoopType.OneShot ? 0 : animationClip.start;
            frameClip.duration = playable.LoopOut != AnimVR.LoopType.OneShot ? 
                parentTimeline.fixedDuration - frameClip.start : 
                (animationClip.start - frameClip.start) + animationClip.duration;

            ctx.AddObjectToAsset(pathForName + "_activeAsset", frameClip.asset);

            int frameIndex = -1;
            foreach(var frame in playable.Frames)
            {
                if (!frame.isInstance)
                {
                    var frameObj = GenerateUnityObject(frame, ctx, parentTimeline, playableObj.transform, ++frameIndex);
                    if (frameIndex != 0) frameObj.SetActive(false);
                    frameObj.transform.SetAsLastSibling();
                }
                animAsset.FrameIndices.Add(frameIndex);
            }

            return playableObj;
        }

        public GameObject GenerateUnityObject(AudioData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            AudioClip clip = null;

            var dir = Application.dataPath + Path.GetDirectoryName(ctx.assetPath).Substring(6);
            var clipPath = dir + "/" + Path.GetFileNameWithoutExtension(ctx.assetPath) + "_audio/" + playable.displayName + "_audio.wav";

            if (savedClips.ContainsKey(playable.audioDataKey))
            {
                clip = savedClips[playable.audioDataKey];
            }
            else
            {
                var assetPath = clipPath.Replace(Application.dataPath, "Assets");

                if (!File.Exists(clipPath))
                {
                    clip = stage.AudioDataPool.RetrieveClipFromPool(playable.audioDataKey);
                    if (clip)
                    {
                        clip.name = playable.displayName + "_audio";
                        SavWav.Save(clipPath, clip);
                        AssetDatabase.ImportAsset(assetPath);
                        if (Settings.AudioImport != AudioImportSetting.ClipsAndTracks) needsAudioReimport = true;
                    }
                }

                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                savedClips[playable.audioDataKey] = clip;
            }

            if (Settings.AudioImport != AudioImportSetting.ClipsAndTracks) return null;

            var playableObj = MakePlayableBaseObject(playable, parent);
            var audioSource = playableObj.AddComponent<AudioSource>();
            audioSource.spatialBlend = playable.Spatialize ? 0 : 1;

            var pathForName = AnimationUtility.CalculateTransformPath(playableObj.transform, stageTransform);

            var groupTrack = parentTimeline.CreateTrack<GroupTrack>(parentTrack, playable.displayName);
            ctx.AddObjectToAsset(pathForName + "_GroupTrack", groupTrack);

            var track = parentTimeline.CreateTrack<AudioTrack>(groupTrack, playable.displayName);
            ctx.AddObjectToAsset(pathForName + "_audioTrack", track);

            bool loop = playable.LoopType == AnimVR.LoopType.Loop;

            var audioTrackClip = track.CreateDefaultClip();
            audioTrackClip.displayName = playable.displayName;
            (audioTrackClip.asset as AudioPlayableAsset).clip = clip;
            
            typeof(AudioPlayableAsset).GetField("m_Loop", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).
                SetValue(audioTrackClip.asset, loop);

            float start = (playable.AbsoluteTimeOffset + frameOffset) / stage.fps;
            float duration = clip ? clip.length : 1;

            if(loop)
            {
                audioTrackClip.start = 0;
                audioTrackClip.duration = parentTimeline.fixedDuration;
                audioTrackClip.clipIn = duration - start % duration;
            }
            else
            {
                audioTrackClip.start = start;
                audioTrackClip.duration = duration;
            }

            ctx.AddObjectToAsset(pathForName + "_asset", audioTrackClip.asset);

            director.SetGenericBinding(track, audioSource);

            return playableObj;
        }

        public AnimationClip MakeAnimationClip(List<SerializableTransform> frames, List<float> times, string path)
        {
            var xCurve = new AnimationCurve();
            var yCurve = new AnimationCurve();
            var zCurve = new AnimationCurve();

            var rotXCurve = new AnimationCurve();
            var rotYCurve = new AnimationCurve();
            var rotZCurve = new AnimationCurve();
            var rotWCurve = new AnimationCurve();

            var scaleXCurve = new AnimationCurve();
            var scaleYCurve = new AnimationCurve();
            var scaleZCurve = new AnimationCurve();

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var time = times != null ? times[i] : (i / stage.fps);


                xCurve.AddKey(new Keyframe(time, frame.pos.x));//, float.NegativeInfinity, float.PositiveInfinity));
                yCurve.AddKey(new Keyframe(time, frame.pos.y));//, float.NegativeInfinity, float.PositiveInfinity));
                zCurve.AddKey(new Keyframe(time, frame.pos.z));//, float.NegativeInfinity, float.PositiveInfinity));

                rotXCurve.AddKey(new Keyframe(time, frame.rot.x));//, float.NegativeInfinity, float.PositiveInfinity));
                rotYCurve.AddKey(new Keyframe(time, frame.rot.y));//, float.NegativeInfinity, float.PositiveInfinity));
                rotZCurve.AddKey(new Keyframe(time, frame.rot.z));//, float.NegativeInfinity, float.PositiveInfinity));
                rotWCurve.AddKey(new Keyframe(time, frame.rot.w));//, float.NegativeInfinity, float.PositiveInfinity));

                scaleXCurve.AddKey(new Keyframe(time, frame.scl.x));//, float.NegativeInfinity, float.PositiveInfinity));
                scaleYCurve.AddKey(new Keyframe(time, frame.scl.y));//, float.NegativeInfinity, float.PositiveInfinity));
                scaleZCurve.AddKey(new Keyframe(time, frame.scl.z));//, float.NegativeInfinity, float.PositiveInfinity));

                AnimationUtility.SetKeyBroken(xCurve, i, true);
                AnimationUtility.SetKeyBroken(yCurve, i, true);
                AnimationUtility.SetKeyBroken(zCurve, i, true);

                AnimationUtility.SetKeyBroken(scaleXCurve, i, true);
                AnimationUtility.SetKeyBroken(scaleYCurve, i, true);
                AnimationUtility.SetKeyBroken(scaleZCurve, i, true);

                AnimationUtility.SetKeyBroken(rotXCurve, i, true);
                AnimationUtility.SetKeyBroken(rotYCurve, i, true);
                AnimationUtility.SetKeyBroken(rotZCurve, i, true);
                AnimationUtility.SetKeyBroken(rotWCurve, i, true);

                AnimationUtility.SetKeyLeftTangentMode(xCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(yCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(zCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyLeftTangentMode(rotXCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(rotYCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(rotZCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(rotWCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyLeftTangentMode(scaleXCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(scaleYCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(scaleZCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyRightTangentMode(xCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(yCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(zCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyRightTangentMode(rotXCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(rotYCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(rotZCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(rotWCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyRightTangentMode(scaleXCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(scaleYCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(scaleZCurve, i, AnimationUtility.TangentMode.Constant);
            }

            var animationClip = new AnimationClip();
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localPosition.x", xCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localPosition.y", yCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localPosition.z", zCurve);


            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localRotation.x", rotXCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localRotation.y", rotYCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localRotation.z", rotZCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localRotation.w", rotWCurve);

            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localScale.x", scaleXCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localScale.y", scaleYCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localScale.z", scaleZCurve);

            animationClip.frameRate = stage.fps;

            return animationClip;
        }

        public AnimationClip MakeAnimationClip(TransformTimelineData timeline, string path)
        {
            return MakeAnimationClip(timeline.Frames, timeline.FrameTimes, path);
        }

        public GameObject GenerateUnityObject(CameraData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            var playableObj = MakePlayableBaseObject(playable, parent);

            var transformAnchor = new GameObject("TransformAnchor");

            playable.CurrentShotOffset.ApplyTo(transformAnchor.transform);
            transformAnchor.transform.SetParent(playableObj.transform, false);

            var cam = transformAnchor.AddComponent<Camera>();
            cam.backgroundColor = stage.backgroundColor;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // TODO: Field of view 
            cam.stereoTargetEye = StereoTargetEyeMask.None;

            var pathForName = AnimationUtility.CalculateTransformPath(transformAnchor.transform, stageTransform);

            var groupTrack = parentTimeline.CreateTrack<GroupTrack>(parentTrack, playable.displayName);
            ctx.AddObjectToAsset(pathForName + "_GroupTrack", groupTrack);

            if (playable.Timeline.Frames.Count > 0)
            {
                var animTrack = parentTimeline.CreateTrack<AnimationTrack>(groupTrack, pathForName + "_TransformTrack");

                director.SetGenericBinding(animTrack, animator);

                ctx.AddObjectToAsset(pathForName + "_TransformTrack", animTrack);

                var animationClip = MakeAnimationClip(playable.Timeline, AnimationUtility.CalculateTransformPath(transformAnchor.transform, rootTransform));

                ctx.AddObjectToAsset(pathForName + "_animation", animationClip);

                var timelineClip = animTrack.CreateClip(animationClip);
                timelineClip.start = (playable.AbsoluteTimeOffset + frameOffset) / stage.fps;
                timelineClip.displayName = playable.displayName;
                ctx.AddObjectToAsset(pathForName + "_asset", timelineClip.asset);
            }

            return playableObj;
        }

        public GameObject GenerateUnityObject(StaticMeshData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            var playableObj = MakePlayableBaseObject(playable, parent);

            var transformAnchor = new GameObject("TransformAnchor");
            transformAnchor.transform.SetParent(playableObj.transform, false);
            var pathForName = AnimationUtility.CalculateTransformPath(transformAnchor.transform, stageTransform);

            List<Material> materials = new List<Material>();

            int matIndex = 0;
            foreach(var matData in playable.Materials)
            {
                var mat = MeshUtils.MaterialFromData(matData, materialToUse);
                mat.name = pathForName + "_material" + (matIndex++).ToString();
                ctx.AddObjectToAsset(mat.name, mat);

                if (mat.mainTexture)
                {
                    ctx.AddObjectToAsset(mat.name + "_diffuse", mat.mainTexture);
                }
                materials.Add(mat);
            }

            int partIndex = 0;
            foreach (var part in playable.Frames)
            {
                var partObj = new GameObject("MeshPart");
                var mf = partObj.AddComponent<MeshFilter>();
                var mr = partObj.AddComponent<MeshRenderer>();

                partObj.transform.SetParent(transformAnchor.transform, false);
                part.Transform.ApplyTo(partObj.transform);

                mr.sharedMaterial = materials[part.MaterialIndex];

                mf.sharedMesh = MeshUtils.MeshFromData(part);
                mf.sharedMesh.name = pathForName + "_mesh" + (partIndex).ToString();
                ctx.AddObjectToAsset(mf.sharedMesh.name, mf.sharedMesh);

                totalVertices += mf.sharedMesh.vertexCount;
            }

            var groupTrack = parentTimeline.CreateTrack<GroupTrack>(parentTrack, playable.displayName);
            ctx.AddObjectToAsset(pathForName + "_GroupTrack", groupTrack);

            double clipStart = (playable.AbsoluteTimeOffset + frameOffset) / stage.fps;
            double clipDuration = 1.0 / stage.fps;

            if (playable.InstanceMap.Count > 1)
            {
                var animTrack = parentTimeline.CreateTrack<AnimationTrack>(groupTrack, pathForName + "_TransformTrack");

                director.SetGenericBinding(animTrack, animator);

                ctx.AddObjectToAsset(pathForName + "_TransformTrack", animTrack);

                var animationClip = MakeAnimationClip(playable.InstanceMap, null, AnimationUtility.CalculateTransformPath(transformAnchor.transform, rootTransform));
                animationClip.name = pathForName + "_animation";


                ctx.AddObjectToAsset(pathForName + "_animation", animationClip);

                var timelineClip = animTrack.CreateClip(animationClip);
                timelineClip.start = clipStart;
                timelineClip.displayName = playable.displayName;

                typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(timelineClip, LOOP_MAPPING[playable.LoopIn], null);
                typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(timelineClip, LOOP_MAPPING[playable.LoopOut], null);

                clipDuration = timelineClip.duration;

                ctx.AddObjectToAsset(pathForName + "_asset", timelineClip.asset);
            }
            else
            {
                playable.InstanceMap[0].ApplyTo(transformAnchor.transform);
            }

            var activeTrack = parentTimeline.CreateTrack<ActivationTrack>(groupTrack, pathForName + "_Activation");
            ctx.AddObjectToAsset(pathForName + "_Activation", activeTrack);

            director.SetGenericBinding(activeTrack, playableObj);

            var clip = activeTrack.CreateDefaultClip();
            clip.start = playable.LoopIn != AnimVR.LoopType.OneShot ? 0 : clipStart;
            clip.duration = playable.LoopOut != AnimVR.LoopType.OneShot ? parentTimeline.fixedDuration - clip.start : (clipStart - clip.start) + clipDuration;

            ctx.AddObjectToAsset(pathForName + "_activeAsset", clip.asset);

            return playableObj;
        }

        public GameObject GenerateUnityObject(FrameData frame, AssetImportContext ctx, TimelineAsset parentTimeline, Transform parent, int index)
        {
            var playableObj = new GameObject(index.ToString());
            playableObj.transform.parent = parent;
            playableObj.transform.localPosition = frame.transform.pos.V3;
            playableObj.transform.localRotation = frame.transform.rot.Q;
            playableObj.transform.localScale = frame.transform.scl.V3;

            var pathForName = AnimationUtility.CalculateTransformPath(playableObj.transform, stageTransform);


            List<List<CombineInstance>> instances = new List<List<CombineInstance>>();
            List<CombineInstance> currentList = new List<CombineInstance>();
            instances.Add(currentList);
            int vCount = 0;
            foreach(var line in frame.Lines)
            {
                try
                {
                    List<Vector3> verts = new List<Vector3>();
                    List<int> indices = new List<int>();
                    List<Vector4> colors = new List<Vector4>();

                    MeshUtils.GeneratePositionData(line, verts, indices, colors);

                    CombineInstance instance = new CombineInstance();

                    if (verts.Count == 0) continue;

                    Mesh mesh = new Mesh();
                    mesh.SetVertices(verts);
                    mesh.SetTriangles(indices, 0);
                    mesh.SetColors(colors.Select(c => new Color(c.x, c.y, c.z, c.w)).ToList());
                    instance.mesh = mesh;

                    vCount += verts.Count;

                    if (vCount > 60000)
                    {
                        currentList = new List<CombineInstance>();
                        instances.Add(currentList);
                        vCount -= 60000;
                    }

                    currentList.Add(instance);
                    totalLines++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e.Message);
                }
            }

            totalVertices += vCount;

            int meshId = 0;
            foreach (var mesh in instances)
            {
                var subObj = new GameObject("Submesh" + index);
                subObj.transform.SetParent(playableObj.transform, false);

                var mf = subObj.AddComponent<MeshFilter>();
                var mr = subObj.AddComponent<MeshRenderer>();
                Mesh combinedMesh = new Mesh();

                combinedMesh.CombineMeshes(mesh.ToArray(), true, false, false);
                combinedMesh.name = pathForName + meshId;

                mf.sharedMesh = combinedMesh;
                mr.sharedMaterial = materialToUse;

                ctx.AddObjectToAsset(pathForName + "_mesh" + index, mf.sharedMesh);
                meshId++;
            }


            return playableObj;
        }
    }
}
#elif UNITY_2017_1_OR_NEWER
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.Playables;
using UnityEditor.Experimental.AssetImporters;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;

/// TODO:
/// - Setup audio sources
/// - Camera FOV

namespace ANIMVR
{
    public enum AudioImportSetting
    {
        None,
        ClipsOnly,
        ClipsAndTracks
    }

    [Serializable]
    public struct AnimVRImporterSettings
    {
        public bool ApplyStageTransform;
        public DirectorWrapMode DefaultWrapMode;
        public AudioImportSetting AudioImport;
        public bool ImportCameras;
        public bool UnlitByDefault;
        public string Shader;
    }

    [ScriptedImporter(1, "stage")]
    public class AnimVRImporter : ScriptedImporter
    {
        public AnimVRImporterSettings Settings = new AnimVRImporterSettings()
        {
            ApplyStageTransform = false,
            DefaultWrapMode = DirectorWrapMode.None,
            AudioImport = AudioImportSetting.ClipsOnly,
            ImportCameras = true,
            UnlitByDefault = true,
            Shader = "AnimVR/Standard"
        };

        public bool needsAudioReimport;

        public Texture2D PreviewTexture;
        public string InfoString;

        Transform rootTransform, stageTransform;

        [NonSerialized]
        StageData stage;

        PlayableDirector director;
        Animator animator;

        Dictionary<AudioDataPool.AudioPoolKey, AudioClip> savedClips;

        Material materialToUse;

        int totalVertices;
        int totalLines;

        static Dictionary<AnimVR.LoopType, TimelineClip.ClipExtrapolation> LOOP_MAPPING = new Dictionary<AnimVR.LoopType, TimelineClip.ClipExtrapolation>()
        {
            {AnimVR.LoopType.Hold, TimelineClip.ClipExtrapolation.Hold },
            {AnimVR.LoopType.Loop, TimelineClip.ClipExtrapolation.Loop },
            {AnimVR.LoopType.OneShot, TimelineClip.ClipExtrapolation.None }
        };

        public override void OnImportAsset(AssetImportContext ctx)
        {
            stage = AnimData.LoadFromFile(ctx.assetPath);
            if(stage == null)
            {
                return;
            }
            
            MeshUtils.SimplifyStage(stage);

            savedClips = new Dictionary<AudioDataPool.AudioPoolKey, AudioClip>();
            totalVertices = 0;
            totalLines = 0;

            PreviewTexture = new Texture2D(1, 1);
            PreviewTexture.LoadImage(stage.previewFrames[0], false);
            PreviewTexture.Apply();

            if (Settings.Shader == null) Settings.Shader = "AnimVR/Standard";

            materialToUse = new Material(Shader.Find(Settings.Shader));
            materialToUse.SetFloat("_Unlit", Settings.UnlitByDefault ? 1 : 0);
            materialToUse.SetFloat("_Gamma", PlayerSettings.colorSpace == ColorSpace.Gamma ? 1.0f : 2.2f);
            materialToUse.name = "BaseMaterial";

            ctx.AddSubAsset("BaseMaterial", materialToUse);

            needsAudioReimport = false;

            var stageObj = GenerateUnityObject(stage, ctx);

            ctx.SetMainAsset(stage.name, stageObj, PreviewTexture);

            InfoString = "FPS: " + stage.fps + ", " + stage.timelineLength + " frames \n" 
                         + totalVertices + " verts, " + totalLines + " lines";

            savedClips = null;
            stage = null;
        }

        public GameObject GenerateUnityObject(StageData stage, AssetImportContext ctx)
        {
            var stageObj = new GameObject(stage.name);

            stageTransform = rootTransform = stageObj.transform;

            director = stageObj.AddComponent<PlayableDirector>();
            director.extrapolationMode = Settings.DefaultWrapMode;

            var timelineAsset = TimelineAsset.CreateInstance<TimelineAsset>();
            timelineAsset.name = stage.name + "_Timeline";
            timelineAsset.editorSettings.fps = stage.fps;
            timelineAsset.durationMode = TimelineAsset.DurationMode.FixedLength;
            timelineAsset.fixedDuration = stage.timelineLength * 1.0 / stage.fps;

            ctx.AddSubAsset(timelineAsset.name, timelineAsset);
            director.playableAsset = timelineAsset;

            animator = stageObj.AddComponent<Animator>();

            foreach(var symbol in stage.Symbols)
            {
                var symbolObj = GenerateUnityObject(symbol, 0, ctx, timelineAsset, null, stageObj.transform);
                symbolObj.transform.SetParent(stageObj.transform, false);
                if(Settings.ApplyStageTransform)
                {
                    symbolObj.transform.localPosition += stage.transform.pos;
                    symbolObj.transform.localRotation *= stage.transform.rot;

                    var scl = symbolObj.transform.localScale;
                    var s = stage.transform.scl;
                    symbolObj.transform.localScale = new Vector3(scl.x * s.x, scl.y * s.y, scl.z * s.z);
                }
            }

            return stageObj;
        }

        public GameObject GenerateUnityObject(PlayableData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            if (playable is SymbolData) return GenerateUnityObject(playable as SymbolData, frameOffset, ctx, parentTimeline, parentTrack, parent);
            else if (playable is TimeLineData) return GenerateUnityObject(playable as TimeLineData, frameOffset, ctx, parentTimeline, parentTrack, parent);
// No audio support on Mac
#if UNITY_EDITOR_WIN
            else if (playable is AudioData && Settings.AudioImport != AudioImportSetting.None) return GenerateUnityObject(playable as AudioData, frameOffset, ctx, parentTimeline, parentTrack, parent);
#endif
            else if (playable is CameraData && Settings.ImportCameras) return GenerateUnityObject(playable as CameraData, frameOffset, ctx, parentTimeline, parentTrack, parent);
            else if (playable is StaticMeshData) return GenerateUnityObject(playable as StaticMeshData, frameOffset, ctx, parentTimeline, parentTrack, parent);
            // Todo: Support Unity Import Data
            else return null;
        }

        public GameObject MakePlayableBaseObject(PlayableData playable, Transform parent)
        {
            var playableObj = new GameObject(playable.displayName ?? "Layer");
            playableObj.transform.parent = parent;
            playableObj.transform.localPosition = playable.transform.pos.V3;
            playableObj.transform.localRotation = playable.transform.rot.Q;
            playableObj.transform.localScale = playable.transform.scl.V3;

            playableObj.SetActive(playable.isVisible);

            return playableObj;
        }

        public GameObject GenerateUnityObject(SymbolData symbol, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            var symbolObj = MakePlayableBaseObject(symbol, parent);

            var path = AnimationUtility.CalculateTransformPath(symbolObj.transform, stageTransform);

            var prevDirector = director;
            var prevRootTransform = rootTransform;
            var prevAnimator = animator;


            // Top level symbol doesn't need to group stuff
            if (parent != stageTransform)
            {
                int minPlayableStart = symbol.Playables.Min(p => p.AbsoluteTimeOffset);
                int frameLength = symbol.Playables.Max(p => p.AbsoluteTimeOffset + p.GetFrameCount(stage.fps) - minPlayableStart);

                int frameStart = symbol.AbsoluteTimeOffset + minPlayableStart;

                director = symbolObj.AddComponent<PlayableDirector>();
                var timelineAsset = TimelineAsset.CreateInstance<TimelineAsset>();
                timelineAsset.name = path + "_Timeline";
                timelineAsset.editorSettings.fps = stage.fps;
                timelineAsset.durationMode = TimelineAsset.DurationMode.BasedOnClips;
                timelineAsset.fixedDuration = frameLength / stage.fps;

                ctx.AddSubAsset(timelineAsset.name, timelineAsset);
                director.playableAsset = timelineAsset;

                animator = symbolObj.AddComponent<Animator>();

                var controlTrack = parentTimeline.CreateTrack<AnimVR.Timeline.AnimControlTrack>(null, symbolObj.name);
                ctx.AddSubAsset(path + "_Control", controlTrack);

                var controlClip = controlTrack.CreateDefaultClip();
                controlClip.displayName = symbolObj.name;
                controlClip.start = frameStart / stage.fps;
                controlClip.duration = frameLength / stage.fps;

                typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(controlClip, LOOP_MAPPING[symbol.LoopIn], null);
                typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(controlClip, LOOP_MAPPING[symbol.LoopOut], null);

                var controlAsset = controlClip.asset as AnimVR.Timeline.AnimControlPlayableAsset;
                controlAsset.name = symbolObj.name;
                prevDirector.SetGenericBinding(controlAsset, symbolObj);

                ctx.AddSubAsset(path + "_ControlAsset", controlAsset);

                parentTimeline = timelineAsset;
                rootTransform = symbolObj.transform;

                frameOffset = -minPlayableStart;
            }

            foreach (var playbale in symbol.Playables)
            {
                if (playbale.isVisible)
                {
                    GenerateUnityObject(playbale, frameOffset, ctx, parentTimeline, null, symbolObj.transform);
                }
            }

            director = prevDirector;
            rootTransform = prevRootTransform;
            animator = prevAnimator;

            return symbolObj;
        }

        public GameObject GenerateUnityObject(TimeLineData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            var playableObj = MakePlayableBaseObject(playable, parent);
            var pathForName = AnimationUtility.CalculateTransformPath(playableObj.transform, stageTransform);

            // GROUP
            var groupTrack = parentTimeline.CreateTrack<GroupTrack>(parentTrack, playable.displayName);
            ctx.AddSubAsset(pathForName + "_GroupTrack", groupTrack);

            // ANIMATION
            var animationTrack = parentTimeline.CreateTrack<AnimVRTrack>(groupTrack, pathForName + "_animation");
            ctx.AddSubAsset(pathForName + "_animation", animationTrack);

            var animationClip = animationTrack.CreateDefaultClip();
            animationClip.duration = playable.GetFrameCount(stage.fps) / stage.fps;
            animationClip.start = (playable.AbsoluteTimeOffset + frameOffset) / stage.fps;

            typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(animationClip, LOOP_MAPPING[playable.LoopIn], null);
            typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(animationClip, LOOP_MAPPING[playable.LoopOut], null);

            var animAsset = animationClip.asset as AnimVRFramesAsset;
            animAsset.FPS = stage.fps;

            director.SetGenericBinding(animAsset, playableObj);
            ctx.AddSubAsset(pathForName + "_activeAsset", animAsset);

            // ACTIVATION
            var frameTrack = parentTimeline.CreateTrack<ActivationTrack>(groupTrack, pathForName + "_track");
            ctx.AddSubAsset(pathForName + "_track", frameTrack);
            director.SetGenericBinding(frameTrack, playableObj);

            var frameClip = frameTrack.CreateDefaultClip();
            frameClip.start = playable.LoopIn != AnimVR.LoopType.OneShot ? 0 : animationClip.start;
            frameClip.duration = playable.LoopOut != AnimVR.LoopType.OneShot ? 
                parentTimeline.fixedDuration - frameClip.start : 
                (animationClip.start - frameClip.start) + animationClip.duration;

            ctx.AddSubAsset(pathForName + "_activeAsset", frameClip.asset);

            int frameIndex = -1;
            foreach(var frame in playable.Frames)
            {
                if (!frame.isInstance)
                {
                    var frameObj = GenerateUnityObject(frame, ctx, parentTimeline, playableObj.transform, ++frameIndex);
                    if (frameIndex != 0) frameObj.SetActive(false);
                    frameObj.transform.SetAsLastSibling();
                }
                animAsset.FrameIndices.Add(frameIndex);
            }

            return playableObj;
        }

        public GameObject GenerateUnityObject(AudioData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            AudioClip clip = null;

            var dir = Application.dataPath + Path.GetDirectoryName(ctx.assetPath).Substring(6);
            var clipPath = dir + "/" + Path.GetFileNameWithoutExtension(ctx.assetPath) + "_audio/" + playable.displayName + "_audio.wav";

            if (savedClips.ContainsKey(playable.audioDataKey))
            {
                clip = savedClips[playable.audioDataKey];
            }
            else
            {
                var assetPath = clipPath.Replace(Application.dataPath, "Assets");

                if (!File.Exists(clipPath))
                {
                    clip = stage.AudioDataPool.RetrieveClipFromPool(playable.audioDataKey);
                    if (clip)
                    {
                        clip.name = playable.displayName + "_audio";
                        SavWav.Save(clipPath, clip);
                        AssetDatabase.ImportAsset(assetPath);
                        if (Settings.AudioImport != AudioImportSetting.ClipsAndTracks) needsAudioReimport = true;
                    }
                }

                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                savedClips[playable.audioDataKey] = clip;
            }

            if (Settings.AudioImport != AudioImportSetting.ClipsAndTracks) return null;

            var playableObj = MakePlayableBaseObject(playable, parent);
            var audioSource = playableObj.AddComponent<AudioSource>();
            audioSource.spatialBlend = playable.Spatialize ? 0 : 1;

            var pathForName = AnimationUtility.CalculateTransformPath(playableObj.transform, stageTransform);

            var groupTrack = parentTimeline.CreateTrack<GroupTrack>(parentTrack, playable.displayName);
            ctx.AddSubAsset(pathForName + "_GroupTrack", groupTrack);

            var track = parentTimeline.CreateTrack<AudioTrack>(groupTrack, playable.displayName);
            ctx.AddSubAsset(pathForName + "_audioTrack", track);

            bool loop = playable.LoopType == AnimVR.LoopType.Loop;

            var audioTrackClip = track.CreateDefaultClip();
            audioTrackClip.displayName = playable.displayName;
            (audioTrackClip.asset as AudioPlayableAsset).clip = clip;
            
            typeof(AudioPlayableAsset).GetField("m_Loop", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).
                SetValue(audioTrackClip.asset, loop);

            float start = (playable.AbsoluteTimeOffset + frameOffset) / stage.fps;
            float duration = clip ? clip.length : 1;

            if(loop)
            {
                audioTrackClip.start = 0;
                audioTrackClip.duration = parentTimeline.fixedDuration;
                audioTrackClip.clipIn = duration - start % duration;
            }
            else
            {
                audioTrackClip.start = start;
                audioTrackClip.duration = duration;
            }

            ctx.AddSubAsset(pathForName + "_asset", audioTrackClip.asset);

            director.SetGenericBinding(track, audioSource);

            return playableObj;
        }

        public AnimationClip MakeAnimationClip(List<SerializableTransform> frames, List<float> times, string path)
        {
            var xCurve = new AnimationCurve();
            var yCurve = new AnimationCurve();
            var zCurve = new AnimationCurve();

            var rotXCurve = new AnimationCurve();
            var rotYCurve = new AnimationCurve();
            var rotZCurve = new AnimationCurve();
            var rotWCurve = new AnimationCurve();

            var scaleXCurve = new AnimationCurve();
            var scaleYCurve = new AnimationCurve();
            var scaleZCurve = new AnimationCurve();

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var time = times != null ? times[i] : (i / stage.fps);


                xCurve.AddKey(new Keyframe(time, frame.pos.x));//, float.NegativeInfinity, float.PositiveInfinity));
                yCurve.AddKey(new Keyframe(time, frame.pos.y));//, float.NegativeInfinity, float.PositiveInfinity));
                zCurve.AddKey(new Keyframe(time, frame.pos.z));//, float.NegativeInfinity, float.PositiveInfinity));

                rotXCurve.AddKey(new Keyframe(time, frame.rot.x));//, float.NegativeInfinity, float.PositiveInfinity));
                rotYCurve.AddKey(new Keyframe(time, frame.rot.y));//, float.NegativeInfinity, float.PositiveInfinity));
                rotZCurve.AddKey(new Keyframe(time, frame.rot.z));//, float.NegativeInfinity, float.PositiveInfinity));
                rotWCurve.AddKey(new Keyframe(time, frame.rot.w));//, float.NegativeInfinity, float.PositiveInfinity));

                scaleXCurve.AddKey(new Keyframe(time, frame.scl.x));//, float.NegativeInfinity, float.PositiveInfinity));
                scaleYCurve.AddKey(new Keyframe(time, frame.scl.y));//, float.NegativeInfinity, float.PositiveInfinity));
                scaleZCurve.AddKey(new Keyframe(time, frame.scl.z));//, float.NegativeInfinity, float.PositiveInfinity));

                AnimationUtility.SetKeyBroken(xCurve, i, true);
                AnimationUtility.SetKeyBroken(yCurve, i, true);
                AnimationUtility.SetKeyBroken(zCurve, i, true);

                AnimationUtility.SetKeyBroken(scaleXCurve, i, true);
                AnimationUtility.SetKeyBroken(scaleYCurve, i, true);
                AnimationUtility.SetKeyBroken(scaleZCurve, i, true);

                AnimationUtility.SetKeyBroken(rotXCurve, i, true);
                AnimationUtility.SetKeyBroken(rotYCurve, i, true);
                AnimationUtility.SetKeyBroken(rotZCurve, i, true);
                AnimationUtility.SetKeyBroken(rotWCurve, i, true);

                AnimationUtility.SetKeyLeftTangentMode(xCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(yCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(zCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyLeftTangentMode(rotXCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(rotYCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(rotZCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(rotWCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyLeftTangentMode(scaleXCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(scaleYCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyLeftTangentMode(scaleZCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyRightTangentMode(xCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(yCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(zCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyRightTangentMode(rotXCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(rotYCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(rotZCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(rotWCurve, i, AnimationUtility.TangentMode.Constant);

                AnimationUtility.SetKeyRightTangentMode(scaleXCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(scaleYCurve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(scaleZCurve, i, AnimationUtility.TangentMode.Constant);
            }

            var animationClip = new AnimationClip();
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localPosition.x", xCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localPosition.y", yCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localPosition.z", zCurve);


            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localRotation.x", rotXCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localRotation.y", rotYCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localRotation.z", rotZCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localRotation.w", rotWCurve);

            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localScale.x", scaleXCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localScale.y", scaleYCurve);
            AnimationUtility.SetEditorCurve(animationClip, path, typeof(Transform), "localScale.z", scaleZCurve);

            animationClip.frameRate = stage.fps;

            return animationClip;
        }

        public AnimationClip MakeAnimationClip(TransformTimelineData timeline, string path)
        {
            return MakeAnimationClip(timeline.Frames, timeline.FrameTimes, path);
        }

        public GameObject GenerateUnityObject(CameraData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            var playableObj = MakePlayableBaseObject(playable, parent);

            var transformAnchor = new GameObject("TransformAnchor");

            playable.CurrentShotOffset.ApplyTo(transformAnchor.transform);
            transformAnchor.transform.SetParent(playableObj.transform, false);

            var cam = transformAnchor.AddComponent<Camera>();
            cam.backgroundColor = stage.backgroundColor;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // TODO: Field of view 
            cam.stereoTargetEye = StereoTargetEyeMask.None;

            var pathForName = AnimationUtility.CalculateTransformPath(transformAnchor.transform, stageTransform);

            var groupTrack = parentTimeline.CreateTrack<GroupTrack>(parentTrack, playable.displayName);
            ctx.AddSubAsset(pathForName + "_GroupTrack", groupTrack);

            if (playable.Timeline.Frames.Count > 0)
            {
                var animTrack = parentTimeline.CreateTrack<AnimationTrack>(groupTrack, pathForName + "_TransformTrack");

                director.SetGenericBinding(animTrack, animator);

                ctx.AddSubAsset(pathForName + "_TransformTrack", animTrack);

                var animationClip = MakeAnimationClip(playable.Timeline, AnimationUtility.CalculateTransformPath(transformAnchor.transform, rootTransform));

                ctx.AddSubAsset(pathForName + "_animation", animationClip);

                var timelineClip = animTrack.CreateClip(animationClip);
                timelineClip.start = (playable.AbsoluteTimeOffset + frameOffset) / stage.fps;
                timelineClip.displayName = playable.displayName;
                ctx.AddSubAsset(pathForName + "_asset", timelineClip.asset);
            }

            return playableObj;
        }

        public GameObject GenerateUnityObject(StaticMeshData playable, int frameOffset, AssetImportContext ctx, TimelineAsset parentTimeline, TrackAsset parentTrack, Transform parent)
        {
            var playableObj = MakePlayableBaseObject(playable, parent);

            var transformAnchor = new GameObject("TransformAnchor");
            transformAnchor.transform.SetParent(playableObj.transform, false);
            var pathForName = AnimationUtility.CalculateTransformPath(transformAnchor.transform, stageTransform);

            List<Material> materials = new List<Material>();

            int matIndex = 0;
            foreach(var matData in playable.Materials)
            {
                var mat = MeshUtils.MaterialFromData(matData, materialToUse);
                mat.name = pathForName + "_material" + (matIndex++).ToString();
                ctx.AddSubAsset(mat.name, mat);

                if (mat.mainTexture)
                {
                    ctx.AddSubAsset(mat.name + "_diffuse", mat.mainTexture);
                }
                materials.Add(mat);
            }

            int partIndex = 0;
            foreach (var part in playable.Frames)
            {
                var partObj = new GameObject("MeshPart");
                var mf = partObj.AddComponent<MeshFilter>();
                var mr = partObj.AddComponent<MeshRenderer>();

                partObj.transform.SetParent(transformAnchor.transform, false);

                mr.sharedMaterial = materials[part.MaterialIndex];

                mf.sharedMesh = MeshUtils.MeshFromData(part);
                mf.sharedMesh.name = pathForName + "_mesh" + (partIndex).ToString();
                ctx.AddSubAsset(mf.sharedMesh.name, mf.sharedMesh);

                totalVertices += mf.sharedMesh.vertexCount;
            }

            var groupTrack = parentTimeline.CreateTrack<GroupTrack>(parentTrack, playable.displayName);
            ctx.AddSubAsset(pathForName + "_GroupTrack", groupTrack);

            double clipStart = (playable.AbsoluteTimeOffset + frameOffset) / stage.fps;
            double clipDuration = 1.0 / stage.fps;

            if (playable.InstanceMap.Count > 1)
            {
                var animTrack = parentTimeline.CreateTrack<AnimationTrack>(groupTrack, pathForName + "_TransformTrack");

                director.SetGenericBinding(animTrack, animator);

                ctx.AddSubAsset(pathForName + "_TransformTrack", animTrack);

                var animationClip = MakeAnimationClip(playable.InstanceMap, null, AnimationUtility.CalculateTransformPath(transformAnchor.transform, rootTransform));
                animationClip.name = pathForName + "_animation";


                ctx.AddSubAsset(pathForName + "_animation", animationClip);

                var timelineClip = animTrack.CreateClip(animationClip);
                timelineClip.start = clipStart;
                timelineClip.displayName = playable.displayName;

                typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(timelineClip, LOOP_MAPPING[playable.LoopIn], null);
                typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(timelineClip, LOOP_MAPPING[playable.LoopOut], null);

                clipDuration = timelineClip.duration;

                ctx.AddSubAsset(pathForName + "_asset", timelineClip.asset);
            }
            else
            {
                playable.InstanceMap[0].ApplyTo(transformAnchor.transform);
            }

            var activeTrack = parentTimeline.CreateTrack<ActivationTrack>(groupTrack, pathForName + "_Activation");
            ctx.AddSubAsset(pathForName + "_Activation", activeTrack);

            director.SetGenericBinding(activeTrack, playableObj);

            var clip = activeTrack.CreateDefaultClip();
            clip.start = playable.LoopIn != AnimVR.LoopType.OneShot ? 0 : clipStart;
            clip.duration = playable.LoopOut != AnimVR.LoopType.OneShot ? parentTimeline.fixedDuration - clip.start : (clipStart - clip.start) + clipDuration;

            ctx.AddSubAsset(pathForName + "_activeAsset", clip.asset);

            return playableObj;
        }

        public GameObject GenerateUnityObject(FrameData frame, AssetImportContext ctx, TimelineAsset parentTimeline, Transform parent, int index)
        {
            var playableObj = new GameObject(index.ToString());
            playableObj.transform.parent = parent;
            playableObj.transform.localPosition = frame.transform.pos.V3;
            playableObj.transform.localRotation = frame.transform.rot.Q;
            playableObj.transform.localScale = frame.transform.scl.V3;

            var pathForName = AnimationUtility.CalculateTransformPath(playableObj.transform, stageTransform);


            List<List<CombineInstance>> instances = new List<List<CombineInstance>>();
            List<CombineInstance> currentList = new List<CombineInstance>();
            instances.Add(currentList);
            int vCount = 0;
            foreach(var line in frame.Lines)
            {
                try
                {
                    List<Vector3> verts = new List<Vector3>();
                    List<int> indices = new List<int>();
                    List<Vector4> colors = new List<Vector4>();

                    MeshUtils.GeneratePositionData(line, verts, indices, colors);

                    CombineInstance instance = new CombineInstance();

                    if (verts.Count == 0) continue;

                    Mesh mesh = new Mesh();
                    mesh.SetVertices(verts);
                    mesh.SetTriangles(indices, 0);
                    mesh.SetColors(colors.Select(c => new Color(c.x, c.y, c.z, c.w)).ToList());
                    instance.mesh = mesh;

                    vCount += verts.Count;

                    if (vCount > 60000)
                    {
                        currentList = new List<CombineInstance>();
                        instances.Add(currentList);
                        vCount -= 60000;
                    }

                    currentList.Add(instance);
                    totalLines++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e.Message);
                }
            }

            totalVertices += vCount;

            int meshId = 0;
            foreach (var mesh in instances)
            {
                var subObj = new GameObject("Submesh" + index);
                subObj.transform.SetParent(playableObj.transform, false);

                var mf = subObj.AddComponent<MeshFilter>();
                var mr = subObj.AddComponent<MeshRenderer>();
                Mesh combinedMesh = new Mesh();

                combinedMesh.CombineMeshes(mesh.ToArray(), true, false, false);
                combinedMesh.name = pathForName + meshId;

                mf.sharedMesh = combinedMesh;
                mr.sharedMaterial = materialToUse;

                ctx.AddSubAsset(pathForName + "_mesh" + index, mf.sharedMesh);
                meshId++;
            }


            return playableObj;
        }
    }
}
#endif