using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ANIMVR
{
    [System.Serializable]
    [TrackClipType(typeof(AnimVRFramesAsset))]
    [TrackMediaType(TimelineAsset.MediaType.Script)]
    [TrackColor(0.53f, 0.0f, 0.08f)]
    public class AnimVRTrack : TrackAsset
    {
        GameObject GetGameObjectBinding(PlayableDirector director)
        {
            if ((UnityEngine.Object)director == (UnityEngine.Object)null)
                return (GameObject)null;

            GameObject genericBinding1 = director.GetGenericBinding((UnityEngine.Object)this) as GameObject;
            if ((UnityEngine.Object)genericBinding1 != (UnityEngine.Object)null)
                return genericBinding1;

            Component genericBinding2 = director.GetGenericBinding((UnityEngine.Object)this) as Component;
            if ((UnityEngine.Object)genericBinding2 != (UnityEngine.Object)null)
                return genericBinding2.gameObject;
            return (GameObject)null;
        }
        
        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            GameObject gameObjectBinding = this.GetGameObjectBinding(director);
            if (!((UnityEngine.Object) gameObjectBinding != (UnityEngine.Object) null))
                return;

            foreach (Transform child in gameObjectBinding.transform)
            {
                driver.AddFromName(child.gameObject, "m_IsActive");
            }
        }
    }
}

