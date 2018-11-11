using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using FrameData = UnityEngine.Playables.FrameData;

namespace AnimVR.Timeline {

    /// <summary>
    ///   <para>Asset that generates playables for controlling time-related elements on a GameObject.</para>
    /// </summary>
    [Serializable]
    [NotKeyable]
    public class AnimControlPlayableAsset : PlayableAsset, IPropertyPreview, ITimelineClipAsset {

        private static readonly List<PlayableDirector> k_EmptyDirectorsList = new List<PlayableDirector>(0);

        /// <summary>
        ///   <para>Indicate if user wants to control PlayableDirectors.</para>
        /// </summary>
        [SerializeField]
        public bool updateDirector = true;

        /// <summary>
        ///   <para>Indicate if the playable will use Activation.</para>
        /// </summary>
        [SerializeField]
        public bool active = true;

        /// <summary>
        ///   <para>Indicates the active state of the gameObject when the Timeline is stopped.</para>
        /// </summary>
        [SerializeField]
        public ActivationControlPlayable.PostPlaybackState postPlayback = ActivationControlPlayable.PostPlaybackState.Revert;

        private PlayableAsset m_ControlDirectorAsset;

        private double m_Duration = PlayableBinding.DefaultDuration;

        private bool m_SupportLoop;

        private static HashSet<PlayableDirector> s_ProcessedDirectors = new HashSet<PlayableDirector>();


        public override double duration { get { return m_Duration; } }

        public ClipCaps clipCaps {
            get {
                return (ClipCaps)(12 | (!this.m_SupportLoop ? 0 : 1) | (int)ClipCaps.Extrapolation | (int)ClipCaps.Blending);
            }
        }

        GameObject GetGameObjectBinding(PlayableDirector director) {
            if (director == null)
                return null;

            GameObject genericBinding1 = director.GetGenericBinding(this) as GameObject;
            if (genericBinding1 != null)
                return genericBinding1;

            Component genericBinding2 = director.GetGenericBinding(this) as Component;
            if (genericBinding2 != null)
                return genericBinding2.gameObject;
            return null;
        }

        public override IEnumerable<PlayableBinding> outputs {
            get {
                var binding = AnimationPlayableBinding.Create(this.name + " Source", this);
                return new PlayableBinding[] { binding };
            }
        }


        public override Playable CreatePlayable(PlayableGraph graph, GameObject go) {

            Playable val = Playable.Null;

            List<Playable> list = new List<Playable>();
            GameObject val2 = GetGameObjectBinding(go.GetComponent<PlayableDirector>());


            m_Duration = PlayableBinding.DefaultDuration;
            m_SupportLoop = false;
            if (val2 != null) {
                IList<PlayableDirector> directors = (!updateDirector) ? k_EmptyDirectorsList : GetComponent<PlayableDirector>(val2);
                UpdateDurationAndLoopFlag(directors);
                PlayableDirector component = go.GetComponent<PlayableDirector>();
                if (component != null) {
                    m_ControlDirectorAsset = component.playableAsset;
                }

                if (active) {
                    CreateActivationPlayable(val2, graph, list);
                }
                if (updateDirector) {
                    SearchHierarchyAndConnectDirector(directors, graph, list, false);
                }

                val = ConnectPlayablesToMixer(graph, list);
            }
            if (!PlayableExtensions.IsValid<Playable>(val)) {
                val = Playable.Create(graph, 0);
            }
            return val;
        }

        private static Playable ConnectPlayablesToMixer(PlayableGraph graph, List<Playable> playables) {
            Playable val = Playable.Create(graph, playables.Count);
            for (int i = 0; i != playables.Count; i++) {
                ConnectMixerAndPlayable(graph, val, playables[i], i);
            }
            PlayableExtensions.SetPropagateSetTime<Playable>(val, true);
            return val;
        }

        private void CreateActivationPlayable(GameObject root, PlayableGraph graph, List<Playable> outplayables) {
            ScriptPlayable<ActivationControlPlayable> val = ActivationControlPlayable.Create(graph, root, postPlayback);
            if (PlayableExtensions.IsValid<ScriptPlayable<ActivationControlPlayable>>(val)) {
                outplayables.Add(val);
            }
        }

        private void SearchHierarchyAndConnectDirector(IEnumerable<PlayableDirector> directors, PlayableGraph graph, List<Playable> outplayables, bool disableSelfReferences) {
            foreach (PlayableDirector director in directors) {
                if (director != null) {
                    if (director.playableAsset != m_ControlDirectorAsset) {
                        outplayables.Add(DirectorControlPlayable.Create(graph, director));
                    } else if (disableSelfReferences) {
                        director.enabled = false;
                    }
                }
            }
        }

        private static void ConnectMixerAndPlayable(PlayableGraph graph, Playable mixer, Playable playable, int portIndex) {
            graph.Connect<Playable, Playable>(playable, 0, mixer, portIndex);
            PlayableExtensions.SetInputWeight<Playable, Playable>(mixer, playable, 1f);
        }

