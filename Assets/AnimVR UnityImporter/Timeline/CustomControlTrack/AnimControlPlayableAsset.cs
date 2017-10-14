// Decompiled with JetBrains decompiler
// Type: UnityEngine.Timeline.ControlPlayableAsset
// Assembly: UnityEngine.Timeline, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 4983A86B-0DC2-486A-9404-133C2F91C639
// Assembly location: C:\dev\AnimVR2017\AnimVR_2017\Library\UnityAssemblies\UnityEngine.Timeline.dll

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AnimVR.Timeline
{
    /// <summary>
    ///   <para>Asset that generates playables for controlling time-related elements on a GameObject.</para>
    /// </summary>
    [NotKeyable]
    [Serializable]
    public class AnimControlPlayableAsset : PlayableAsset, IPropertyPreview, ITimelineClipAsset
    {
        private static readonly int k_MaxRandInt = 10000;
        /// <summary>
        ///   <para>Indicates if user wants to control ParticleSystems.</para>
        /// </summary>
        [SerializeField]
        public bool updateParticle = true;
        /// <summary>
        ///   <para>Indicate if user wants to control PlayableDirectors.</para>
        /// </summary>
        [SerializeField]
        public bool updateDirector = true;
        /// <summary>
        ///   <para>Indicates that whether Monobehaviours implementing ITimeControl on the gameObject will be controlled.</para>
        /// </summary>
        [SerializeField]
        public bool updateITimeControl = true;
        /// <summary>
        ///   <para>Indicate whether to search the entire hierachy for controlable components.</para>
        /// </summary>
        [SerializeField]
        public bool searchHierarchy = true;
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
        private double m_Duration = PlayableBinding.DefaultDuration;
        /// <summary>
        ///   <para>Prefab object that will be instantiated.</para>
        /// </summary>
        [SerializeField]
        public GameObject prefabGameObject;
        /// <summary>
        ///   <para>Let the particle systems behave the same way on each execution.</para>
        /// </summary>
        [SerializeField]
        public uint particleRandomSeed;
        private PlayableAsset m_ControlDirectorAsset;
        private bool m_SupportLoop;

        public override double duration
        {
            get
            {
                return this.m_Duration;
            }
        }

        public ClipCaps clipCaps
        {
            get
            {
                return  (ClipCaps)(12 | (!this.m_SupportLoop ? 0 : 1) | (int)ClipCaps.Extrapolation | (int)ClipCaps.Blending);
            }
        }

        public void OnEnable()
        {
            if ((int)this.particleRandomSeed != 0)
                return;
            this.particleRandomSeed = (uint)UnityEngine.Random.Range(1, AnimControlPlayableAsset.k_MaxRandInt);
        }

        GameObject GetGameObjectBinding(PlayableDirector director)
        {
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

        public override IEnumerable<PlayableBinding> outputs
        {
            get
            {
                var binding = new PlayableBinding();
                binding.streamType = DataStreamType.None;
                binding.sourceObject = this;
                binding.streamName = this.name + " Source";
                binding.sourceBindingType = typeof(GameObject);
                return new PlayableBinding[] { binding };
            }
        }

        public override Playable CreatePlayable(PlayableGraph graph, GameObject go)
        {
            List<Playable> playableList = new List<Playable>();
            GameObject gameObject = GetGameObjectBinding(go.GetComponent<PlayableDirector>());

            if (this.prefabGameObject != null)
            {
                Transform parentTransform = !(gameObject != null) ? (Transform)null : gameObject.transform;
                ScriptPlayable<PrefabControlPlayable> scriptPlayable = PrefabControlPlayable.Create(graph, this.prefabGameObject, parentTransform);
                gameObject = scriptPlayable.GetBehaviour().prefabInstance;
                playableList.Add((Playable)scriptPlayable);
            }

            this.UpdateDurationAndLoopFlag(gameObject);
            if (gameObject == null)
                return Playable.Create(graph, 0);

            PlayableDirector component = go.GetComponent<PlayableDirector>();
            if (component != null)
                this.m_ControlDirectorAsset = component.playableAsset;

            if (go == gameObject && this.prefabGameObject == null)
            {
                UnityEngine.Debug.LogWarning((object)("Control Playable (" + this.name + ") is referencing the same PlayableDirector component than the one in which it is playing."));
                this.active = false;
                if (!this.searchHierarchy)
                    this.updateDirector = false;
            }

            if (this.active)
                this.CreateActivationPlayable(gameObject, graph, playableList);
            if (this.updateDirector)
                this.SearchHierarchyAndConnectDirector(gameObject, graph, playableList);
            if (this.updateParticle)
                this.SearchHiearchyAndConnectParticleSystem(gameObject, graph, playableList);
            if (this.updateITimeControl)
                this.SearchHierarchyAndConnectControlableScripts(gameObject, graph, playableList);

            return this.ConnectPlayablesToMixer(graph, playableList);
        }

        private Playable ConnectPlayablesToMixer(PlayableGraph graph, List<Playable> playables)
        {
            Playable playable = Playable.Create(graph, playables.Count);
            for (int portIndex = 0; portIndex != playables.Count; ++portIndex)
                this.ConnectMixerAndPlayable(graph, playable, playables[portIndex], portIndex);
            playable.SetPropagateSetTime<Playable>(true);
            return playable;
        }

        private void CreateActivationPlayable(GameObject root, PlayableGraph graph, List<Playable> outplayables)
        {
            ScriptPlayable<ActivationControlPlayable> playable = ActivationControlPlayable.Create(graph, root, this.postPlayback);
            if (!playable.IsValid<ScriptPlayable<ActivationControlPlayable>>())
                return;
            outplayables.Add((Playable)playable);
        }

        private bool SearchHiearchyAndConnectParticleSystem(GameObject root, PlayableGraph graph, List<Playable> outplayables)
        {
            if (root == null)
                return false;
            bool flag = false;
            foreach (ParticleSystem particleSystem in this.GetParticleSystems(root))
            {
                if (particleSystem != null)
                {
                    flag = true;
                    outplayables.Add((Playable)ParticleControlPlayable.Create(graph, particleSystem, this.particleRandomSeed));
                }
            }
            return flag;
        }

        private void SearchHierarchyAndConnectDirector(GameObject root, PlayableGraph graph, List<Playable> outplayables)
        {
            if (root == null)
                return;
            foreach (PlayableDirector director in this.GetDirectors(root))
            {
                if (director != null && director.playableAsset != this.m_ControlDirectorAsset)
                    outplayables.Add((Playable)AnimDirectorControlPlayable.Create(graph, director));
            }
        }

        private void SearchHierarchyAndConnectControlableScripts(GameObject root, PlayableGraph graph, List<Playable> outplayables)
        {
            if (root == null)
                return;
            foreach (MonoBehaviour controlableScript in this.GetControlableScripts(root))
                outplayables.Add((Playable)TimeControlPlayable.Create(graph, (ITimeControl)controlableScript));
        }

        private void ConnectMixerAndPlayable(PlayableGraph graph, Playable mixer, Playable playable, int portIndex)
        {
            graph.Connect<Playable, Playable>(playable, 0, mixer, portIndex);
            mixer.SetInputWeight<Playable, Playable>(playable, 1f);
        }

        [DebuggerHidden]
        private IEnumerable<ParticleSystem> GetParticleSystems(GameObject gameObject)
        {
            return (IEnumerable<ParticleSystem>)gameObject.GetComponents<ParticleSystem>();
        }

        [DebuggerHidden]
        private IEnumerable<PlayableDirector> GetDirectors(GameObject gameObject)
        {
            return (IEnumerable<PlayableDirector>)gameObject.GetComponents<PlayableDirector>();
        }

        [DebuggerHidden]
        private IEnumerable<MonoBehaviour> GetControlableScripts(GameObject root)
        {
            return (IEnumerable<MonoBehaviour>)root.GetComponents<ITimeControl>().Select(v => v as MonoBehaviour);
        }

        private void UpdateDurationAndLoopFlag(GameObject gameObject)
        {
            this.m_Duration = PlayableBinding.DefaultDuration;
            this.m_SupportLoop = false;
            if (gameObject == null)
                return;
            List<PlayableDirector> list1 = this.GetDirectors(gameObject).ToList<PlayableDirector>();
            List<ParticleSystem> list2 = this.GetParticleSystems(gameObject).ToList<ParticleSystem>();
            if (list1.Count == 1 && list2.Count == 0 && list1[0].playableAsset != null)
            {
                this.m_Duration = list1[0].playableAsset.duration;
                this.m_SupportLoop = list1[0].extrapolationMode == DirectorWrapMode.Loop;
            }
            else
            {
                if (list1.Count != 0 || list2.Count != 1)
                    return;
                this.m_Duration = (double)list2[0].main.duration;
                this.m_SupportLoop = list2[0].main.loop;
            }
        }

        public void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            GameObject gameObject = GetGameObjectBinding(director);
            if (!gameObject)
                return;

            if (this.updateParticle)
            {
                foreach (ParticleSystem particleSystem in this.GetParticleSystems(gameObject))
                {
                    driver.AddFromName<ParticleSystem>(particleSystem.gameObject, "randomSeed");
                    driver.AddFromName<ParticleSystem>(particleSystem.gameObject, "autoRandomSeed");
                }
            }
            if (this.active)
                driver.AddFromName(gameObject, "m_IsActive");
            if (this.updateITimeControl)
            {
                foreach (MonoBehaviour controlableScript in this.GetControlableScripts(gameObject))
                {
                    IPropertyPreview propertyPreview = controlableScript as IPropertyPreview;
                    if (propertyPreview != null)
                        propertyPreview.GatherProperties(director, driver);
                    else
                        driver.AddFromComponent(controlableScript.gameObject, (Component)controlableScript);
                }
            }
        }
    }
}
