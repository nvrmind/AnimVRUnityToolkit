// Decompiled with JetBrains decompiler
// Type: UnityEngine.Timeline.DirectorControlPlayable
// Assembly: UnityEngine.Timeline, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 4983A86B-0DC2-486A-9404-133C2F91C639
// Assembly location: C:\dev\AnimVR2017\AnimVR_2017\Library\UnityAssemblies\UnityEngine.Timeline.dll

using System;
using UnityEngine;
using UnityEngine.Playables;


namespace AnimVR.Timeline
{
    /// <summary>
    ///   <para>Playable used to control other PlayableDirectors.</para>
    /// </summary>
    public class AnimDirectorControlPlayable : PlayableBehaviour
    {
        /// <summary>
        ///   <para>The PlayableDirector to control.</para>
        /// </summary>
        public PlayableDirector director;

        public static ScriptPlayable<AnimDirectorControlPlayable> Create(PlayableGraph graph, PlayableDirector director)
        {
            if ((UnityEngine.Object)director == (UnityEngine.Object)null)
                return ScriptPlayable<AnimDirectorControlPlayable>.Null;
            ScriptPlayable<AnimDirectorControlPlayable> scriptPlayable = ScriptPlayable<AnimDirectorControlPlayable>.Create(graph, 0);
            scriptPlayable.GetBehaviour().director = director;
            return scriptPlayable;
        }

        public override void PrepareFrame(Playable playable, UnityEngine.Playables.FrameData info)
        {
            if ((UnityEngine.Object)this.director == (UnityEngine.Object)null || !this.director.isActiveAndEnabled || (UnityEngine.Object)this.director.playableAsset == (UnityEngine.Object)null)
                return;
            if (this.director.playableGraph.IsValid())
            {
                int rootPlayableCount = this.director.playableGraph.GetRootPlayableCount();
                for (int index = 0; index < rootPlayableCount; ++index)
                {
                    Playable rootPlayable = this.director.playableGraph.GetRootPlayable(index);
                    if (rootPlayable.IsValid<Playable>())
                    {
                        rootPlayable.SetSpeed<Playable>((double)info.effectiveSpeed);
                        for(int i = 0; i < rootPlayable.GetInputCount(); i++)
                        {
                            rootPlayable.SetInputWeight(i, info.effectiveWeight);
                        }
                    }
                }
            }

            this.UpdateTime(playable);
        }

        public override void OnBehaviourPlay(Playable playable, UnityEngine.Playables.FrameData info)
        {
            if (!((UnityEngine.Object)this.director != (UnityEngine.Object)null) || !((UnityEngine.Object)this.director.playableAsset != (UnityEngine.Object)null))
                return;
            this.UpdateTime(playable);
            this.director.Evaluate();
            this.director.Play();
        }

        public override void OnBehaviourPause(Playable playable, UnityEngine.Playables.FrameData info)
        {
            if (!((UnityEngine.Object)this.director != (UnityEngine.Object)null) || !((UnityEngine.Object)this.director.playableAsset != (UnityEngine.Object)null))
                return;
            this.director.Pause();
        }

        private void UpdateTime(Playable playable)
        {
            double val1 = Math.Max(0.1, this.director.playableAsset.duration);
            switch (this.director.extrapolationMode)
            {
                case DirectorWrapMode.Hold:
                    this.director.time = Math.Min(val1, Math.Max(0.0, playable.GetTime<Playable>()));
                    break;
                case DirectorWrapMode.Loop:
                    this.director.time = Math.Max(0.0, playable.GetTime<Playable>() % val1);
                    break;
                case DirectorWrapMode.None:
                    this.director.time = playable.GetTime<Playable>();
                    break;
            }
            this.director.Evaluate();
        }
    }
}