        internal IList<T> GetComponent<T>(GameObject gameObject) {
            List<T> list = new List<T>();
            if (gameObject != null) {
                gameObject.GetComponents<T>(list);
            }
            return list;
        }

        private static IEnumerable<MonoBehaviour> GetControlableScripts(GameObject root) {
            if (!(root == null)) {
                MonoBehaviour[] componentsInChildren = root.GetComponentsInChildren<MonoBehaviour>();
                int num = 0;
                MonoBehaviour script;
                while (true) {
                    if (num >= componentsInChildren.Length) {
                        yield break;
                    }
                    script = componentsInChildren[num];
                    if (script is ITimeControl) {
                        break;
                    }
                    num++;
                }
                yield return script;
                /*Error: Unable to find new state assignment for yield return*/
                ;
            }
        }

        private double OnTickAfter(double time) {
            long discrete = (long)(time / 1E-12 + 0.5);
            discrete++;
            return (double)discrete * 1E-12;
        }

        private void UpdateDurationAndLoopFlag(IList<PlayableDirector> directors) {
            if (directors.Count != 0) {
                double num = double.NegativeInfinity;
                bool flag = false;
                foreach (PlayableDirector director in directors) {
                    if (director.playableAsset != null) {
                        double num2 = director.playableAsset.duration;
                        if (director.playableAsset is TimelineAsset && num2 > 0.0) {
                            num2 = OnTickAfter(num2);
                        }
                        num = Math.Max(num, num2);
                        flag = (flag || (int)director.extrapolationMode == 1);
                    }
                }

                m_Duration = ((!double.IsNegativeInfinity(num)) ? num : PlayableBinding.DefaultDuration);
                m_SupportLoop = flag;
            }
        }



        public void GatherProperties(PlayableDirector director, IPropertyCollector driver) {
            if (!(director == null) && !s_ProcessedDirectors.Contains(director)) {
                s_ProcessedDirectors.Add(director);
                GameObject val = GetGameObjectBinding(director);
                if (val != null) {
                    if (active) {
                        driver.AddFromName(val, "m_IsActive");
                    }

                    if (updateDirector) {
                        foreach (PlayableDirector item in GetComponent<PlayableDirector>(val)) {
                            if (!(item == null)) {
                                TimelineAsset timelineAsset = item.playableAsset as TimelineAsset;
                                if (!(timelineAsset == null)) {
                                    timelineAsset.GatherProperties(item, driver);
                                }
                            }
                        }
                    }
                }
                s_ProcessedDirectors.Remove(director);
            }
        }
    }


    public class ActivationControlPlayable : PlayableBehaviour {
        public enum PostPlaybackState {
            Active,
            Inactive,
            Revert
        }

        private enum InitialState {
            Unset,
            Active,
            Inactive
        }

        public GameObject gameObject = null;

        public PostPlaybackState postPlayback = PostPlaybackState.Revert;

        private InitialState m_InitialState;

        public static ScriptPlayable<ActivationControlPlayable> Create(PlayableGraph graph, GameObject gameObject, PostPlaybackState postPlaybackState) {
            if (gameObject == null) {
                return ScriptPlayable<ActivationControlPlayable>.Null;
            }
            ScriptPlayable<ActivationControlPlayable> result = ScriptPlayable<ActivationControlPlayable>.Create(graph);
            ActivationControlPlayable behaviour = result.GetBehaviour();
            behaviour.gameObject = gameObject;
            behaviour.postPlayback = postPlaybackState;
            return result;
        }

        public override void OnBehaviourPlay(Playable playable, UnityEngine.Playables.FrameData info) {
            if (!(gameObject == null)) {
                gameObject.SetActive(value: true);
                m_InitialState = InitialState.Active;
            }
        }

        public override void OnBehaviourPause(Playable playable, UnityEngine.Playables.FrameData info) {
            if (!(gameObject == null) && 
                ( (info.seekOccurred && info.evaluationType == UnityEngine.Playables.FrameData.EvaluationType.Evaluate) || 
                  (info.deltaTime > 0 && playable.GetGraph().IsPlaying()))) {
                gameObject.SetActive(value: false);
                m_InitialState = InitialState.Inactive;
            }
        }

        public override void ProcessFrame(Playable playable, UnityEngine.Playables.FrameData info, object userData) {
            if (gameObject != null) {
                gameObject.SetActive(value: true);
                m_InitialState = InitialState.Active;
            }
        }


        public override void OnPlayableDestroy(Playable playable) {
            if (!(gameObject == null) && m_InitialState != 0) {
                switch (postPlayback) {
                    case PostPlaybackState.Active:
                        gameObject.SetActive(value: true);
                        break;
                    case PostPlaybackState.Inactive:
                        gameObject.SetActive(value: false);
                        break;
                    case PostPlaybackState.Revert:
                        gameObject.SetActive(m_InitialState == InitialState.Active);
                        break;
                }
            }
        }
    }


