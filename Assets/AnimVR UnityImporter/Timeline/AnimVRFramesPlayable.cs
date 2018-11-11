using AnimVR;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace ANIMVR {
    using FrameData = UnityEngine.Playables.FrameData;

    public class AnimVRFramesPlayable : PlayableBehaviour {
        public FrameFadeMode FadeIn, FadeOut;
        public Transform Parent;
        public double FPS;
        public List<int> FrameIndices;

        List<Transform> frameTransforms;
        Renderer[] renderers;
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        public override void OnGraphStart(Playable playable) {
            base.OnGraphStart(playable);
            renderers = Parent.GetComponentsInChildren<Renderer>(true);
            frameTransforms = new List<Transform>(Parent.childCount);
            for (int i = 0; i < Parent.childCount; i++) {
                frameTransforms.Add(Parent.GetChild(i));
            }

        }

        public override void OnBehaviourPlay(Playable playable, FrameData info) {
            Update(playable, info);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info) {
            Update(playable, info);
        }


        public override void ProcessFrame(Playable playable, FrameData info, object playerData) {
            Update(playable, info);
        }

        void Update(Playable playable, FrameData info) {
            if (!Parent) return;

            var time = playable.GetTime();

            int index = Mathf.FloorToInt((float)(time * FPS)) % FrameIndices.Count;

            int currentIndex = FrameIndices[index];

            for (int i = 0; i < frameTransforms.Count; i++) {
                frameTransforms[i].gameObject.SetActive(i == currentIndex);
            }

            block.SetFloat("_FadeIn",  info.effectiveWeight);
            block.SetFloat("_FadeOut", 1 /*info.effectiveWeight*/);
            block.SetInt("_FadeModeIn",  (int)FadeIn);
            block.SetInt("_FadeModeOut", (int)FadeOut);
            foreach (var r in renderers) {
                r.SetPropertyBlock(block);
            }
        }
    }
}
