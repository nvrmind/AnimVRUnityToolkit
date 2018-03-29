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
        public bool HasFades;
          

        [NonSerialized]
        StageData stage;

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

        static Dictionary<AnimVR.TrimLoopType, TimelineClip.ClipExtrapolation> TRIM_LOOP_MAPPING = new Dictionary<AnimVR.TrimLoopType, TimelineClip.ClipExtrapolation>()
        {
            {AnimVR.TrimLoopType.Hold, TimelineClip.ClipExtrapolation.Hold },
            {AnimVR.TrimLoopType.Loop, TimelineClip.ClipExtrapolation.Loop },
            {AnimVR.TrimLoopType.OneShot, TimelineClip.ClipExtrapolation.None },
            {AnimVR.TrimLoopType.Infinity, TimelineClip.ClipExtrapolation.Continue }
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
            HasFades = false;

            PreviewTexture = new Texture2D(1, 1);
            PreviewTexture.LoadImage(stage.previewFrames[0], false);
            PreviewTexture.Apply();

            if (Settings.Shader == null)
            {
                Debug.Log("Resetting shader");
                Settings.Shader = "AnimVR/Standard";
            }

            materialToUse = new Material(Shader.Find(Settings.Shader));
            materialToUse.SetFloat("_Unlit", Settings.UnlitByDefault ? 1 : 0);
            materialToUse.SetFloat("_Gamma", PlayerSettings.colorSpace == ColorSpace.Gamma ? 1.0f : 2.2f);
            materialToUse.name = Path.GetFileNameWithoutExtension(ctx.assetPath) + "_BaseMaterial";

            needsAudioReimport = false;

            GenerateUnityObject(stage, ctx);

            ctx.AddObjectToAsset(Path.GetFileNameWithoutExtension(ctx.assetPath) + "_BaseMaterial", materialToUse);

            InfoString = "FPS: " + stage.fps + ", " + stage.timelineLength + " frames \n" 
                         + totalVertices + " verts, " + totalLines + " lines";

            savedClips = null;
            stage = null;

        }

        public struct Context
        {
            public Transform parentTransform, rootTransform;
            public PlayableDirector director;
            public Animator animator;
            public AssetImportContext ctx;
            public TimelineAsset parentTimeline;
            public float fps;
            public int frameOffset;
        }

        public GameObject GenerateUnityObject(StageData stage, AssetImportContext ctx)
        {
            var stageObj = new GameObject(stage.name);

            ctx.AddObjectToAsset(stage.name, stageObj, PreviewTexture);
            ctx.SetMainObject(stageObj);

            Context context = new Context();

            context.fps = stage.fps;
            context.parentTransform = context.rootTransform = stageObj.transform;

            context.director = stageObj.AddComponent<PlayableDirector>();
            context.director.extrapolationMode = Settings.DefaultWrapMode;

            var timelineAsset = TimelineAsset.CreateInstance<TimelineAsset>();
            timelineAsset.name = stage.name + "_Timeline";
            timelineAsset.editorSettings.fps = stage.fps;
            timelineAsset.durationMode = TimelineAsset.DurationMode.FixedLength;
            timelineAsset.fixedDuration = stage.timelineLength  * 1.0 / stage.fps;

            ctx.AddObjectToAsset(timelineAsset.name, timelineAsset);
            context.director.playableAsset = timelineAsset;

            context.animator = stageObj.AddComponent<Animator>();
            context.ctx = ctx;
            context.parentTimeline = timelineAsset;

            foreach(var symbol in stage.Symbols)
            {
                var symbolObj = GenerateUnityObject(symbol, context);

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

        public GameObject GenerateUnityObject(PlayableData playable, Context ctx)
        {
            if (playable.FadeIn != 0 || playable.FadeOut != 0) HasFades = true;

            if (playable is SymbolData) return GenerateUnityObject(playable as SymbolData, ctx);
            else if (playable is TimeLineData) return GenerateUnityObject(playable as TimeLineData, ctx);
// No audio support on Mac
#if UNITY_EDITOR_WIN
            else if (playable is AudioData && Settings.AudioImport != AudioImportSetting.None) return GenerateUnityObject(playable as AudioData, ctx);
#endif
            else if (playable is CameraData && Settings.ImportCameras) return GenerateUnityObject(playable as CameraData, ctx);
            else if (playable is StaticMeshData) return GenerateUnityObject(playable as StaticMeshData, ctx);
            else return null;
        }

        public GameObject MakePlayableBaseObject(PlayableData playable, ref Context ctx, float start, float duration)
        {
            start += ctx.frameOffset / ctx.fps;
            var hackyStart = Mathf.Max(0, start);
            var clipIn = hackyStart - start;
            start = hackyStart;
            duration -= clipIn;

            var playableObj = new GameObject(playable.displayName ?? "Layer");
            playableObj.transform.parent = ctx.parentTransform;
            playableObj.transform.localPosition = playable.transform.pos.V3;
            playableObj.transform.localRotation = playable.transform.rot.Q;
            playableObj.transform.localScale = playable.transform.scl.V3;

            playableObj.SetActive(playable.isVisible);

            var path = AnimationUtility.CalculateTransformPath(playableObj.transform, ctx.rootTransform);

            var director = playableObj.AddComponent<PlayableDirector>();
            var timelineAsset = TimelineAsset.CreateInstance<TimelineAsset>();
            timelineAsset.name = path + "_Timeline";
            timelineAsset.editorSettings.fps = stage.fps;
            timelineAsset.durationMode = TimelineAsset.DurationMode.BasedOnClips;
            timelineAsset.fixedDuration = start + duration;

            ctx.ctx.AddObjectToAsset(timelineAsset.name, timelineAsset);
            director.playableAsset = timelineAsset;

            ctx.animator = playableObj.AddComponent<Animator>();

            var controlTrack = ctx.parentTimeline.CreateTrack<AnimVR.Timeline.AnimControlTrack>(null, playableObj.name);
            ctx.ctx.AddObjectToAsset(path + "_Control", controlTrack);

            var controlClip = controlTrack.CreateDefaultClip();
            controlClip.displayName = playableObj.name;
            controlClip.start = start;
            controlClip.duration = duration;
            controlClip.clipIn = clipIn;

            typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(controlClip, TRIM_LOOP_MAPPING[playable.TrimLoopIn], null);
            typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(controlClip, TRIM_LOOP_MAPPING[playable.TrimLoopOut], null);

            var controlAsset = controlClip.asset as AnimVR.Timeline.AnimControlPlayableAsset;
            controlAsset.name = playable.name;
            ctx.director.SetGenericBinding(controlAsset, playableObj);

            ctx.ctx.AddObjectToAsset(path + "_ControlAsset", controlAsset);

            ctx.parentTimeline = timelineAsset;
            ctx.director = director;
            ctx.parentTransform = playableObj.transform;

            return playableObj;
        }

        public GameObject GenerateUnityObject(SymbolData symbol, Context ctx)
        {
            if (symbol.Playables.Count == 0) return null;

            int minPlayableStart = symbol.Playables.Min(p => p.GetLocalTrimStart(ctx.fps));
            int frameLength = symbol.Playables.Max(p => p.GetLocalTrimEnd(ctx.fps) - minPlayableStart);
            int frameStart = symbol.AbsoluteTimeOffset + minPlayableStart;

            if(symbol.didChangeTrimLength)
            {
                frameStart = symbol.AbsoluteTimeOffset - symbol.TrimIn;
                frameLength = 10 + symbol.TrimIn + symbol.TrimOut;
            }

            if(ctx.parentTransform == ctx.rootTransform)
            {
                frameStart = 0;
                frameLength = stage.timelineLength;
            }

            var symbolObj = MakePlayableBaseObject(symbol, ref ctx, frameStart/ctx.fps, frameLength/ctx.fps);

            ctx.frameOffset = symbol.TrimIn;
            foreach (var playbale in symbol.Playables)
            {
                if (playbale.isVisible)
                {
                    GenerateUnityObject(playbale, ctx);
                }
            }

            return symbolObj;
        }

        public GameObject GenerateUnityObject(TimeLineData playable, Context ctx)
        {
            int startFrame = playable.AbsoluteTimeOffset - playable.TrimIn;
            int frameCount = playable.GetFrameCount(stage.fps) + playable.TrimIn + playable.TrimOut;

            var playableObj = MakePlayableBaseObject(playable, ref ctx, startFrame/ctx.fps, frameCount/ctx.fps);
            var pathForName = AnimationUtility.CalculateTransformPath(playableObj.transform, ctx.rootTransform);

            // GROUP
            var groupTrack = ctx.parentTimeline.CreateTrack<GroupTrack>(null, playable.displayName);
            ctx.ctx.AddObjectToAsset(pathForName + "_GroupTrack", groupTrack);

            // ANIMATION
            var animationTrack = ctx.parentTimeline.CreateTrack<AnimVRTrack>(groupTrack, pathForName + "_animation");
            ctx.ctx.AddObjectToAsset(pathForName + "_animation", animationTrack);

            var animationClip = animationTrack.CreateDefaultClip();
            animationClip.duration = playable.GetFrameCount(stage.fps) / stage.fps;
            animationClip.start = playable.TrimIn / stage.fps;

            typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(animationClip, LOOP_MAPPING[playable.LoopIn], null);
            typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(animationClip, LOOP_MAPPING[playable.LoopOut], null);

            var animAsset = animationClip.asset as AnimVRFramesAsset;
            animAsset.FPS = stage.fps;

            ctx.director.SetGenericBinding(animAsset, playableObj);
            ctx.ctx.AddObjectToAsset(pathForName + "_animAsset", animAsset);

            /*
            // ACTIVATION
            var frameTrack = ctx.parentTimeline.CreateTrack<ActivationTrack>(groupTrack, pathForName + "_track");
            ctx.ctx.AddObjectToAsset(pathForName + "_track", frameTrack);
            ctx.director.SetGenericBinding(frameTrack, playableObj);

            var frameClip = frameTrack.CreateDefaultClip();
            frameClip.start = playable.LoopIn != AnimVR.LoopType.OneShot ? 0 : animationClip.start;
            frameClip.duration = playable.LoopOut != AnimVR.LoopType.OneShot ?
                ctx.parentTimeline.fixedDuration - frameClip.start : 
                (animationClip.start - frameClip.start) + animationClip.duration;

            ctx.ctx.AddObjectToAsset(pathForName + "_activeAsset", frameClip.asset);
            */

            int frameIndex = -1;
            foreach(var frame in playable.Frames)
            {
                if (!frame.isInstance)
                {
                    var frameObj = GenerateUnityObject(frame, ctx, ++frameIndex);
                    if (frameIndex != 0) frameObj.SetActive(false);
                    frameObj.transform.SetAsLastSibling();
                }
                animAsset.FrameIndices.Add(frameIndex);
            }

            return playableObj;
        }

        public GameObject GenerateUnityObject(AudioData playable, Context ctx)
        {
            AudioClip clip = null;

            var dir = Application.dataPath + Path.GetDirectoryName(ctx.ctx.assetPath).Substring(6);
            var clipPath = dir + "/" + Path.GetFileNameWithoutExtension(ctx.ctx.assetPath) + "_audio/" + playable.displayName + "_audio.wav";

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

            float start = playable.AbsoluteTimeOffset / stage.fps - playable.TrimIn;
            float duration = clip ? clip.length : 1 + playable.TrimIn + playable.TrimOut;


            var playableObj = MakePlayableBaseObject(playable, ref ctx, start, duration);
            var audioSource = playableObj.AddComponent<AudioSource>();
            audioSource.spatialBlend = playable.Spatialize ? 0 : 1;

            var pathForName = AnimationUtility.CalculateTransformPath(playableObj.transform, ctx.rootTransform);

            var groupTrack = ctx.parentTimeline.CreateTrack<GroupTrack>(null, playable.displayName);
            ctx.ctx.AddObjectToAsset(pathForName + "_GroupTrack", groupTrack);

            var track = ctx.parentTimeline.CreateTrack<AudioTrack>(groupTrack, playable.displayName);
            ctx.ctx.AddObjectToAsset(pathForName + "_audioTrack", track);

            bool loop = playable.LoopType == AnimVR.LoopType.Loop;

            var audioTrackClip = track.CreateDefaultClip();
            audioTrackClip.displayName = playable.displayName;
            (audioTrackClip.asset as AudioPlayableAsset).clip = clip;
            
            typeof(AudioPlayableAsset).GetField("m_Loop", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).
                SetValue(audioTrackClip.asset, loop);


            if(loop)
            {
                audioTrackClip.start = 0;
                audioTrackClip.duration = ctx.parentTimeline.fixedDuration;
                audioTrackClip.clipIn = duration - start % duration;
            }
            else
            {
                audioTrackClip.start = playable.TrimIn / ctx.fps;
                audioTrackClip.duration = duration;
            }

            ctx.ctx.AddObjectToAsset(pathForName + "_asset", audioTrackClip.asset);

            ctx.director.SetGenericBinding(track, audioSource);

            return playableObj;
        }


        public GameObject GenerateUnityObject(CameraData playable, Context ctx)
        {
            float start = playable.AbsoluteTimeOffset - playable.TrimIn;
            float duration = playable.RecordingTime + (playable.TrimIn + playable.TrimOut)/ctx.fps;
            var playableObj = MakePlayableBaseObject(playable, ref ctx, start / ctx.fps, duration);

            var transformAnchor = new GameObject("TransformAnchor");

            playable.CurrentShotOffset.ApplyTo(transformAnchor.transform);
            transformAnchor.transform.SetParent(playableObj.transform, false);

            var cam = transformAnchor.AddComponent<Camera>();
            cam.backgroundColor = stage.backgroundColor;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // TODO: Field of view 
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.nearClipPlane = 0.001f;

            var pathForName = AnimationUtility.CalculateTransformPath(transformAnchor.transform, ctx.rootTransform);

            var groupTrack = ctx.parentTimeline.CreateTrack<GroupTrack>(null, playable.displayName);
            ctx.ctx.AddObjectToAsset(pathForName + "_GroupTrack", groupTrack);

            if (playable.Timeline.Frames.Count > 0)
            {
                var animTrack = ctx.parentTimeline.CreateTrack<AnimationTrack>(groupTrack, pathForName + "_TransformTrack");

                ctx.director.SetGenericBinding(animTrack, ctx.animator);

                ctx.ctx.AddObjectToAsset(pathForName + "_TransformTrack", animTrack);

                var animationClip = MakeAnimationClip(playable.Timeline, AnimationUtility.CalculateTransformPath(transformAnchor.transform, ctx.parentTransform));

                ctx.ctx.AddObjectToAsset(pathForName + "_animation", animationClip);

                var timelineClip = animTrack.CreateClip(animationClip);
                timelineClip.start = playable.TrimIn;
                timelineClip.displayName = playable.displayName;

                typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(timelineClip, LOOP_MAPPING[playable.LoopIn], null);
                typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(timelineClip, LOOP_MAPPING[playable.LoopOut], null);

                ctx.ctx.AddObjectToAsset(pathForName + "_asset", timelineClip.asset);
            }

            return playableObj;
        }

        public GameObject GenerateUnityObject(StaticMeshData playable, Context ctx)
        {
            int frameStart = playable.AbsoluteTimeOffset - playable.TrimIn;
            int frameLength = playable.GetFrameCount(ctx.fps) + playable.TrimIn + playable.TrimOut;

            var playableObj = MakePlayableBaseObject(playable, ref ctx, frameStart / ctx.fps, frameLength / ctx.fps);

            var transformAnchor = new GameObject("TransformAnchor");
            transformAnchor.transform.SetParent(playableObj.transform, false);
            var pathForName = AnimationUtility.CalculateTransformPath(transformAnchor.transform, ctx.rootTransform);

            List<Material> materials = new List<Material>();

            int matIndex = 0;
            foreach(var matData in playable.Materials)
            {
                var mat = MeshUtils.MaterialFromData(matData, materialToUse);
                mat.name = pathForName + "_material" + (matIndex++).ToString();
                ctx.ctx.AddObjectToAsset(mat.name, mat);

                if (mat.mainTexture)
                {
                    ctx.ctx.AddObjectToAsset(mat.name + "_diffuse", mat.mainTexture);
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
                ctx.ctx.AddObjectToAsset(mf.sharedMesh.name, mf.sharedMesh);

                totalVertices += mf.sharedMesh.vertexCount;
            }

            var groupTrack = ctx.parentTimeline.CreateTrack<GroupTrack>(null, playable.displayName);
            ctx.ctx.AddObjectToAsset(pathForName + "_GroupTrack", groupTrack);

            double clipDuration = 1.0 / stage.fps;

            if (playable.InstanceMap.Count > 1)
            {
                var animTrack = ctx.parentTimeline.CreateTrack<AnimationTrack>(groupTrack, pathForName + "_TransformTrack");

                ctx.director.SetGenericBinding(animTrack, ctx.animator);

                ctx.ctx.AddObjectToAsset(pathForName + "_TransformTrack", animTrack);

                var animationClip = MakeAnimationClip(playable.InstanceMap, null, AnimationUtility.CalculateTransformPath(transformAnchor.transform, ctx.parentTransform));
                animationClip.name = pathForName + "_animation";

                ctx.ctx.AddObjectToAsset(pathForName + "_animation", animationClip);

                var timelineClip = animTrack.CreateClip(animationClip);
                timelineClip.start = playable.TrimIn;
                timelineClip.displayName = playable.displayName;

                typeof(TimelineClip).GetProperty("preExtrapolationMode").SetValue(timelineClip, LOOP_MAPPING[playable.LoopIn], null);
                typeof(TimelineClip).GetProperty("postExtrapolationMode").SetValue(timelineClip, LOOP_MAPPING[playable.LoopOut], null);

                clipDuration = timelineClip.duration;

                ctx.ctx.AddObjectToAsset(pathForName + "_asset", timelineClip.asset);
            }
            else
            {
                playable.InstanceMap[0].ApplyTo(transformAnchor.transform);
            }

            /*
            var activeTrack = ctx.parentTimeline.CreateTrack<ActivationTrack>(groupTrack, pathForName + "_Activation");
            ctx.ctx.AddObjectToAsset(pathForName + "_Activation", activeTrack);

            ctx.director.SetGenericBinding(activeTrack, playableObj);

            var clip = activeTrack.CreateDefaultClip();
            clip.start = playable.LoopIn != AnimVR.LoopType.OneShot ? 0 : playable.TrimIn;
            clip.duration = playable.LoopOut != AnimVR.LoopType.OneShot ? ctx.parentTimeline.fixedDuration - clip.start : (playable.TrimIn - clip.start) + clipDuration;

            ctx.ctx.AddObjectToAsset(pathForName + "_activeAsset", clip.asset);*/

            return playableObj;
        }

        public GameObject GenerateUnityObject(FrameData frame, Context ctx, int index)
        {
            var playableObj = new GameObject(index.ToString());
            playableObj.transform.parent = ctx.parentTransform;
            playableObj.transform.localPosition = frame.transform.pos.V3;
            playableObj.transform.localRotation = frame.transform.rot.Q;
            playableObj.transform.localScale = frame.transform.scl.V3;

            var pathForName = AnimationUtility.CalculateTransformPath(playableObj.transform, ctx.rootTransform);


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

                ctx.ctx.AddObjectToAsset(pathForName + "_mesh" + index, mf.sharedMesh);
                meshId++;
            }


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
#pragma warning disable CS0618 // Type or member is obsolete
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
#pragma warning restore CS0618 // Type or member is obsolete

            animationClip.frameRate = stage.fps;

            return animationClip;
        }

        public AnimationClip MakeAnimationClip(TransformTimelineData timeline, string path)
        {
            return MakeAnimationClip(timeline.Frames, timeline.FrameTimes, path);
        }

    }
}