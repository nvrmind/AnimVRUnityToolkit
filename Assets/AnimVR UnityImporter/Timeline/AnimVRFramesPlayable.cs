using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace ANIMVR
{
    using FrameData = UnityEngine.Playables.FrameData;

    public class AnimVRFramesPlayable : PlayableBehaviour
    {
        public Transform Parent;
        public double FPS;
        public List<int> FrameIndices;


        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            base.ProcessFrame(playable, info, playerData);

            if (!Parent) return;

            var time = playable.GetTime();

            int index = Mathf.FloorToInt((float)(time * FPS)) % FrameIndices.Count;

            int currentIndex = FrameIndices[index];

            for(int i = 0; i < Parent.childCount; i++)
            {
                Parent.GetChild(i).gameObject.SetActive(i == currentIndex);
            }
        }
    }
}
