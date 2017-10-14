using UnityEngine.Timeline;

namespace AnimVR.Timeline
{
    /// <summary>
    ///   <para>A Track whose clips control time-related elements on a GameObject.</para>
    /// </summary>
    [TrackColor(0.2313f, 0.6353f, 0.5843f)]
    [TrackClipType(typeof(AnimControlPlayableAsset))]
    [TrackMediaType(TimelineAsset.MediaType.Script)]
    public class AnimControlTrack : TrackAsset
    {
    }
}