    public class DirectorControlPlayable : PlayableBehaviour {
        public PlayableDirector director;

        private bool m_SyncTime = false;

        private double m_AssetDuration = 1.7976931348623157E+308;

        

        public static ScriptPlayable<DirectorControlPlayable> Create(PlayableGraph graph, PlayableDirector director) {
            if (director == null) {
                return ScriptPlayable<DirectorControlPlayable>.Null;
            }
            ScriptPlayable<DirectorControlPlayable> result = ScriptPlayable<DirectorControlPlayable>.Create(graph);
            result.GetBehaviour().director = director;
            return result;
        }

        public override void PrepareFrame(Playable playable, UnityEngine.Playables.FrameData info) {
            if (!(director == null) && director.isActiveAndEnabled && !(director.playableAsset == null)) {
                m_SyncTime |= (info.evaluationType == UnityEngine.Playables.FrameData.EvaluationType.Evaluate || DetectDiscontinuity(playable, info));
                SyncSpeed((double)info.effectiveSpeed);
                SyncPlayState(playable.GetGraph(), playable.GetTime());
                SyncWeight(info.effectiveWeight);
            }
        }

        public override void OnBehaviourPlay(Playable playable, UnityEngine.Playables.FrameData info) {
            m_SyncTime = true;
            if (director != null && director.playableAsset != null) {
                m_AssetDuration = director.playableAsset.duration;
                SyncWeight(info.effectiveWeight);
            }
        }

        public override void OnBehaviourPause(Playable playable, UnityEngine.Playables.FrameData info) {
            if (director != null && director.playableAsset != null) {
                SyncWeight(info.effectiveWeight);
                director.Stop();
            }
        }

        public override void ProcessFrame(Playable playable, UnityEngine.Playables.FrameData info, object playerData) {
            if (!(director == null) && director.isActiveAndEnabled && !(director.playableAsset == null)) {
                if (m_SyncTime || DetectOutOfSync(playable)) {
                    UpdateTime(playable);
                    director.Evaluate();
                }
                m_SyncTime = false;
                SyncWeight(info.effectiveWeight);
            }
        }

        private void SyncSpeed(double speed) {
            if (director.playableGraph.IsValid()) {
                int rootPlayableCount = director.playableGraph.GetRootPlayableCount();
                for (int i = 0; i < rootPlayableCount; i++) {
                    Playable rootPlayable = director.playableGraph.GetRootPlayable(i);
                    if (rootPlayable.IsValid()) {
                        rootPlayable.SetSpeed(speed);
                    }
                }
            }
        }

        private void SyncWeight(float weight) {
            if (director.playableGraph.IsValid()) {
                int rootPlayableCount = director.playableGraph.GetRootPlayableCount();
                for (int i = 0; i < rootPlayableCount; i++) {
                    Playable rootPlayable = director.playableGraph.GetRootPlayable(i);
                    if (rootPlayable.IsValid() && rootPlayable.CanSetWeights()) {
                        for(int j = 0; j < rootPlayable.GetInputCount(); j++) {
                            rootPlayable.SetInputWeight(j, weight);
                        }
                    }
                }
            }
        }

        private void SyncPlayState(PlayableGraph graph, double playableTime) {
            bool outOfRange = playableTime >= m_AssetDuration && director.extrapolationMode == DirectorWrapMode.None;
            if (graph.IsPlaying() && !outOfRange) {
                director.Play();
            } else {
                director.Pause();
            }
        }

        private bool DetectDiscontinuity(Playable playable, UnityEngine.Playables.FrameData info) {
            return Math.Abs(playable.GetTime() - playable.GetPreviousTime() - info.deltaTime * (double)info.effectiveWeight) > 1E-12;
        }

        private bool DetectOutOfSync(Playable playable) {
            double num = playable.GetTime();
            if (playable.GetTime() >= m_AssetDuration) {
                if (director.extrapolationMode == DirectorWrapMode.None) {
                    return false;
                }
                if (director.extrapolationMode == DirectorWrapMode.Hold) {
                    num = m_AssetDuration;
                } else if (m_AssetDuration > 1.4012984643248171E-45) {
                    num %= m_AssetDuration;
                }
            }
            if (!Mathf.Approximately((float)num, (float)director.time)) {
                return true;
            }
            return false;
        }

        private void UpdateTime(Playable playable) {
            double num = Math.Max(0.1, director.playableAsset.duration);
            switch (director.extrapolationMode) {
                case DirectorWrapMode.Hold:
                    director.time = Math.Min(num, Math.Max(0.0, playable.GetTime()));
                    break;
                case DirectorWrapMode.Loop:
                    director.time = Math.Max(0.0, playable.GetTime() % num);
                    break;
                case DirectorWrapMode.None:
                    director.time = playable.GetTime();
                    break;
            }
        }
    }

}
