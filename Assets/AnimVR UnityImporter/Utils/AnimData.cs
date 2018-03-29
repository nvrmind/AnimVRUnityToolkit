#if UNITY_EDITOR || ANIM_RUNTIME_AVAILABLE

using System.Collections.Generic;
using AnimVR;
using UnityEngine;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using Debug = UnityEngine.Debug;
using ZipFile = Ionic.Zip.ZipFile;
using NAudio.Wave;
using NAudio.Lame;
using System.Text.RegularExpressions;
using System.Collections;
using System.Threading;

#if !ANIM_RUNTIME_AVAILABLE
public partial class AnimMeshTransform : MonoBehaviour { }
public class AnimFrame : MonoBehaviour { }
public class AnimLine : MonoBehaviour { }

public class Singleton<T> : MonoBehaviour { }

public class AnimPlayable : MonoBehaviour { }
public class AnimStage : AnimPlayable { }
public class AnimSymbol : AnimPlayable { }
public class AnimTimeline : AnimPlayable { }
public class AnimCam : AnimPlayable { }
public class AnimAudio : AnimPlayable { }
public class AnimVideo : AnimPlayable { }
public class AnimMesh : AnimPlayable { }
public class AnimSkybox : AnimPlayable { }
public class AnimUnityImport : AnimPlayable { }

public class AnimPuppet : AnimPlayable { }
public class PuppetFrame : AnimPlayable { }
public class PuppetPoint : AnimPlayable { }
public class PuppetLink : AnimPlayable { }
#endif

public enum BrushType { Sphere, Cube, Splat, Point, Ribbon }
public enum BrushMode { Paint, Line }
public enum AudioStorageType { Raw, Compressed, Mp3 }

namespace AnimVR
{
    public enum LoopType { Loop, OneShot, Hold }
    public enum TrimLoopType { Loop, OneShot, Hold, Infinity }
    public enum CreateMode { After, Before, End, Start }

    [Flags]
    public enum FrameFadeMode
    {
        FadeOpacity = 1,
        FadeOpacityAlongLine = 2,
        FadeWidth = 4,
        FadeWidthAlongLine = 8,
        RandomOffset = 16,
        EndToStart = 32,
    }

}

public static class AudioDataUtils
{
    private const int SampleSize = 1024;

    public static AudioClip DecodeToClip(byte[] memory, int channels, int frequency, AudioStorageType storageType)
    {
        switch (storageType)
        {
            case AudioStorageType.Raw: return DecodeClipFromRaw(memory, channels, frequency);
            case AudioStorageType.Mp3: return DecodeClipFromMp3(memory);
        }

        return null;
    }

    public static byte[] EncodeSamples(float[] samples, int channels, int frequency, out AudioStorageType storageType)
    {
        float lengthInSecond = ((float)samples.Length / channels) / frequency;
        byte[] result = null;
        if (lengthInSecond < 20)
        {
            result = EncodeSamplesToOgg(samples);
            storageType = AudioStorageType.Raw;
        }
        else
        {
            result = EncodeSamplesToMp3(samples, channels, frequency);
            storageType = AudioStorageType.Mp3;
        }

        return result;
    }

    private static AudioClip DecodeClipFromRaw(byte[] memory, int channels, int frequency)
    {
        float[] samples = new float[memory.Length / sizeof(float)];
        Buffer.BlockCopy(memory, 0, samples, 0, memory.Length);

        AudioClip result = AudioClip.Create("", samples.Length / channels, channels, frequency, false);
        result.SetData(samples, 0);
        result.LoadAudioData();

        return result;

        /*
        using (MemoryStream inStream = new MemoryStream(memory))
        using (var vorbis = new NVorbis.VorbisReader(inStream, true))
        {
            // get the channels & sample rate
            //var channels = vorbis.Channels;
            var sampleRate = vorbis.SampleRate;

            Debug.Log(channels);
            Debug.Log(sampleRate);
            Debug.Log(vorbis.TotalSamples);

            // OPTIONALLY: get a TimeSpan indicating the total length of the Vorbis stream
            var totalTime = vorbis.TotalTime;

            // create a buffer for reading samples
            var readBuffer = new float[channels * sampleRate / 5];  // 200ms

            // get the initial position (obviously the start)
            var position = TimeSpan.Zero;

            var allSamples = new List<float>();

            int offset = 0;
            // go grab samples
            int cnt;
            while ((cnt = vorbis.ReadSamples(readBuffer, 0, readBuffer.Length)) > 0)
            {
                allSamples.AddRange(readBuffer.Take(cnt));
            }

            AudioClip clip = AudioClip.Create("", allSamples.Count / channels, channels, sampleRate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.SetData(allSamples.ToArray(), 0);
            clip.LoadAudioData();

            return clip;
        }*/
    }

    private static AudioClip DecodeClipFromMp3(byte[] encodedSamples)
    {
        int channels, frequency;
        var samples = DecodeSamplesFromMp3(encodedSamples, out channels, out frequency);
        AudioClip clip = AudioClip.Create("", samples.Length / channels, channels, frequency, false);
        clip.SetData(samples, 0);
        clip.LoadAudioData();

        return clip;
    }


    public class StreamAudioFileResult
    {
        public List<float> samples = new List<float>();
        public int channels = 0;
        public int frequency = 0;
        public int totalSamples = 0;
    }

    public static IEnumerator DecodeSamplesFromFileAsync(string filename, StreamAudioFileResult result)
    {
        if(Path.GetExtension(filename).ToLower() == ".ogg")
        {
            NVorbis.VorbisReader reader = null;

            try
            {
                var createThread = new Thread(() => reader = new NVorbis.VorbisReader(filename));
                createThread.Start();

                while (createThread.ThreadState != System.Threading.ThreadState.Stopped) yield return null;

                createThread.Join();

                if (reader == null)
                {
                    result.channels = -1;
                    Debug.LogException(new Exception("Failed to load audio file: " + filename));
                    yield break;
                }

                result.frequency = reader.SampleRate;
                result.channels = reader.Channels;
                result.totalSamples = reader.TotalSamples != long.MaxValue ? (int)reader.TotalSamples : 0;
                

                float[] buffer = new float[result.channels * result.frequency / 5];
                int totalRead = 0;
                int bytesRead;
                while ((bytesRead = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
                {
                    totalRead += bytesRead;

                    if (bytesRead == buffer.Length)
                    {
                        result.samples.AddRange(buffer);
                    }
                    else
                    {
                        result.samples.AddRange(buffer.Take(bytesRead));
                    }

                    yield return null;
                }

                if(result.totalSamples == 0)
                {
                    result.totalSamples = (int)reader.DecodedPosition;
                }

            }
            finally
            {
                if (reader != null)
                {
                    reader.Dispose();
                }
            }
        }
        else
        {
            NAudio.Wave.AudioFileReader reader = null;
            try
            {
                var createThread = new Thread(() => reader = new NAudio.Wave.AudioFileReader(filename));
                createThread.Start();

                while (createThread.ThreadState != System.Threading.ThreadState.Stopped) yield return null;

                createThread.Join();

                if (reader == null)
                {
                    result.channels = -1;
                    Debug.LogException(new Exception("Failed to load audio file: " + filename));
                    yield break;
                }

                result.frequency = reader.WaveFormat.SampleRate;
                result.channels = reader.WaveFormat.Channels;
                result.totalSamples = (int)reader.Length/(4 * reader.WaveFormat.Channels);

                NAudio.Wave.SampleProviders.SampleChannel channel = new NAudio.Wave.SampleProviders.SampleChannel(reader, false);

                float[] buffer = new float[channel.WaveFormat.AverageBytesPerSecond / 20];
                int totalRead = 0;
                int bytesRead;
                do
                {
                    bytesRead = channel.Read(buffer, 0, buffer.Length);
                    totalRead += bytesRead;

                    if (bytesRead == buffer.Length)
                    {
                        result.samples.AddRange(buffer);
                    }
                    else
                    {
                        result.samples.AddRange(buffer.Take(bytesRead));
                    }

                    yield return null;
                } while (bytesRead > 0);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Dispose();
                }
            }
        }
    }

    public static float[] DecodeSamplesFromFile(string filename, out int channels, out int frequency)
    {
        StreamAudioFileResult result = new StreamAudioFileResult();
        var loadFile = DecodeSamplesFromFileAsync(filename, result);
        channels = result.channels;
        frequency = result.frequency;
        while (loadFile.MoveNext()) { }
        return result.samples.ToArray();
    }

    public static float[] DecodeSamplesFromMp3(byte[] encodedSamples, out int channels, out int frequency)
    {
        using (var stream = new MemoryStream(encodedSamples))
        using (var reader = new NAudio.Wave.Mp3FileReader(stream))
        {
            List<float> samples = new List<float>();
            frequency = reader.Mp3WaveFormat.SampleRate;
            channels = reader.Mp3WaveFormat.Channels;


            NAudio.Wave.SampleProviders.SampleChannel channel = new NAudio.Wave.SampleProviders.SampleChannel(reader, false);

            float[] buffer = new float[channel.WaveFormat.AverageBytesPerSecond];
            int totalRead = 0;
            int bytesRead;
            do
            {
                bytesRead = channel.Read(buffer, 0, buffer.Length);
                totalRead += bytesRead;

                if (bytesRead == buffer.Length)
                {
                    samples.AddRange(buffer);
                }
                else
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        samples.Add(buffer[i]);
                    }
                }

            } while (bytesRead > 0);

            return samples.ToArray();
        }
    }

    private static byte[] EncodeClipToOgg(AudioClip clip)
    {
        var sampleBuffer = new float[clip.samples * clip.channels];
        var success = clip.GetData(sampleBuffer, 0);

        Debug.Log("Channels: " + clip.channels);
        Debug.Log("Sample Rate: " + clip.frequency);

        if (!success) return null;
        return EncodeSamplesToOgg(sampleBuffer);
    }

    private static byte[] EncodeSamplesToOgg(float[] sampleBuffer)
    {
        byte[] inBytes = new byte[sampleBuffer.Length * sizeof(float)];
        Buffer.BlockCopy(sampleBuffer, 0, inBytes, 0, inBytes.Length);

        Debug.Log("Encoding " + sampleBuffer.Length + " samples.");

        return inBytes;
    }

    private static byte[] EncodeSamplesToMp3(float[] samples, int channels, int frequency)
    {
        WaveFormat wv = WaveFormat.CreateIeeeFloatWaveFormat(frequency, channels);

        using (var retMs = new MemoryStream())
        using (var wtr = new LameMP3FileWriter(retMs, wv, 128))
        {
            byte[] result = new byte[samples.Length * sizeof(float)];
            Debug.Assert(samples.Length * 4 == result.Length);
            Buffer.BlockCopy(samples, 0, result, 0, result.Length);
            wtr.Write(result, 0, result.Length);
            wtr.Flush();

            return retMs.ToArray();
        }
    }

    private static byte[] EncodeClipToMp3(AudioClip clip)
    {
        var samples = new float[clip.samples * clip.channels];
        var success = clip.GetData(samples, 0);

        if (!success) return null;

        WaveFormat wv = WaveFormat.CreateIeeeFloatWaveFormat(clip.frequency, clip.channels);

        Debug.Log("Converting clip " + clip.name + " to mp3.");
        Debug.Log("Sample Rate: " + wv.SampleRate);
        Debug.Log("Bits Per Sample: " + wv.BitsPerSample);
        Debug.Log("Channels: " + wv.Channels);

        using (var retMs = new MemoryStream())
        using (var wtr = new LameMP3FileWriter(retMs, wv, 128))
        {
            byte[] result = new byte[samples.Length * sizeof(float)];
            Debug.Assert(samples.Length * 4 == result.Length);
            Buffer.BlockCopy(samples, 0, result, 0, result.Length);
            Debug.Log("Writing " + samples.Length + " samples...");
            wtr.Write(result, 0, result.Length);
            wtr.Flush();
            Debug.Log("Resulting in " + retMs.Length + " mp3 samples...");

            return retMs.ToArray();
        }
    }
}

public static class ListExtensions
{
    static Dictionary<Type, System.Reflection.MethodInfo> DeepCopyMethodCache = new Dictionary<Type, System.Reflection.MethodInfo>();

    public static T Last<T>(this List<T> list)
    {
        return list[list.Count - 1];
    }

    public static List<T> DeepCopy<T>(this List<T> list)
    {
        if (list == null) return null;
        var result = new List<T>(list.Count);

        var tType = typeof(T);

        // Directly implements IDeepCopy
        if (typeof(IDeepCopy<T>).IsAssignableFrom(tType))
        {
            for (int i = 0; i < list.Count; i++)
            {
                result.Add((list[i] as IDeepCopy<T>).DeepCopy());
            }
        }
        // Some parent class implements IDeepCopy
        else
        {
            System.Reflection.MethodInfo copyMethod = null;

            if (!DeepCopyMethodCache.ContainsKey(tType))
            {
                var interfaceType = typeof(T).GetInterfaces().FirstOrDefault(x => x.IsGenericType &&
                                    x.GetGenericTypeDefinition() == typeof(IDeepCopy<>));
                if (interfaceType != null)
                {
                    var args = interfaceType.GetGenericArguments();
                    copyMethod = args[0].GetMethod("DeepCopy");
                }

                DeepCopyMethodCache[tType] = copyMethod;
            }
            else
            {
                copyMethod = DeepCopyMethodCache[tType];
            }

            if (copyMethod != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var res = copyMethod.Invoke(list[i], null);
                    result.Add((T)res);
                }
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    result.Add(list[i]);
                }
            }
        }

        return result;
    }

    public static T[] DeepCopy<T>(this T[] original)
    {
        if (original == null) return null;
        T[] result = new T[original.Length];

        var tType = typeof(T);

        // Directly implements IDeepCopy
        if (typeof(IDeepCopy<T>).IsAssignableFrom(tType))
        {
            for (int i = 0; i < original.Length; i++)
            {
                result[i] = (original[i] as IDeepCopy<T>).DeepCopy();
            }
        }
        // Some parent class implements IDeepCopy
        else
        {
            System.Reflection.MethodInfo copyMethod = null;

            if (!DeepCopyMethodCache.ContainsKey(tType))
            {
                var interfaceType = typeof(T).GetInterfaces().FirstOrDefault(x => x.IsGenericType &&
                                    x.GetGenericTypeDefinition() == typeof(IDeepCopy<>));
                if (interfaceType != null)
                {
                    var args = interfaceType.GetGenericArguments();
                    copyMethod = args[0].GetMethod("DeepCopy");
                }

                DeepCopyMethodCache[tType] = copyMethod;
            }
            else
            {
                copyMethod = DeepCopyMethodCache[tType];
            }

            if (copyMethod != null)
            {
                for (int i = 0; i < original.Length; i++)
                {
                    result[i] = (T)copyMethod.Invoke(original[i], null);
                }
            }
            else
            {
                Array.Copy(original, result, original.Length);
            }
        }

        return result;
    }

    public static void AddBefore<T>(this List<T> list, int index, T item)
    {
        list.Insert(index, item);
    }

    public static void AddAfter<T>(this List<T> list, ref int index, T item)
    {
        if (list.Count == 0 && index == 0)
        {
            list.Add(item);
        }
        else
        {
            if (index > list.Count)
                Debug.Log(index + " " + list.Count);
            list.Insert(index + 1, item);
            index++;
        }
    }

    public static void AddToList<T>(this List<T> list, ref int index, T item, CreateMode mode)
    {
        switch (mode)
        {
            case CreateMode.After: list.AddAfter(ref index, item); break;
            case CreateMode.Before: list.AddBefore(index, item); break;
            case CreateMode.End: list.Add(item); break;
            case CreateMode.Start: list.Insert(0, item); break;
        }
    }
}

partial class AnimMeshTransform
{
    [Serializable]
    public class MeshFrameTransformDataProxy : IAnimData, IDeepCopy<MeshFrameTransformDataProxy>
    {
        [NonSerialized]
        public AnimMeshTransform meshTransform;
        SerializableTransform transform = new SerializableTransform();

        public MonoBehaviour attachedObj
        {
            get
            {
                return meshTransform;
            }
        }

        public string name
        {
            get
            {
                return "";
            }

            set
            {

            }
        }

        public SerializableTransform Trans
        {
            get
            {
                return transform;
            }
        }

        public MeshFrameTransformDataProxy DeepCopy()
        {
            var result = new MeshFrameTransformDataProxy();
            result.transform = transform.DeepCopy();
            return result;
        }

        public void SetTransform(SerializableTransform transform)
        {
            this.transform = transform;
#if ANIM_RUNTIME_AVAILABLE
            int index = this.meshTransform.TransformProxies.IndexOf(this);
            this.meshTransform.parentMesh.meshData.Timeline.Frames[index].Fill(transform);
            this.meshTransform.parentMesh.UpdateView();
#endif
        }
    }
}

[Serializable]
public class TransformFrame
{
    public Vector3 Position;
    public Vector3 Scale;
    public Quaternion Rotation;

    public TransformFrame()
    {

    }

    public TransformFrame(SerializableTransform transform)
    {
        Position = transform.pos.V3;
        Scale = transform.scl.V3;
        Rotation = transform.rot.Q;
    }

    public TransformFrame(Transform transform)
    {
        Position = transform.localPosition;
        Scale = transform.localScale;
        Rotation = transform.localRotation;
    }

    public static TransformFrame Lerp(TransformFrame a, TransformFrame b, float t)
    {
        TransformFrame interpolated = new TransformFrame();
        interpolated.Position = Vector3.Lerp(a.Position, b.Position, t);
        interpolated.Scale = Vector3.Lerp(a.Scale, b.Scale, t);
        interpolated.Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
        return interpolated;
    }

    public void From(Transform transform)
    {
        Position = transform.localPosition;
        Scale = transform.localScale;
        Rotation = transform.localRotation;
    }

    public void ApplyTo(Transform transform)
    {
        transform.localPosition = Position;
        transform.localScale = Scale;
        transform.localRotation = Rotation;
    }
}

[Serializable]
public struct TransformTimeline
{
    public List<float> FrameTimes;
    public List<TransformFrame> Frames;

    public TransformTimeline(TransformTimelineData data)
    {
        FrameTimes = new List<float>(data.FrameTimes.ToArray());
        Frames = data.Frames.Select(v => new TransformFrame(v)).ToList();
    }

    public void RemoveKey(float time)
    {
        int removeFrame = FrameTimes.BinarySearch(time);
        if (removeFrame >= 0)
        {
            FrameTimes.RemoveAt(removeFrame);
            Frames.RemoveAt(removeFrame);
        }
    }

    public void AddKey(float time, TransformFrame frame)
    {
        int insertFrame = FrameTimes.BinarySearch(time);
        if (insertFrame < 0)
        {
            insertFrame = ~insertFrame;
            FrameTimes.Insert(insertFrame, time);
            Frames.Insert(insertFrame, frame);
        }
        else
        {
            Frames[insertFrame] = frame;
        }
    }

    public TransformFrame Evaluate(float time, bool interpolate)
    {
        if (Frames.Count == 0) return new TransformFrame();

        int startFrameIndex = FrameTimes.BinarySearch(time);

        if (startFrameIndex < 0) startFrameIndex = ~startFrameIndex - 1;

        if (!interpolate) return Frames[startFrameIndex];
        if (startFrameIndex < 0) return Frames[0];
        if (startFrameIndex >= Frames.Count - 1) return Frames[Frames.Count - 1];

        int nextFrameIndex = startFrameIndex + 1;
        var startFrame = Frames[startFrameIndex];
        var nextFrame = Frames[nextFrameIndex];

        float interp = (time - FrameTimes[startFrameIndex]) / (FrameTimes[nextFrameIndex] - FrameTimes[startFrameIndex]);

        return TransformFrame.Lerp(startFrame, nextFrame, interp);
    }

    public TransformFrame Evaluate(int index)
    {
        return Frames[index];
    }
}

[System.Serializable]
public struct SerializableVector3
{
    public float x;
    public float y;
    public float z;

    public SerializableVector3(Vector3 v)
    {
        x = v.x;
        y = v.y;
        z = v.z;
    }

    public SerializableVector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    private void Fill(Vector3 v3)
    {
        x = v3.x;
        y = v3.y;
        z = v3.z;
    }

    public Vector3 V3 { get { return new Vector3(x, y, z); } set { Fill(value); } }


    public override bool Equals(object obj)
    {
        if (!(obj is SerializableVector3)) return false;
        return this == (SerializableVector3)obj;
    }

    public override int GetHashCode()
    {
        var hashCode = 373119288;
        hashCode = hashCode * -1521134295 + base.GetHashCode();
        hashCode = hashCode * -1521134295 + x.GetHashCode();
        hashCode = hashCode * -1521134295 + y.GetHashCode();
        hashCode = hashCode * -1521134295 + z.GetHashCode();
        return hashCode;
    }

    public static bool operator ==(SerializableVector3 x, SerializableVector3 y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.x == y.x && x.y == y.y && x.z == y.z;
    }

    public static bool operator !=(SerializableVector3 x, SerializableVector3 y)
    {
        return !(x == y);
    }

    public static implicit operator Vector3(SerializableVector3 s)
    {
        return new Vector3(s.x, s.y, s.z);
    }

    public static implicit operator SerializableVector3(Vector3 c)
    {
        return new SerializableVector3(c);
    }
}

[System.Serializable]
public struct SerializableVector2
{
    public float x;
    public float y;

    public SerializableVector2(Vector2 v)
    {
        x = v.x;
        y = v.y;
    }

    public SerializableVector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }


    private void Fill(Vector2 v3)
    {
        x = v3.x;
        y = v3.y;
    }

    public Vector2 V2 { get { return new Vector2(x, y); } set { Fill(value); } }


    public override bool Equals(object obj)
    {
        if (!(obj is SerializableVector2)) return false;
        return this == (SerializableVector2)obj;
    }

    public override int GetHashCode()
    {
        var hashCode = 1502939027;
        hashCode = hashCode * -1521134295 + base.GetHashCode();
        hashCode = hashCode * -1521134295 + x.GetHashCode();
        hashCode = hashCode * -1521134295 + y.GetHashCode();
        return hashCode;
    }

    public static bool operator ==(SerializableVector2 x, SerializableVector2 y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.x == y.x && x.y == y.y;
    }

    public static bool operator !=(SerializableVector2 x, SerializableVector2 y)
    {
        return !(x == y);
    }

    public static implicit operator Vector2(SerializableVector2 s)
    {
        return new Vector2(s.x, s.y);
    }

    public static implicit operator SerializableVector2(Vector2 c)
    {
        return new SerializableVector2(c);
    }
}

[System.Serializable]
public struct SerializableQuaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public static readonly SerializableQuaternion identity = new SerializableQuaternion(Quaternion.identity);

    public SerializableQuaternion(Quaternion i)
    {
        this.x = i.x;
        this.y = i.y;
        this.z = i.z;
        this.w = i.w;
    }

    private void Fill(Quaternion q)
    {
        x = q.x;
        y = q.y;
        z = q.z;
        w = q.w;
    }

    public Quaternion Q { get { return new Quaternion(x, y, z, w); } set { Fill(value); } }

    public override bool Equals(object obj)
    {
        if (!(obj is SerializableQuaternion)) return false;
        return this == (SerializableQuaternion)obj;
    }

    public override int GetHashCode()
    {
        var hashCode = -1743314642;
        hashCode = hashCode * -1521134295 + base.GetHashCode();
        hashCode = hashCode * -1521134295 + x.GetHashCode();
        hashCode = hashCode * -1521134295 + y.GetHashCode();
        hashCode = hashCode * -1521134295 + z.GetHashCode();
        hashCode = hashCode * -1521134295 + w.GetHashCode();
        return hashCode;
    }

    public static bool operator ==(SerializableQuaternion x, SerializableQuaternion y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.x == y.x && x.y == y.y && x.z == y.z && x.w == y.w;
    }

    public static bool operator !=(SerializableQuaternion x, SerializableQuaternion y)
    {
        return !(x == y);
    }

    public static implicit operator Quaternion(SerializableQuaternion s)
    {
        return new Quaternion(s.x, s.y, s.z, s.w);
    }

    public static implicit operator SerializableQuaternion(Quaternion c)
    {
        return new SerializableQuaternion(c);
    }

}

[System.Serializable]
public struct SerializableColor
{
    public float r;
    public float g;
    public float b;
    public float a;

    private void Fill(Color c)
    {
        r = c.r;
        g = c.g;
        b = c.b;
        a = c.a;
    }

    public SerializableColor(float r, float g, float b, float a)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    public SerializableColor(Color c) : this(c.r, c.g, c.b, c.a)
    {
    }

    public void SetAlpha(float a)
    {
        this.a = a;
    }

    public static implicit operator Color(SerializableColor s)
    {
        return new Color(s.r, s.g, s.b, s.a);
    }

    public static implicit operator SerializableColor(Color c)
    {
        return new SerializableColor(c);
    }

    public Color C
    {
        get { return new Color(r, g, b, a); }
        set { Fill(value); }
    }

    public override bool Equals(object obj)
    {
        if (!(obj is SerializableColor)) return false;
        return this == (SerializableColor)obj;
    }

    public override int GetHashCode()
    {
        var hashCode = -355389506;
        hashCode = hashCode * -1521134295 + base.GetHashCode();
        hashCode = hashCode * -1521134295 + r.GetHashCode();
        hashCode = hashCode * -1521134295 + g.GetHashCode();
        hashCode = hashCode * -1521134295 + b.GetHashCode();
        hashCode = hashCode * -1521134295 + a.GetHashCode();
        hashCode = hashCode * -1521134295 + EqualityComparer<Color>.Default.GetHashCode(C);
        return hashCode;
    }

    public static bool operator ==(SerializableColor x, SerializableColor y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a;
    }

    public static bool operator !=(SerializableColor x, SerializableColor y)
    {
        return !(x == y);
    }
}


public interface IDeepCopy<T>
{
    T DeepCopy();
}

[System.Serializable]
public class SerializableTransform : IDeepCopy<SerializableTransform>
{
    public SerializableVector3 pos = new SerializableVector3();
    public SerializableQuaternion rot = new SerializableQuaternion(Quaternion.identity);
    public SerializableVector3 scl = new SerializableVector3(1, 1, 1);


    public void Lerp(SerializableTransform a, SerializableTransform b, float t)
    {
        pos.V3 = Vector3.Lerp(a.pos.V3, b.pos.V3, t);
        scl.V3 = Vector3.Lerp(a.scl.V3, b.scl.V3, t);
        rot.Q = Quaternion.Slerp(a.rot.Q, b.rot.Q, t);
    }

    public SerializableTransform()
    {

    }

    public SerializableTransform(SerializableTransform other)
    {
        this.pos.V3 = other.pos.V3;
        this.rot.Q = other.rot.Q;
        this.scl.V3 = other.scl.V3;
    }

    public SerializableTransform(Transform transform)
    {
        Fill(transform);
    }

    public SerializableTransform(TransformFrame transform)
    {
        pos.V3 = transform.Position;
        scl.V3 = transform.Scale;
        rot.Q = transform.Rotation;
    }

    public void Fill(SerializableTransform other)
    {
        this.pos.V3 = other.pos.V3;
        this.rot.Q = other.rot.Q;
        this.scl.V3 = other.scl.V3;
    }

    public void Fill(Transform t)
    {
        pos.V3 = t.localPosition;
        rot.Q = t.localRotation;
        scl.V3 = t.localScale;
    }

    public void FillWorld(Transform t)
    {
        pos.V3 = t.position;
        rot.Q = t.rotation;
        scl.V3 = t.lossyScale;
    }

    public void ApplyTo(Transform t)
    {
        t.localPosition = pos.V3;
        t.localRotation = rot.Q;
        t.localScale = scl.V3;
    }

    public void ApplyTo(ref Vector3 v)
    {
        var s = scl.V3;
        v.x *= s.x;
        v.y *= s.y;
        v.z *= s.z;

        v = rot.Q * v;
        v += pos.V3;
    }

    public Matrix4x4 Mat
    {
        get
        {
            return Matrix4x4.TRS(pos.V3, rot.Q, scl.V3);
        }
    }

    public Transform Trans { set { Fill(value); } }

    public override bool Equals(object obj)
    {
        SerializableTransform data = obj as SerializableTransform;
        if (data == null) return false;
        return this == data;
    }

    public SerializableTransform DeepCopy()
    {
        return new SerializableTransform(this);
    }

    public override int GetHashCode()
    {
        var hashCode = 1619851620;
        hashCode = hashCode * -1521134295 + EqualityComparer<SerializableVector3>.Default.GetHashCode(pos);
        hashCode = hashCode * -1521134295 + EqualityComparer<SerializableQuaternion>.Default.GetHashCode(rot);
        hashCode = hashCode * -1521134295 + EqualityComparer<SerializableVector3>.Default.GetHashCode(scl);
        return hashCode;
    }

    public static bool operator ==(SerializableTransform x, SerializableTransform y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.pos == y.pos && x.rot == y.rot && x.scl == y.scl;
    }

    public static bool operator !=(SerializableTransform x, SerializableTransform y)
    {
        return !(x == y);
    }
}

public interface IAnimData
{
    string name { get; set; }
    void SetTransform(SerializableTransform transform);
    SerializableTransform Trans { get; }
    MonoBehaviour attachedObj { get; }
}

[Serializable]
public struct BrushStyle
{
    public BrushType brushType;
    public BrushMode brushMode;
    public bool isOneSided;
    public bool isFlat;
    public bool taperOpacity;
    public bool taperShape;
    public bool constantWidth;
    public bool multiLine;
    public bool isWeb;
    public bool isObjectSpaceTex;
    public int textureIndex;

    public BrushStyle(LineData line)
    {
        brushType = line.brushType;
        brushMode = line.brushMode;
        isOneSided = line.isOneSided;
        isFlat = line.isFlat;
        taperOpacity = line.taperOpacity;
        taperShape = line.taperShape;
        constantWidth = line.constantWidth;
        multiLine = line.multiLine;
        isWeb = line.isWeb;
        isObjectSpaceTex = line.isObjectSpaceTex;
        textureIndex = line.textureIndex;
    }

    public void ApplyTo(LineData line)
    {
        line.brushType = brushType;
        line.brushMode = brushMode;
        line.isOneSided = isOneSided;
        line.isFlat = isFlat;
        line.taperOpacity = taperOpacity;
        line.taperShape = taperShape;
        line.constantWidth = constantWidth;
        line.multiLine = multiLine;
        line.isWeb = isWeb;
        line.isObjectSpaceTex = isObjectSpaceTex;
        line.textureIndex = textureIndex;
    }
}

[System.Serializable]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
public class LineData : IAnimData, IDeepCopy<LineData>
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    [OptionalField]
    [NonSerialized]
    public string name = "Line" + Guid.NewGuid();

    string IAnimData.name
    {
        get
        {
            return "Line";
        }

        set
        {
        }
    }

    public BrushType brushType;
    public BrushMode brushMode;
    public List<float> widths = new List<float>();
    public List<SerializableVector3> Points = new List<SerializableVector3>();
    public List<SerializableQuaternion> rotations = new List<SerializableQuaternion>();
    public List<SerializableColor> colors = new List<SerializableColor>();
    public SerializableTransform transform = new SerializableTransform();

    [OptionalField]
    public List<float> light = new List<float>();

    [OptionalField(VersionAdded = 2)]
    public List<SerializableQuaternion> cameraOrientations = new List<SerializableQuaternion>();
    [OptionalField(VersionAdded = 2)]
    public bool isOneSided = false;

    [OptionalField(VersionAdded = 2)]
    public bool isFlat = false;
    [OptionalField(VersionAdded = 2)]
    public bool taperOpacity = false;
    [OptionalField(VersionAdded = 2)]
    public bool taperShape = false;
    [OptionalField(VersionAdded = 3)]
    public bool constantWidth = false;
    [OptionalField(VersionAdded = 3)]
    public bool multiLine = false;
    [OptionalField(VersionAdded = 4)]
    public bool isWeb = false;
    [OptionalField(VersionAdded = 5)]
    public bool isObjectSpaceTex = false;

    [OptionalField(VersionAdded = 5)]
    public int textureIndex = -1;

    [OnDeserializing]
    private void SetDefaults(StreamingContext sc)
    {
        cameraOrientations = new List<SerializableQuaternion>();
        isOneSided = false;
        isFlat = false;
        taperOpacity = false;
        taperShape = false;
        constantWidth = false;
        multiLine = false;
        isWeb = false;
        isObjectSpaceTex = false;
        textureIndex = -1;
    }

    [OnDeserialized]
    private void FormatData(StreamingContext sc)
    {
        if (cameraOrientations.Count > Points.Count)
        {
            cameraOrientations = cameraOrientations.GetRange(0, Points.Count);
        }

        if (light == null || light.Count == 0)
        {
            light = Enumerable.Repeat(0.5f, Points.Count).ToList();
        }
    }

    [NonSerialized]
    public AnimLine attachedLine;

    public override bool Equals(object obj)
    {
        LineData data = obj as LineData;
        if (data == null) return false;
        return this == data;
    }

    public void SetTransform(SerializableTransform transform)
    {
        this.transform = transform;
        if (attachedLine) transform.ApplyTo(attachedLine.transform);
    }

    public LineData DeepCopy()
    {
        var result = new LineData();
        result.attachedLine = null;
        result.brushMode = brushMode;
        result.brushType = brushType;
        result.cameraOrientations = cameraOrientations.DeepCopy();
        result.colors = colors.DeepCopy();
        result.constantWidth = constantWidth;
        result.isFlat = isFlat;
        result.isObjectSpaceTex = isObjectSpaceTex;
        result.isOneSided = isOneSided;
        result.isWeb = isWeb;
        result.light = light.DeepCopy();
        result.multiLine = multiLine;
        result.Points = Points.DeepCopy();
        result.rotations = rotations.DeepCopy();
        result.taperOpacity = taperOpacity;
        result.taperShape = taperShape;
        result.textureIndex = textureIndex;
        result.transform = transform.DeepCopy();
        result.widths = widths.DeepCopy();
        return result;
    }

    public SerializableTransform Trans { get { return transform; } }

    public MonoBehaviour attachedObj
    {
        get { return attachedLine; }
    }

    public static bool operator ==(LineData x, LineData y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.name == y.name && x.brushType == y.brushType && x.brushMode == y.brushMode &&
               x.widths.SequenceEqual(y.widths) && x.Points.SequenceEqual(y.Points) && x.rotations.SequenceEqual(y.rotations) &&
               x.colors.SequenceEqual(y.colors) && x.transform == y.transform;
    }

    public static bool operator !=(LineData x, LineData y)
    {
        return !(x == y);
    }
}

[System.Serializable]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
public class FrameData : IAnimData, IDeepCopy<FrameData>
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    public string name = "Frame" + Guid.NewGuid();
    string IAnimData.name
    {
        get
        {
            return name;
        }

        set
        {
            name = value;
        }
    }

    [OptionalField]
    public FrameFadeMode FadeModeIn = FrameFadeMode.FadeOpacity;

    [OptionalField]
    public FrameFadeMode FadeModeOut = FrameFadeMode.FadeOpacity;

    public bool isInstance = false;
    public List<LineData> Lines = new List<LineData>();
    public SerializableTransform transform = new SerializableTransform();

    public MonoBehaviour attachedObj
    {
        get { return attachedFrame; }
    }

    [NonSerialized]
    public AnimFrame attachedFrame;

    public override bool Equals(object obj)
    {
        FrameData data = obj as FrameData;
        if (data == null) return false;
        return this == data;
    }

    public void SetTransform(SerializableTransform transform)
    {
        this.transform = transform;
        if (attachedFrame) transform.ApplyTo(attachedFrame.transform);
    }

    public FrameData DeepCopy()
    {
        FrameData result = new FrameData();
        result.isInstance = isInstance;
        result.Lines = Lines.DeepCopy();
        result.name = name;
        result.transform = transform.DeepCopy();
        result.FadeModeIn = FadeModeIn;
        result.FadeModeOut = FadeModeOut;
        return result;
    }

    public SerializableTransform Trans { get { return transform; } }

    public static bool operator ==(FrameData x, FrameData y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.name == y.name && x.isInstance == y.isInstance &&
               x.Lines.SequenceEqual(y.Lines) && x.transform == y.transform;
    }

    public static bool operator !=(FrameData x, FrameData y)
    {
        return !(x == y);
    }
}

[System.Serializable]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
public class TimeLineData : PlayableData
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    public List<FrameData> Frames = new List<FrameData>();

    [OptionalField]
    public bool FrameFadeInOutLinked = true;

    public AnimTimeline attachedTimeline { get { return base.attachedPlayable as AnimTimeline; } }

    public override int GetFrameCount(float fps)
    {
        return Frames.Count;
    }

    public override bool Equals(object obj)
    {
        TimeLineData data = obj as TimeLineData;
        if (data == null) return false;
        return this == data;
    }

    public static bool operator ==(TimeLineData x, TimeLineData y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.name == y.name && x.LoopType == y.LoopType &&
               x.Frames.SequenceEqual(y.Frames) && x.transform == y.transform;
    }

    public static bool operator !=(TimeLineData x, TimeLineData y)
    {
        return !(x == y);
    }

    public override PlayableData DeepCopy()
    {
        var result = new TimeLineData();
        PlayableData.CopyPlayableData(this, result);
        result.FrameFadeInOutLinked = FrameFadeInOutLinked;
        result.Frames = Frames.DeepCopy();

        return result;
    }

    [OnDeserialized]
    private void FormatDataDeserialized(StreamingContext sc)
    {
        if (SaveDataVersion == 0)
        {
            TrimLoopIn = TrimLoopOut = TrimLoopType.Infinity;
            FrameFadeInOutLinked = true;
            foreach(var f in Frames)
            {
                f.FadeModeIn = f.FadeModeOut = FrameFadeMode.FadeOpacity;
            }
            SaveDataVersion = 3;
        }
    }
}


[Serializable]
public class MeshData : IDeepCopy<MeshData>
{
    [OptionalField]
    public SerializableTransform Transform = new SerializableTransform();

    [OptionalField]
    public int MaterialIndex = 0;
    public SerializableVector3[] vertices;
    public SerializableVector3[] normals;
    public SerializableVector2[] uvs;
    [OptionalField]
    public SerializableColor[] colors;
    public int[] triangles;

    [OnDeserializing]
    void FormatDataDeserialize(StreamingContext sc)
    {
        Transform = new SerializableTransform();
    }

    public MeshData DeepCopy()
    {
        MeshData result = new MeshData();
        result.colors = colors != null ? colors.DeepCopy() : null;
        result.MaterialIndex = MaterialIndex;
        result.normals = normals.DeepCopy();
        result.triangles = triangles.DeepCopy();
        result.uvs = uvs.DeepCopy();
        result.vertices = vertices.DeepCopy();
        result.Transform = Transform.DeepCopy();
        return result;
    }
}

public enum MeshShaderType
{
    Standard,
    Unlit
}

[Serializable]
public class MaterialData : IDeepCopy<MaterialData>
{
    public SerializableColor Diffuse = new SerializableColor(Color.white);
    public SerializableColor Specular = new SerializableColor(Color.black);
    public SerializableColor Emissive = new SerializableColor(Color.black);

    public byte[] DiffuseTex;
    public byte[] SpecularTex;
    public byte[] EmissiveTex;

    [OptionalField]
    public MeshShaderType ShaderType = MeshShaderType.Standard;

    [OptionalField]
    public ColorSpace ColorSpace = ColorSpace.Linear;

    public MaterialData DeepCopy()
    {
        MaterialData result = new MaterialData();
        result.ColorSpace = ColorSpace;
        result.Diffuse = Diffuse;
        result.DiffuseTex = DiffuseTex;
        result.Emissive = Emissive;
        result.EmissiveTex = EmissiveTex;
        result.ShaderType = ShaderType;
        result.Specular = Specular;
        result.SpecularTex = SpecularTex;
        return result;
    }
}



[Serializable]
public class StaticMeshData : PlayableData
{
    public List<MeshData> Frames = new List<MeshData>();

    [NonSerialized]
    public List<SerializableTransform> InstanceMap = new List<SerializableTransform>();

    [OptionalField]
    public List<int> SerializedInstanceMappings = new List<int>();

    [OptionalField]
    public List<MaterialData> Materials = new List<MaterialData>();

    [OptionalField]
    public List<AnimMeshTransform.MeshFrameTransformDataProxy> TransformProxies = new List<AnimMeshTransform.MeshFrameTransformDataProxy>();

    [OptionalField]
    public TransformTimelineData Timeline = new TransformTimelineData();

    public StaticMeshData()
    {
        Timeline.Frames.Add(new SerializableTransform());
        InstanceMap.Add(Timeline.Frames[0]);
        TransformProxies.Add(new AnimMeshTransform.MeshFrameTransformDataProxy());
    }

    public int FrameIndexOf(SerializableTransform instance)
    {
        return Timeline.Frames.FindIndex((t) => ReferenceEquals(t, instance));
    }

    [OnDeserialized]
    private void FormatDataDeserialize(StreamingContext sc)
    {
        if (SerializedInstanceMappings == null) SerializedInstanceMappings = new List<int>();
        if (TransformProxies == null) TransformProxies = new List<AnimMeshTransform.MeshFrameTransformDataProxy>();
        if (Timeline == null) Timeline = new TransformTimelineData();
        if (InstanceMap == null) InstanceMap = new List<SerializableTransform>();

        if (Timeline.Frames.Count == 0)
        {
            Timeline.Frames.Add(new SerializableTransform());
            InstanceMap.Add(Timeline.Frames[0]);
            TransformProxies.Add(new AnimMeshTransform.MeshFrameTransformDataProxy());
        }

        InstanceMap = new List<SerializableTransform>(SerializedInstanceMappings.Count);
        foreach (var map in SerializedInstanceMappings)
        {
            InstanceMap.Add(Timeline.Frames[map]);
        }


        if (InstanceMap.Count == 0)
        {
            for (int i = 0; i < Timeline.Frames.Count; i++)
            {
                InstanceMap.Add(Timeline.Frames[i]);
            }
        }

    }

    [OnSerializing]
    private void FormatDataSerialize(StreamingContext sc)
    {
        SerializedInstanceMappings.Clear();
        SerializedInstanceMappings.Capacity = InstanceMap.Count;

        foreach (var map in InstanceMap)
        {
            SerializedInstanceMappings.Add(this.FrameIndexOf(map));
        }
    }

    public override int GetFrameCount(float fps)
    {
        return InstanceMap.Count;
    }

    public override PlayableData DeepCopy()
    {
        StaticMeshData result = new StaticMeshData();
        PlayableData.CopyPlayableData(this, result);

        result.Materials = Materials.DeepCopy();
        result.Frames = Frames.DeepCopy();
        result.Timeline = Timeline.DeepCopy();

        SerializedInstanceMappings.Clear();
        SerializedInstanceMappings.Capacity = InstanceMap.Count;
        foreach (var map in InstanceMap)
        {
            SerializedInstanceMappings.Add(this.FrameIndexOf(map));
        }

        result.InstanceMap = new List<SerializableTransform>(SerializedInstanceMappings.Count);
        foreach (var map in SerializedInstanceMappings)
        {
            result.InstanceMap.Add(result.Timeline.Frames[map]);
        }

        result.TransformProxies = TransformProxies.DeepCopy();
        return result;
    }
}

[Serializable]
public class CameraData : PlayableData
{
    public AnimCam attachedCam { get { return base.attachedPlayable as AnimCam; } }

    public string FilmFormat = "35mm 16:9 Aperture (1.78:1)";
    public int LensIndex = 0;
    public int FStopIndex = 0;
    public float FocusDistance = 0.5f;
    public float RecordingTime = 0;
    [OptionalField] public TransformTimelineData Timeline = new TransformTimelineData();

    [OptionalField] public SerializableTransform CurrentShotOffset = new SerializableTransform();

    [OptionalField] public bool EnableDOF = true;
    [OptionalField] public bool EnableFog = false;
    [OptionalField] public float SmoothingFactor = 1.0f;
    [OptionalField] public bool Stereo = false;
    [OptionalField] public float StereoSeparation = 0.03f;

    [OnDeserialized]
    private void FormatDataDeserialize(StreamingContext sc)
    {
        if (Timeline == null) Timeline = new TransformTimelineData();
    }

    public override int GetFrameCount(float fps)
    {
        return Mathf.Max(1, Mathf.CeilToInt(RecordingTime * fps));
    }

    public override PlayableData DeepCopy()
    {
        CameraData result = new CameraData();
        PlayableData.CopyPlayableData(this, result);

        result.RecordingTime = RecordingTime;

        result.FilmFormat = FilmFormat;
        result.LensIndex = LensIndex;
        result.FocusDistance = FocusDistance;
        result.FStopIndex = FStopIndex;
        result.Timeline = Timeline.DeepCopy();
        result.CurrentShotOffset = CurrentShotOffset.DeepCopy();
        result.EnableDOF = EnableDOF;
        result.EnableFog = EnableFog;
        result.SmoothingFactor = SmoothingFactor;
        result.Stereo = Stereo;
        result.StereoSeparation = StereoSeparation;

        return result;
    }
}

[Serializable]
public class PuppetPointData : PlayableData
{
    [OptionalField]
    public SerializableColor Color = new SerializableColor(1, 1, 1, 1);
    public TransformTimelineData Timeline;

    public PuppetPoint attachedPoint { get { return base.attachedPlayable as PuppetPoint; } }

    public override PlayableData DeepCopy()
    {
        PuppetPointData result = new PuppetPointData();
        PlayableData.CopyPlayableData(this, result);

        result.Color = Color;
        result.name = name;
        result.Timeline = Timeline.DeepCopy();
        result.transform = transform.DeepCopy();
        result.FadeIn = FadeIn;
        result.FadeOut = FadeOut;

        return result;
    }
}


[Serializable]
public class TransformTimelineData : IDeepCopy<TransformTimelineData>
{
    public TransformTimelineData()
    {

    }

    public TransformTimelineData(TransformTimeline timeline)
    {
        FrameTimes = new List<float>(timeline.FrameTimes.ToArray());
        Frames = timeline.Frames.Select((v) => new SerializableTransform(v)).ToList();
    }

    public List<float> FrameTimes = new List<float>();
    public List<SerializableTransform> Frames = new List<SerializableTransform>();

    public TransformTimelineData DeepCopy()
    {
        var result = new TransformTimelineData();
        result.Frames = Frames.DeepCopy();
        result.FrameTimes = FrameTimes.DeepCopy();
        return result;
    }
}

[Serializable]
public class PuppetLinkData : PlayableData
{
    [OptionalField]
    public SerializableColor Color = new SerializableColor(1, 1, 1, 1);
    public int StartIndex, EndIndex;

    public PuppetLink attachedLink { get { return attachedObj as PuppetLink; } }

    public override PlayableData DeepCopy()
    {
        var result = new PuppetLinkData();
        PlayableData.CopyPlayableData(this, result);

        result.Color = Color;
        result.EndIndex = EndIndex;
        result.name = name;
        result.StartIndex = StartIndex;
        result.transform = transform.DeepCopy();
        result.FadeIn = FadeIn;
        result.FadeOut = FadeOut;

        return result;
    }
}

[Serializable]
public class PuppetFrameData : IDeepCopy<PuppetFrameData>
{
    [NonSerialized]
    public PuppetFrame attachedFrame;

    public PuppetFrameData DeepCopy()
    {
        return new PuppetFrameData();
    }
}

[Serializable]
public class PuppetData : PlayableData
{
    public AnimPuppet attachedPuppet { get { return base.attachedPlayable as AnimPuppet; } }

    public List<PuppetFrameData> Frames = new List<PuppetFrameData>();
    public List<PuppetPointData> Points = new List<PuppetPointData>();
    public List<PuppetLinkData> Links = new List<PuppetLinkData>();

    public override int GetFrameCount(float fps)
    {
        return Frames.Count;
    }

    public override PlayableData DeepCopy()
    {
        var result = new PuppetData();
        PlayableData.CopyPlayableData(this, result);

        result.transform = transform.DeepCopy();
        result.Frames = Frames.DeepCopy();
        result.Points = Points.DeepCopy();
        result.Links = Links.DeepCopy();
        return result;
    }
}


[Serializable]
public class AudioData : PlayableData
{
    public static float[] SILENCE = new float[1024];

    public AnimAudio attachedAudio { get { return base.attachedPlayable as AnimAudio; } }

    public bool Spatialize;

    [OptionalField] public bool filter;
    [OptionalField] public float cutOff;
    [OptionalField] public float resonance;

    public byte[] EncodedSamples;
    [OptionalField]
    public float[] EncodedWavSamples;
    [OptionalField]
    public int channels;
    [OptionalField]
    public int frequency;

    [OptionalField] public AudioDataPool.AudioPoolKey audioDataKey;

    public override int GetFrameCount(float fps)
    {
        return 1;
    }

    public override PlayableData DeepCopy()
    {
        var result = new AudioData();
        PlayableData.CopyPlayableData(this, result);

        result.Spatialize = Spatialize;
        result.EncodedSamples = EncodedSamples.DeepCopy();
        result.EncodedWavSamples = EncodedWavSamples.DeepCopy();
        result.channels = channels;
        result.frequency = frequency;
        //filter
        result.filter = filter;
        result.cutOff = cutOff;
        result.resonance = resonance;

        result.audioDataKey = audioDataKey.DeepCopy();

        return result;
    }
}

[Serializable]
public class UnityImportData : PlayableData
{
    public AnimUnityImport attachedUnityImport { get { return base.attachedPlayable as AnimUnityImport; } }

    public byte[] packageData;

    public override PlayableData DeepCopy()
    {
        var result = new UnityImportData();
        PlayableData.CopyPlayableData(this, result);
        result.packageData = packageData.DeepCopy();
        return result;
    }
}

[Serializable]
public class VideoData : PlayableData
{
    public AnimVideo attachedVideo { get { return base.attachedPlayable as AnimVideo; } }

    public string videoUrl;
    public StereoscopicLayout Layout;

    public override PlayableData DeepCopy()
    {
        var result = new VideoData();
        PlayableData.CopyPlayableData(this, result);
        result.videoUrl = videoUrl;
        result.Layout = Layout;
        return result;
    }
}

namespace AnimVR
{
    public enum StereoscopicLayout
    {
        None = 0,
        SideBySide = 1,
        OverUnder = 2,
    }

    [Serializable]
    public class SkyboxData : PlayableData
    {
        public AnimSkybox attachedSkybox { get { return base.attachedPlayable as AnimSkybox; } }

        public float distance = 1;
        public string videoUrl = null;
        public byte[] textureData = null;
        public SerializableColor color = new SerializableColor(1, 1, 1, 1);
        public float yRotation = 0;
        public StereoscopicLayout Layout;
        public int frameCount = 1;


        public override PlayableData DeepCopy()
        {
            var result = new SkyboxData();
            PlayableData.CopyPlayableData(this, result);
            result.videoUrl = videoUrl;
            result.textureData = textureData.DeepCopy();
            result.color = color;
            result.distance = distance;
            result.yRotation = yRotation;
            result.Layout = Layout;
            result.frameCount = frameCount;

            return result;
        }
    }
}

public enum PlayableLicenseType
{
    AllRightsReserved,
    Attribution,
    ShareAlike,
    NonCommercial
}

[Serializable]
public class PlayableAttributionInfo : IDeepCopy<PlayableAttributionInfo>
{
    public string Author;
    public string AttributionText;
    public PlayableLicenseType LicenseType;

    public PlayableAttributionInfo DeepCopy()
    {
        var result = new PlayableAttributionInfo()
        {
            Author = Author,
            AttributionText = AttributionText,
            LicenseType = LicenseType
        };
        return result;
    }
}

[System.Serializable]
public class
    PlayableData : IAnimData, IDeepCopy<PlayableData>
{
    [OptionalField]
    public int SaveDataVersion = 3;
    [OptionalField]
    public bool isVisible = true;
    [OptionalField]
    public float opacity = 1.0f;
    [OptionalField]
    public int IndexInSymbol = -1;
    [OptionalField]
    public string displayName = "Symbol";

    [OptionalField] public bool didChangeTrimLength = false;
  
    [OptionalField] public bool expandedInLayerList = true;
    [Obsolete("Use expandedInLayerList to synchronyze the expanded state between views.", true)]
    [OptionalField]
    public bool expandedInTrackList = true;

    public string name = "Symbol" + Guid.NewGuid();
    string IAnimData.name
    {
        get
        {
            return name;
        }

        set
        {
            name = value;
        }
    }

    public LoopType LoopType = LoopType.Loop;

    [OptionalField]
    public bool UseInOutLoop = false;
    [OptionalField]
    public LoopType LoopIn = LoopType.Loop;
    [OptionalField]
    public LoopType LoopOut = LoopType.Loop;

    [OptionalField]
    public TrimLoopType TrimLoopIn = TrimLoopType.Infinity;
    [OptionalField]
    public TrimLoopType TrimLoopOut = TrimLoopType.Infinity;

    [OptionalField]
    public float FadeIn;
    [OptionalField]
    public float FadeOut;

    [OptionalField]
    [Obsolete("Always use these values!", true)]
    public bool UseTrimIn; 
    [OptionalField]
    public int TrimIn;

    [OptionalField]
    [Obsolete("Always use these values!", true)]
    public bool UseTrimOut;
    [OptionalField]
    public int TrimOut;


    [OptionalField]
    public PlayableAttributionInfo AttributionInfo;

    public SerializableTransform transform = new SerializableTransform();

    [OptionalField(VersionAdded = 5)]
    public int FrameIndex = 0;

    [OptionalField] public int AbsoluteTimeOffset = 0;

    [OptionalField] public int ColorCorrectionTexture = -1;

    public MonoBehaviour attachedObj
    {
        get { return attachedPlayable; }
    }

    [NonSerialized]
    public AnimPlayable attachedPlayable;

    public SerializableTransform Trans { get { return transform; } }

    public void SetTransform(SerializableTransform transform)
    {
        this.transform = transform;
        if (attachedPlayable) transform.ApplyTo(attachedPlayable.transform);
    }

    public virtual int GetFrameCount(float fps)
    {
        return 1;
    }

    public virtual int GetLocalTrimStart(float fps)
    {
        return AbsoluteTimeOffset - TrimIn;
    }

    public virtual int GetLocalTrimEnd(float fps)
    {
        return AbsoluteTimeOffset + GetFrameCount(fps) + TrimOut;
    }

    [OnDeserializing]
    private void FormatDataDeserializing(StreamingContext sc)
    {
        IndexInSymbol = -1;
        isVisible = true;
        opacity = 1;
        UseInOutLoop = false;
        LoopIn = LoopType.Loop;
        LoopOut = LoopType.Loop;

        ColorCorrectionTexture = -1;
    }

    [OnDeserialized]
    private void FormatDataDeserialized(StreamingContext sc)
    {
        if (!UseInOutLoop)
        {
            LoopIn = LoopOut = LoopType;
        }

        if(SaveDataVersion == 0 && !(this is SymbolData) && !(this is TimeLineData))
        {
            TrimLoopIn = TrimLoopOut = TrimLoopType.Infinity;
            didChangeTrimLength = false;
            SaveDataVersion = 3;
        }
    }


    public static void CopyPlayableData(PlayableData source, PlayableData target)
    {
        target.displayName = source.displayName;
        target.FrameIndex = source.FrameIndex;
        target.IndexInSymbol = source.IndexInSymbol;
        target.isVisible = source.isVisible;
        target.LoopType = source.LoopType;
        target.UseInOutLoop = source.UseInOutLoop;
        target.LoopIn = source.LoopIn;
        target.LoopOut = source.LoopOut;
        target.FadeIn = source.FadeIn;
        target.FadeOut = source.FadeOut;

        target.name = source.name;
        target.opacity = source.opacity;
        target.transform = source.transform.DeepCopy();
        target.AbsoluteTimeOffset = source.AbsoluteTimeOffset;
        target.ColorCorrectionTexture = source.ColorCorrectionTexture;
        target.AttributionInfo = source.AttributionInfo != null ? source.AttributionInfo.DeepCopy() : null;

        target.didChangeTrimLength = source.didChangeTrimLength;
        target.TrimIn = source.TrimIn;
        target.TrimOut = source.TrimOut;

        target.TrimLoopIn = source.TrimLoopIn;
        target.TrimLoopOut = source.TrimLoopOut;
    }

    public virtual PlayableData DeepCopy()
    {
        throw new NotImplementedException();
    }
}

[System.Serializable]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
public class SymbolData : PlayableData
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    [OptionalField]
    [Obsolete("previewFrames is obsolete, symbols don't need them.", true)]
    public byte[][] previewFrames;

    [OptionalField]
    [Obsolete("GUID is obsolete, symbols don't need one.", true)]
    public Guid guid;

    [OptionalField]
    public int SelectedPlayable = 0;

    public List<TimeLineData> TimeLines = new List<TimeLineData>();

    [OptionalField(VersionAdded = 7)]
    public List<StaticMeshData> Meshes = new List<StaticMeshData>();

    [OptionalField(VersionAdded = 8)]
    public List<PuppetData> Puppets = new List<PuppetData>();

    [OptionalField(VersionAdded = 8)]
    public List<AudioData> Audios = new List<AudioData>();

    [OptionalField(VersionAdded = 9)]
    public List<CameraData> Cameras = new List<CameraData>();

    [OptionalField(VersionAdded = 10)]
    public List<SymbolData> Symbols = new List<SymbolData>();

    [OptionalField(VersionAdded = 11)]
    public List<UnityImportData> UnityImports = new List<UnityImportData>();

    [OptionalField(VersionAdded = 12)]
    public List<VideoData> Videos = new List<VideoData>();

    [OptionalField(VersionAdded = 13)]
    public List<SkyboxData> Skyboxes = new List<SkyboxData>();

    [NonSerialized]
    public List<PlayableData> Playables = new List<PlayableData>();

    [OnDeserialized]
    private void FormatDataDeserialize(StreamingContext sc)
    {
        Playables = new List<PlayableData>();

        if (Meshes == null) Meshes = new List<StaticMeshData>();
        if (Puppets == null) Puppets = new List<PuppetData>();
        if (Audios == null) Audios = new List<AudioData>();
        if (Cameras == null) Cameras = new List<CameraData>();
        if (Symbols == null) Symbols = new List<SymbolData>();
        if (UnityImports == null) UnityImports = new List<UnityImportData>();
        if (Videos == null) Videos = new List<VideoData>();
        if (Skyboxes == null) Skyboxes = new List<SkyboxData>();

        Playables.AddRange(Enumerable.Repeat<PlayableData>(null, 
            TimeLines.Count + Meshes.Count + Puppets.Count + 
            Audios.Count + Cameras.Count + Symbols.Count + 
            UnityImports.Count + Videos.Count + Skyboxes.Count));

        for (int i = 0; i < TimeLines.Count; i++)
        {
            Playables[TimeLines[i].IndexInSymbol == -1 ? i : TimeLines[i].IndexInSymbol] = TimeLines[i];
        }

        for (int i = 0; i < Meshes.Count; i++)
        {
            Playables[Meshes[i].IndexInSymbol] = Meshes[i];
        }

        for (int i = 0; i < Puppets.Count; i++)
        {
            Playables[Puppets[i].IndexInSymbol] = Puppets[i];
        }

        for (int i = 0; i < Audios.Count; i++)
        {
            Playables[Audios[i].IndexInSymbol] = Audios[i];
        }

        for (int i = 0; i < Cameras.Count; i++)
        {
            Playables[Cameras[i].IndexInSymbol] = Cameras[i];
        }

        for (int i = 0; i < Symbols.Count; i++)
        {
            Playables[Symbols[i].IndexInSymbol] = Symbols[i];
        }

        for (int i = 0; i < UnityImports.Count; i++)
        {
            Playables[UnityImports[i].IndexInSymbol] = UnityImports[i];
        }

        for (int i = 0; i < Videos.Count; i++)
        {
            Playables[Videos[i].IndexInSymbol] = Videos[i];
        }

        for (int i = 0; i < Skyboxes.Count; i++)
        {
            Playables[Skyboxes[i].IndexInSymbol] = Skyboxes[i];
        }

        for (int i = 0; i < Playables.Count; i++)
        {
            if (Playables[i].displayName == null)
            {
                Playables[i].displayName = "Layer " + i;
            }
        }

        if(SaveDataVersion == 0)
        {
            TrimLoopIn = (TrimLoopType)LoopIn;
            TrimLoopOut = (TrimLoopType)LoopOut;
            didChangeTrimLength = false;
            SaveDataVersion = 3;
        }
    }

    [OnSerializing]
    private void FormatDataSerialize(StreamingContext sc)
    {
        TimeLines.Clear();
        Meshes.Clear();
        Puppets.Clear();
        Audios.Clear();
        Cameras.Clear();
        Symbols.Clear();
        UnityImports.Clear();
        Videos.Clear();
        Skyboxes.Clear();

        for (int i = 0; i < Playables.Count; i++)
        {
            var data = Playables[i];
            data.IndexInSymbol = i;
            if (data is TimeLineData) TimeLines.Add(data as TimeLineData);
            else if (data is StaticMeshData) Meshes.Add(data as StaticMeshData);
            else if (data is PuppetData) Puppets.Add(data as PuppetData);
            else if (data is AudioData) Audios.Add(data as AudioData);
            else if (data is CameraData) Cameras.Add(data as CameraData);
            else if (data is SymbolData) Symbols.Add(data as SymbolData);
            else if (data is UnityImportData) UnityImports.Add(data as UnityImportData);
            else if (data is VideoData) Videos.Add(data as VideoData);
            else if (data is SkyboxData) Skyboxes.Add(data as SkyboxData);
        }
    }

    public AnimSymbol attachedSymbol { get { return base.attachedPlayable as AnimSymbol; } }


    public override int GetFrameCount(float fps)
    {
        return 1;
    }

    public override int GetLocalTrimStart(float fps)
    {
        if (didChangeTrimLength)
        {
            return AbsoluteTimeOffset - TrimIn;
        }
        else
        {
            int minClipStart = int.MaxValue;
            for (int i = 0; i < Playables.Count; i++)
            {
                var t = Playables[i];
                minClipStart = Mathf.Min(minClipStart, t.GetLocalTrimStart(fps));
            }

            return AbsoluteTimeOffset + (Playables.Count != 0 ? minClipStart : 0) - TrimIn;
        }
    }

    public override int GetLocalTrimEnd(float fps)
    {
        if (didChangeTrimLength)
        {
            return GetLocalTrimStart(fps) + 10 + TrimOut;
        }
        else
        {
            int lastPlayableEnd = int.MinValue;
            for (int i = 0; i < Playables.Count; i++)
            {
                var t = Playables[i];
                lastPlayableEnd = Mathf.Max(lastPlayableEnd, t.GetLocalTrimEnd(fps));
            }

            return AbsoluteTimeOffset + (Playables.Count == 0 ? 1 : lastPlayableEnd) + TrimOut;
        }
    }


    public override bool Equals(object obj)
    {
        SymbolData data = obj as SymbolData;
        if (data == null) return false;
        return this == data;
    }

    public static bool operator ==(SymbolData x, SymbolData y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;

        return x.name == y.name && x.LoopType == y.LoopType &&
               x.TimeLines.SequenceEqual(y.TimeLines) && x.transform == y.transform;
    }

    public static bool operator !=(SymbolData x, SymbolData y)
    {
        return !(x == y);
    }

    public void AddPlayable(int index, PlayableData data, CreateMode mode)
    {
        Playables.AddToList(ref index, data, mode);
    }

    public void RemovePlayable(PlayableData data)
    {
        Playables.Remove(data);
    }

    public override PlayableData DeepCopy()
    {
        SymbolData result = new SymbolData();
        PlayableData.CopyPlayableData(this, result);

        result.Playables = Playables.DeepCopy();
        result.SelectedPlayable = SelectedPlayable;
        result.expandedInLayerList = expandedInLayerList;

        return result;
    }

    public PlayableData FindPlayable(string path)
    {
        int firstSlashIndex = path.IndexOf('/');
        if (firstSlashIndex == -1) return null;

        string name = path.Substring(0, firstSlashIndex);
        string remaining = path.Substring(firstSlashIndex + 1, path.Length - firstSlashIndex - 1);
        foreach (var playable in Playables)
        {
            if (playable.displayName == name)
            {
                if (remaining.Length == 0) return playable;
                else if (playable is SymbolData) return (playable as SymbolData).FindPlayable(remaining);
            }
        }

        return null;
    }

    public bool PathOf(PlayableData playable, ref string path)
    {
        if ( ReferenceEquals(playable,this))
        {
            path = path + playable.displayName + "/";
            return true;
        }

        foreach (var child in Playables)
        {
            if (child == playable)
            {
                path = path + child.displayName + "/";
                return true;
            }

            if (child is SymbolData)
            {
                var childSymbol = child as SymbolData;
                var tmpPath = path + child.displayName + "/";
                if (childSymbol.PathOf(playable, ref tmpPath))
                {
                    path = tmpPath;
                    return true;
                }
            }
        }

        return false;
    }

    public IEnumerable<PlayableData> EnumeratePlayables()
    {
        for (int i = 0; i < Playables.Count; i++)
        {
            yield return Playables[i];
            if (Playables[i] is SymbolData)
            {
                var s = (SymbolData)Playables[i];
                foreach (var child in s.EnumeratePlayables())
                {
                    yield return child;
                }
            }
        }
    }
}

[Serializable]
public struct StorySettings : IDeepCopy<StorySettings>
{
    public bool loop;
    public float maxPlaytime;
    public SerializableTransform stageTransform;

    public StorySettings DeepCopy()
    {
        var result = new StorySettings();
        result.loop = loop;
        result.maxPlaytime = maxPlaytime;
        result.stageTransform = stageTransform == null ? null : stageTransform.DeepCopy();
        return result;
    }
}


namespace AnimVR
{
    public class DownsampledWaveform
    {
        public int readyBuckets = 0;
        public float[] maxValues;
        public float[] minValues;

        public ComputeBuffer sampleBufferMax;
        public ComputeBuffer sampleBufferMin;
        public Mesh mesh;
        public Material mat;
             
        private int lastReadyBuckets = 0;
        public void UpdateData()
        {
            if (lastReadyBuckets == readyBuckets) return;

            sampleBufferMax.SetData(maxValues, lastReadyBuckets, lastReadyBuckets, readyBuckets - lastReadyBuckets);
            sampleBufferMin.SetData(minValues, lastReadyBuckets, lastReadyBuckets, readyBuckets - lastReadyBuckets);
            mat.SetFloat("_ValueCount", readyBuckets);

            lastReadyBuckets = readyBuckets;
        }

        public void Cleanup()
        {
            if (sampleBufferMax != null) sampleBufferMax.Release();
            if (sampleBufferMin != null) sampleBufferMin.Release();

            GameObject.Destroy(mesh);
            GameObject.Destroy(mat);
        }
    }

    public static class RenderWaveformUtils
    {
        public static DownsampledWaveform InitializeResult(float[] samples, int channels, int sampleRate, int bucketsPerSecond)
        {
            DownsampledWaveform result = new DownsampledWaveform();

            float seconds = (float)(samples.Length / channels) / sampleRate;
            int buckets = (int)(seconds * bucketsPerSecond);

            result.maxValues = new float[buckets];
            result.minValues = new float[buckets];
            result.readyBuckets = 0;

            var mesh = new Mesh();
            mesh.vertices = new Vector3[result.maxValues.Length];

            int[] indices = new int[result.maxValues.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;

            mesh.SetIndices(indices, MeshTopology.LineStrip, 0, false);
            mesh.bounds = new Bounds(new Vector3(0.5f, 0, 0), Vector3.one);

            result.mesh = mesh;

            result.sampleBufferMax = new ComputeBuffer(result.maxValues.Length, sizeof(float), ComputeBufferType.Default);
            result.sampleBufferMin = new ComputeBuffer(result.maxValues.Length, sizeof(float), ComputeBufferType.Default);

            result.mat = new Material(Shader.Find("AnimVR/Waveform"));

            result.mat.SetBuffer("_MaxValues", result.sampleBufferMax);
            result.mat.SetBuffer("_MinValues", result.sampleBufferMin);
            result.mat.SetFloat("_TotalValueCount", result.sampleBufferMin.count);
            result.mat.color = new Color(0, 181.0f/255, 1, 1);

            result.UpdateData();

            return result;
        }

        public static IEnumerator DownsampleWaveform(float[] samples, DownsampledWaveform result)
        {
            int samplesPerBucket = samples.Length / result.maxValues.Length;

            for (int i = 0; i < result.maxValues.Length; i++)
            {
                float bucketMax = float.MinValue;
                float bucketMin = float.MaxValue;

                for (int s = 0; s < samplesPerBucket; s++)
                {
                    int sampleIndex = i * samplesPerBucket + s;
                    float sample = samples[sampleIndex];

                    bucketMax = Mathf.Max(bucketMax, sample);
                    bucketMin = Mathf.Min(bucketMin, sample);
                }

                result.maxValues[i] = bucketMax;
                result.minValues[i] = bucketMin;
                result.readyBuckets = i;

                if ((i + 1) % 10 == 0)
                {
                    result.UpdateData();
                    yield return null;
                }
            }

            result.UpdateData();
        }
    }
}

[Serializable]
public class AudioDataPool : IDeepCopy<AudioDataPool>
{
    [Serializable]
    public struct AudioPoolData : IDeepCopy<AudioPoolData>
    {
        public int channels;
        public int frequency;
        public byte[] data;
        public AudioStorageType storageType;

        public AudioPoolData DeepCopy()
        {
            return new AudioPoolData()
            {
                channels = channels,
                frequency = frequency,
                data = data.DeepCopy(),
                storageType = storageType
            };
        }
    }
    [Serializable]
    public struct AudioPoolKey : IDeepCopy<AudioPoolKey>
    {
        public byte[] hash;
        public int length;
        public AudioPoolKey DeepCopy()
        {
            AudioPoolKey result = new AudioPoolKey()
            {
                hash = hash.DeepCopy(),
                length = length
            };
            return result;
        }

        public static bool operator ==(AudioPoolKey x, AudioPoolKey y)
        {
            return x.length == y.length && ((x.hash == null && y.hash == null) || x.hash.SequenceEqual(y.hash));
        }

        public static bool operator !=(AudioPoolKey x, AudioPoolKey y)
        {
            return !(x == y);
        }

        public bool Equals(AudioPoolKey other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is AudioPoolKey && Equals((AudioPoolKey)obj);
        }

        public int GetHashCode<T>(T[] array)
        {
            var elementComparer = EqualityComparer<T>.Default;

            unchecked
            {
                if (array == null)
                {
                    return 0;
                }
                int hash = 17;
                foreach (T element in array)
                {
                    hash = hash * 31 + elementComparer.GetHashCode(element);
                }
                return hash;
            }
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return  (GetHashCode(hash) * 397) ^ length;
            }
        }
    }

    [Serializable]
    public struct AudioPoolEntry<DataType>
    {
        public AudioPoolKey key;
        public DataType data;
        public AudioPoolEntry<DataType> DeepCopy()
        {
            AudioPoolEntry<DataType> result = new AudioPoolEntry<DataType>();
            result.key = key.DeepCopy();
            if (data is IDeepCopy<DataType>)
            {
                result.data = (data as IDeepCopy<DataType>).DeepCopy();
            }
            else
            {
                result.data = data;
            }
            return result;
        }
    }

    public List<AudioPoolEntry<AudioPoolData>> poolEntries = new List<AudioPoolEntry<AudioPoolData>>();


    [NonSerialized]
    public List<AudioPoolEntry<AudioClip>> runtimePoolEntries = new List<AudioPoolEntry<AudioClip>>();

    [NonSerialized]
    public List<AudioPoolEntry<DownsampledWaveform>> renderedWaveforms = new List<AudioPoolEntry<DownsampledWaveform>>();

    [OnDeserializing]
    void FormatDataDeserializing(StreamingContext sc)
    {
        runtimePoolEntries = new List<AudioPoolEntry<AudioClip>>();
        renderedWaveforms = new List<AudioPoolEntry<DownsampledWaveform>>();
    }

    public bool PoolContainsKey(AudioPoolKey key)
    {
        return poolEntries.FindIndex(e => e.key == key) != -1;
    }

    public class RetrieveWaveformResult
    {
        public DownsampledWaveform data;
    }

    public IEnumerator RetrievWaveformFromPoolAsync(AudioPoolKey key, RetrieveWaveformResult result)
    {
        int waveformIndex = renderedWaveforms.FindIndex(e => e.key == key);
        if (waveformIndex != -1)
        {
            result.data = renderedWaveforms[waveformIndex].data;
            yield break;
        }

        var clip = RetrieveClipFromPool(key);
        if (clip == null) yield break;

        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        result.data = RenderWaveformUtils.InitializeResult(samples, clip.channels, clip.frequency, 100);

        renderedWaveforms.Add(new AudioPoolEntry<DownsampledWaveform>() { data = result.data, key = key });

        yield return RenderWaveformUtils.DownsampleWaveform(samples, result.data);
    }

    public AudioClip RetrieveClipFromPool(AudioPoolKey key)
    {
        int runtimeIndex = runtimePoolEntries.FindIndex(e => e.key == key);
        int dataIndex = poolEntries.FindIndex(e => e.key == key);

        if (runtimeIndex != -1)
        {
            var clip = runtimePoolEntries[runtimeIndex].data;
            if (dataIndex == -1)
            {
                var samples = new float[clip.samples * clip.channels];
                clip.GetData(samples, 0);
                AudioStorageType storageType;
                var encodedSamples = AudioDataUtils.EncodeSamples(samples, clip.channels, clip.frequency, out storageType);
                poolEntries.Add(new AudioPoolEntry<AudioPoolData>()
                {
                    key = key,
                    data = new AudioPoolData()
                    {
                        channels = clip.channels,
                        frequency = clip.frequency,
                        data = encodedSamples,
                        storageType = storageType
                    }
                });
            }
            return clip;
        }

        if (dataIndex == -1) return null;

        var data = poolEntries[dataIndex].data;

        var result = AudioDataUtils.DecodeToClip(data.data, data.channels, data.frequency, data.storageType);

        runtimePoolEntries.Add(new AudioPoolEntry<AudioClip>() { data = result, key = key });
        return result;
    }

    public AudioPoolKey AddWavToPool(float[] samples, int channels, int frequency)
    {
        HashAlgorithm ha = SHA1.Create();
        AudioStorageType storageType;
        var encodedSamples = AudioDataUtils.EncodeSamples(samples, channels, frequency, out storageType);

        AudioPoolKey key = new AudioPoolKey();
        key.length = encodedSamples.Length;
        key.hash = ha.ComputeHash(encodedSamples);

        var audioClip = RetrieveClipFromPool(key);
        if (audioClip == null)
        {
            poolEntries.Add(new AudioPoolEntry<AudioPoolData>()
            {
                key = key,
                data = new AudioPoolData()
                {
                    channels = channels,
                    frequency = frequency,
                    data = encodedSamples,
                    storageType = storageType
                }
            });
        }

        return key;
    }

    public class AddWavToPoolResult
    {
        public AudioPoolKey key;
    }

    public IEnumerator AddWavToPoolAsync(float[] samples, int channels, int frequency, AddWavToPoolResult result)
    {
        HashAlgorithm ha = SHA1.Create();
        AudioStorageType storageType = AudioStorageType.Raw;
        byte[] encodedSamples = null;
        AudioPoolKey key = new AudioPoolKey();

        Thread encodeThread = new Thread(() =>
        {
            encodedSamples = AudioDataUtils.EncodeSamples(samples, channels, frequency, out storageType);

            key.length = encodedSamples.Length;
            key.hash = ha.ComputeHash(encodedSamples);
        });

        encodeThread.Start();

        while (encodeThread.ThreadState != System.Threading.ThreadState.Stopped) yield return null;

        encodeThread.Join();

        if(encodedSamples == null)
        {
            throw new Exception("Failed to add wave to pool!");
        }

        var boolHasKey = PoolContainsKey(key);
        if (!boolHasKey)
        {
            poolEntries.Add(new AudioPoolEntry<AudioPoolData>()
            {
                key = key,
                data = new AudioPoolData()
                {
                    channels = channels,
                    frequency = frequency,
                    data = encodedSamples,
                    storageType = storageType
                }
            });
        }

        result.key = key;
    }

    public AudioPoolKey AddMp3ToPool(byte[] encodedSamples)
    {
        int channels, frequency;
        var samples = AudioDataUtils.DecodeSamplesFromMp3(encodedSamples, out channels, out frequency);
        return AddWavToPool(samples, channels, frequency);
    }

    public void Merge(AudioDataPool other)
    {
        if (other == null) return;
        foreach (var entry in other.poolEntries)
        {
            int index = poolEntries.FindIndex(e => e.key == entry.key);
            if (index == -1) poolEntries.Add(entry);
        }
    }

    public void KeepKeys(List<AudioPoolKey> keys)
    {
        poolEntries = poolEntries.Where(e => keys.Contains(e.key)).ToList();
    }

    public AudioDataPool DeepCopy()
    {
        AudioDataPool result = new AudioDataPool();
        result.poolEntries = poolEntries.DeepCopy();
        result.runtimePoolEntries = runtimePoolEntries.DeepCopy();
        return result;
    }
}

namespace AnimVR
{
    [Serializable]
    public class GlobalSettings : IDeepCopy<GlobalSettings>
    {
        public List<int> FpsPresets = new List<int>() { 6, 8, 12, 24, 25, 30, 60 };
        public SerializableColor BackgroundColor;
        public bool ShowFloor = true;

        public GlobalSettings DeepCopy()
        {
            var result = new GlobalSettings();
            result.FpsPresets = FpsPresets.DeepCopy();
            result.BackgroundColor = BackgroundColor;
            result.ShowFloor = ShowFloor;
            return result;
        }
    }
}

[Serializable]
public class WorkspaceSettings : IDeepCopy<WorkspaceSettings>
{
    public bool ShowFloor = true;
    public bool ShowBigScreen = false;
    public SerializableVector3 BigScreenPos = new Vector3(2.43f, 3.574f, 7.21f);
    public float MasterVolume = 1;
    public bool MuteSound = false;
    public bool ShowGizmos = true;
    public float BrushSmoothAmount = 0;
    public float AnimBrushLength = 12;
    public bool EnableAmbientOcclusion = false;
    public float FPS = 24;

    public WorkspaceSettings DeepCopy()
    {
        var result = new WorkspaceSettings();
        result.ShowFloor = ShowFloor;
        result.ShowBigScreen = ShowBigScreen;
        result.BigScreenPos = BigScreenPos;
        result.MasterVolume = MasterVolume;
        result.MuteSound = MuteSound;
        result.ShowGizmos = ShowGizmos;
        result.BrushSmoothAmount = BrushSmoothAmount;
        result.AnimBrushLength = AnimBrushLength;
        result.EnableAmbientOcclusion = EnableAmbientOcclusion;
        result.FPS = FPS;

        return result;
    }
}

[System.Serializable]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
public class StageData : IAnimData, IDeepCopy<StageData>
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    [OptionalField(VersionAdded = 2)]
    public int SaveDataVersion = 3;

    [OptionalField(VersionAdded = 5)]
    [Obsolete("Use workspace settings instead.", true)]
    public bool floorOn = true;

    [OptionalField]
    public WorkspaceSettings workspaceSettings = new WorkspaceSettings();

    [OptionalField]
    public int SelectedSymbol = 0;

    [OptionalField] public bool AmbientOcclusion = false;

    [OptionalField] public bool ExportedAsStory = false;
    [OptionalField] public StorySettings StorySetting;

    [OptionalField] public AudioDataPool AudioDataPool = new AudioDataPool();

    public byte[][] previewFrames;
    public string name = "Stage";

    [NonSerialized] public PlayableData activePlayable;
    [NonSerialized]
    [Obsolete("We always loop around the active playable if that is enabled.", true)]
    public PlayableData loopAroundPlayable;

    [OptionalField]
    public string activePlayablePath;
    [OptionalField]
    [Obsolete("We always loop around the active playable if that is enabled.", true)]
    public string loopAroundPlayablePath;

    string IAnimData.name
    {
        get
        {
            return name;
        }

        set
        {
            name = value;
        }
    }

    public Guid guid = Guid.Empty;
    public float fps = 24;

    [OptionalField] public int timelineLength = 120;
    [OptionalField] public int playheadPosition = 0;
    [OptionalField] public SerializableVector2 timelineWindow = new SerializableVector2(0, 120);
    [OptionalField] public int loopPointerStart = 0;
    [OptionalField] public int loopPointerEnd = 120;
    [OptionalField] public bool useLoopPointers = true;
    [OptionalField] public bool loopAroundClip = false;
    [OptionalField] public bool keepAbsoluteTimeInEditMode = false;

    public List<SymbolData> Symbols = new List<SymbolData>();
    public SerializableTransform transform = new SerializableTransform();
    public SerializableColor backgroundColor = new SerializableColor();
    public SerializableTransform Trans { get { return transform; } }

    [OptionalField]
    public List<byte[]> ColorLUTs = new List<byte[]>();

    public MonoBehaviour attachedObj
    {
        get { return attachedStage; }
    }

    [NonSerialized]
    public AnimStage attachedStage;

    [NonSerialized]
    public string filename;

    public void SetTransform(SerializableTransform transform)
    {
        this.transform = transform.DeepCopy();
        if (attachedStage) transform.ApplyTo(attachedStage.transform);
    }

    public List<PlayableAttributionInfo> CollectAttributionInfo()
    {
        List<PlayableAttributionInfo> result = new List<PlayableAttributionInfo>();

        foreach(var symbol in Symbols)
        {
            foreach(var playable in symbol.EnumeratePlayables())
            {
                if (playable.AttributionInfo != null && 
                    result.FindIndex((a) => a.AttributionText == playable.AttributionInfo.AttributionText) == -1)
                {
                    result.Add(playable.AttributionInfo);
                }
            }
        }

        return result;
    }

    [OnSerializing]
    private void FormatDataSerializing(StreamingContext sc)
    {
        List<AudioDataPool.AudioPoolKey> usedKeys = new List<AudioDataPool.AudioPoolKey>();
        foreach (var audioData in Symbols[0].EnumeratePlayables().OfType<AudioData>())
        {
            usedKeys.Add(audioData.audioDataKey);
        }

        AudioDataPool.KeepKeys(usedKeys);

        activePlayablePath = activePlayable == null ? "" : PathOf(activePlayable);
    }

    [OnDeserializing]
    private void FormatDataDeserializing(StreamingContext sc)
    {
        fps = 24;
        SelectedSymbol = 0;
        AudioDataPool = null;
        ColorLUTs = new List<byte[]>();
    }

    [OnDeserialized]
    private void FormatDataDeserialized(StreamingContext sc)
    {
        if (fps == 0) fps = 24;
        if (timelineLength == 0) timelineLength = 120;
        if (timelineWindow.x == 0 && timelineWindow.y <= 1) timelineWindow = new SerializableVector2(0, 120);
        if (loopPointerEnd == 0) loopPointerEnd = (int)timelineWindow.y;
        if (workspaceSettings == null) workspaceSettings = new WorkspaceSettings() { ShowFloor = false };
        if(workspaceSettings.BigScreenPos.V3.magnitude < 4) workspaceSettings.BigScreenPos = new Vector3(2.43f, 3.574f, 7.21f);

        if (AudioDataPool == null)
        {
            AudioDataPool = new AudioDataPool();

            if (Symbols.Count > 0)
            {
                foreach (var audioData in Symbols[0].EnumeratePlayables().OfType<AudioData>())
                {
                    Debug.Log("Converting " + audioData.displayName + " to use the audio data pool.");
                    audioData.audioDataKey = audioData.EncodedWavSamples != null
                        ? AudioDataPool.AddWavToPool(audioData.EncodedWavSamples, audioData.channels, audioData.frequency)
                        : AudioDataPool.AddMp3ToPool(audioData.EncodedSamples);

                    audioData.EncodedWavSamples = null;
                    audioData.EncodedSamples = null;
                }
            }
        }

        if (!string.IsNullOrEmpty(activePlayablePath))
        {
            activePlayable = FindPlayable(activePlayablePath);
        }
    }

    public PlayableData FindPlayable(string path)
    {
        return Symbols[0].FindPlayable(path);
    }

    public string PathOf(PlayableData playable)
    {
        string result = "";
        Symbols[0].PathOf(playable, ref result);
        return result;
    }

    public override bool Equals(object obj)
    {
        StageData stageData = obj as StageData;
        if (stageData == null) return false;
        return this == stageData;
    }

    public StageData DeepCopy()
    {
        StageData result = new StageData();
        result.backgroundColor = backgroundColor;
        result.filename = filename;
        result.workspaceSettings = workspaceSettings.DeepCopy();
        result.fps = fps;
        result.guid = guid;
        result.name = name;
        result.previewFrames = previewFrames != null ? (byte[][])previewFrames.Clone() : null;
        result.SaveDataVersion = SaveDataVersion;
        result.SelectedSymbol = SelectedSymbol;
        result.Symbols = Symbols.DeepCopy();
        result.transform = transform.DeepCopy();

        result.timelineLength = timelineLength;
        result.playheadPosition = playheadPosition;
        result.timelineWindow = timelineWindow;
        result.loopPointerStart = loopPointerStart;
        result.loopPointerEnd = loopPointerEnd;
        result.loopAroundClip = loopAroundClip;
        result.useLoopPointers = useLoopPointers;
        result.keepAbsoluteTimeInEditMode = keepAbsoluteTimeInEditMode;
        result.StorySetting = StorySetting.DeepCopy();
        result.ExportedAsStory = ExportedAsStory;
        result.AudioDataPool = AudioDataPool.DeepCopy();

        result.activePlayable = activePlayable;
        result.activePlayablePath = activePlayablePath;

        result.ColorLUTs = ColorLUTs.DeepCopy();

        return result;
    }

    public static bool operator ==(StageData x, StageData y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) && ReferenceEquals(y, null)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;

        return ((x.previewFrames == null && y.previewFrames == null) || x.previewFrames.SequenceEqual(y.previewFrames)) &&
               x.name == y.name && x.guid == y.guid && x.fps == y.fps &&
               x.Symbols.SequenceEqual(y.Symbols) && x.transform == y.transform && x.backgroundColor == y.backgroundColor;
    }

    public static bool operator !=(StageData x, StageData y)
    {
        return !(x == y);
    }

}

[System.Serializable]
[ExecuteInEditMode]
public class AnimData : Singleton<AnimData>
{
    public delegate void OnChanged();
    public event OnChanged onChanged;

    public static byte[] whiteTextureData;

    public void Changed()
    {
        if (onChanged != null)
            onChanged();
    }

    [NonSerialized]
    public List<StageData> Stages = new List<StageData>();

    public StageData CreateStageData()
    {
        StageData stageData = new StageData();
        Stages.Add(stageData);
        Changed();
        return stageData;
    }

    public SymbolData CreateSymbolData(CreateMode createMode, int index, StageData activeStageData)
    {
        SymbolData symbolData = new SymbolData();

        activeStageData.Symbols.AddToList(ref index, symbolData, createMode);

        Changed();
        return symbolData;
    }

    public FrameData CreateFrameData(CreateMode createMode, int index, TimeLineData activeTimelineData, bool isInstance = false)
    {
        FrameData frameData = new FrameData();
        activeTimelineData.Frames.AddToList(ref index, frameData, createMode);
        frameData.isInstance = isInstance;
        Changed();
        return frameData;
    }

    public LineData CreateLineData(CreateMode createMode, int index, FrameData activeFrameData)
    {
        LineData lineData = new LineData();

        activeFrameData.Lines.AddToList(ref index, lineData, createMode);

        Changed();
        return lineData;
    }

    public void UpdateLineData(LineData activeLineData, Vector3 point, Quaternion rotation, float width, Color color, float light, BrushType brushtype, BrushMode brushMode, Quaternion camRotation)
    {
        var p = new SerializableVector3();
        p.V3 = point;
        var q = new SerializableQuaternion();
        q.Q = rotation;
        var c = new SerializableColor();
        c.C = color;
        activeLineData.Points.Add(p);
        activeLineData.rotations.Add(q);
        activeLineData.widths.Add(width);
        activeLineData.colors.Add(c);
        activeLineData.brushType = brushtype;
        activeLineData.brushMode = brushMode;

        var camRot = new SerializableQuaternion();
        camRot.Q = camRotation;
        activeLineData.cameraOrientations.Add(camRot);
        activeLineData.light.Add(light);
    }

    public void InsertPoint(LineData activeLineData, Vector3 point, Quaternion rotation, float width, Color color, float light, BrushType brushtype, BrushMode brushMode, Quaternion camRotation, int index)
    {
        var p = new SerializableVector3();
        p.V3 = point;
        var q = new SerializableQuaternion();
        q.Q = rotation;
        var c = new SerializableColor();
        c.C = color;
        activeLineData.Points.Insert(index, p);
        activeLineData.rotations.Insert(index, q);
        activeLineData.widths.Insert(index, width);
        activeLineData.colors.Insert(index, c);
        activeLineData.brushType = brushtype;
        activeLineData.brushMode = brushMode;

        var camRot = new SerializableQuaternion();
        camRot.Q = camRotation;
        activeLineData.cameraOrientations.Insert(index, camRot);
        activeLineData.light.Insert(index, light);
    }

    public void UpdateBGColor(Color color)
    {
        var c = new SerializableColor();
        c.C = color;
        Stages[0].backgroundColor = c;
    }

    public Color CurrentBackgroundColor()
    {
        return Stages[0].backgroundColor.C;
    }


    public T DeepCopy<T>(T t) where T : IDeepCopy<T>
    {
        return AnimData.DeepCopyS(t);
    }

    public static T DeepCopyS<T>(T t) where T : IDeepCopy<T>
    {
        return t.DeepCopy();
    }

    public static PreviewAnimation LoadPreviewFromFile(string filepath)
    {
        PreviewAnimation anim = new PreviewAnimation();
        anim.filename = filepath;
        try
        {
            using (Stream stream = new MemoryStream())
            {
                using (ZipFile archive = ZipFile.Read(filepath))
                {
                    archive.ParallelDeflateThreshold = -1;
                    if (!archive.ContainsEntry("preview"))
                    {
                        return null;
                    }
                    var entry = archive["preview"];
                    entry.Extract(stream);
                }

                stream.Seek(0, SeekOrigin.Begin);
                BinaryFormatter bf = new BinaryFormatter();
                anim.previewFrames = (byte[][])bf.Deserialize(stream);
            }
        }
        catch (Exception _)
        {
            Debug.Log("Error loading zip file preview: " + filepath + "\n" + _.Message + "\n" + (_.InnerException != null ? _.InnerException.Message : ""));
            Debug.Log("Trying old file format!");

            try
            {
                using (FileStream inStream = new FileStream(filepath, FileMode.Open))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    var stage = (StageData)bf.Deserialize(inStream);
                    anim.previewFrames = stage.previewFrames;
                }
            }
            catch (Exception e)
            {
                Debug.Log("Error loading file preview: " + filepath + "\n" + e.Message);
                return null;
            }
        }

        return anim;
    }

    sealed class Unity20171Fixer : SerializationBinder
    {
        Regex match = new Regex(@"UnityEngine\.(.*)");
        public override Type BindToType(string assemblyName, string typeName)
        {

            if (!match.IsMatch(assemblyName)) return Type.GetType(String.Format("{0}, {1}", typeName, assemblyName));

            Debug.Log(typeName);
            return Type.GetType(String.Format("{0}, {1}", typeName, "UnityEngine"));
        }
    }

    public static void UpgradeDataFromVersion2(StageData stageData)
    {
        stageData.SaveDataVersion = 3;

    }

    public static StageData LoadFromMemory(byte[] memory)
    {
        StageData stage = null;
        try
        {
            using (Stream inStream = new MemoryStream(memory))
            using (Stream stream = new MemoryStream())
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                using (ZipFile archive = ZipFile.Read(inStream))
                {
                    archive.ParallelDeflateThreshold = -1;
                    if (!archive.ContainsEntry("stage"))
                    {
                        return null;
                    }
                    var entry = archive["stage"];
                    entry.Extract(stream);
                }

                var timeExtract = stopwatch.ElapsedMilliseconds;

                stream.Seek(0, SeekOrigin.Begin);
                BinaryFormatter bf = new BinaryFormatter();

#if UNITY_2017_2_OR_NEWER
                stage = (StageData)bf.Deserialize(stream);
#else
                bf.Binder = new Unity20171Fixer();
                stage = (StageData)bf.Deserialize(stream);
#endif
                stopwatch.Stop();

                if (stage.SaveDataVersion == 2)
                {
                    UpgradeDataFromVersion2(stage);
                }
            }
        }
        catch (Exception _)
        {
            Debug.Log(_);
            Debug.Log("Error loading zip file preview: "  + "\n" + _.Message + "\n" + (_.InnerException != null ? _.InnerException.Message : ""));
            Debug.Log("Trying old file format!");
        }

        stage.filename = "Memory";
        return stage;
    }

    public static StageData LoadFromFile(string filepath)
    {
        StageData stage = null;
        try
        {
            using (Stream stream = new MemoryStream())
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                using (ZipFile archive = ZipFile.Read(filepath))
                {
                    archive.ParallelDeflateThreshold = -1;
                    if (!archive.ContainsEntry("stage"))
                    {
                        return null;
                    }
                    var entry = archive["stage"];
                    entry.Extract(stream);
                }

                var timeExtract = stopwatch.ElapsedMilliseconds;

                stream.Seek(0, SeekOrigin.Begin);
                BinaryFormatter bf = new BinaryFormatter();

#if UNITY_2017_2_OR_NEWER
                stage = (StageData)bf.Deserialize(stream);
#else
                bf.Binder = new Unity20171Fixer();
                stage = (StageData)bf.Deserialize(stream);
#endif
                stopwatch.Stop();

                if(stage.SaveDataVersion == 2)
                {
                    UpgradeDataFromVersion2(stage);
                }
            }
        }
        catch (Exception _)
        {
            Debug.Log(_);
            Debug.Log("Error loading zip file preview: " + filepath + "\n" + _.Message + "\n" + (_.InnerException != null ? _.InnerException.Message : ""));
            Debug.Log("Trying old file format!");
            try
            {
                using (FileStream inStream = new FileStream(filepath, FileMode.Open))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    stage = (StageData)bf.Deserialize(inStream);
                }
            }
            catch (Exception e)
            {
                Debug.Log("Error loading stage file: " + filepath + "\n" + e.Message);
            }
        }

        stage.filename = filepath;
        return stage;
    }

    public static void WriteToFile(string filepath, StageData stage)
    {
        try
        {
            using (FileStream output = new FileStream(filepath, FileMode.Create))
            {
                try
                {
                    using (ICSharpCode.SharpZipLib.Zip.ZipOutputStream s = new ICSharpCode.SharpZipLib.Zip.ZipOutputStream(output))
                    {
                        s.IsStreamOwner = false;
                        s.SetLevel(9); // 0 - store only to 9 - means best compression

                        using (MemoryStream prevStream = new MemoryStream())
                        {
                            var bf = new BinaryFormatter();
                            var previewFrames = stage.previewFrames;
                            if (previewFrames == null)
                            {
                                previewFrames = new byte[1][];
                                previewFrames[0] = Texture2D.whiteTexture.EncodeToPNG();
                            }

                            bf.Serialize(prevStream, previewFrames);
                            prevStream.Seek(0, SeekOrigin.Begin);

                            var entry = new ICSharpCode.SharpZipLib.Zip.ZipEntry("preview");
                            s.PutNextEntry(entry);
                            s.Write(prevStream.ToArray(), 0, (int)prevStream.Length);
                            s.CloseEntry();
                        }

                        using (MemoryStream stageStream = new MemoryStream())
                        {
                            var bf = new BinaryFormatter();
                            bf.Serialize(stageStream, stage);
                            stageStream.Seek(0, SeekOrigin.Begin);

                            var entry = new ICSharpCode.SharpZipLib.Zip.ZipEntry("stage");
                            s.PutNextEntry(entry);
                            s.Write(stageStream.ToArray(), 0, (int)stageStream.Length);
                            s.CloseEntry();
                        }

                        s.Finish();
                        s.Flush();
                        s.Close();
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Error writing zip file: " + filepath + "\n" + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Debug.Log("Error saving stage file: " + filepath + "\n" + e.Message);
        }
    }


    public static byte[] WriteToMemory(StageData stage)
    {
        try
        {
            using (MemoryStream output = new MemoryStream())
            {
                try
                {
                    using (ICSharpCode.SharpZipLib.Zip.ZipOutputStream s = new ICSharpCode.SharpZipLib.Zip.ZipOutputStream(output))
                    {
                        s.IsStreamOwner = false;
                        s.SetLevel(9); // 0 - store only to 9 - means best compression

                        using (MemoryStream prevStream = new MemoryStream())
                        {
                            var bf = new BinaryFormatter();
                            var previewFrames = stage.previewFrames;
                            if (previewFrames == null)
                            {
                                previewFrames = new byte[1][];
                                previewFrames[0] = Texture2D.whiteTexture.EncodeToPNG();
                            }

                            bf.Serialize(prevStream, previewFrames);
                            prevStream.Seek(0, SeekOrigin.Begin);

                            var entry = new ICSharpCode.SharpZipLib.Zip.ZipEntry("preview");
                            s.PutNextEntry(entry);
                            s.Write(prevStream.ToArray(), 0, (int)prevStream.Length);
                            s.CloseEntry();
                        }

                        using (MemoryStream stageStream = new MemoryStream())
                        {
                            var bf = new BinaryFormatter();
                            bf.Serialize(stageStream, stage);
                            stageStream.Seek(0, SeekOrigin.Begin);

                            var entry = new ICSharpCode.SharpZipLib.Zip.ZipEntry("stage");
                            s.PutNextEntry(entry);
                            s.Write(stageStream.ToArray(), 0, (int)stageStream.Length);
                            s.CloseEntry();
                        }

                        s.Finish();
                        s.Flush();
                        s.Close();
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Error writing zip file: " + "memory" + "\n" + e.Message);
                }

                return output.ToArray();
            }
        }
        catch (Exception e)
        {
            Debug.Log("Error saving stage file: " + "memory" + "\n" + e.Message);
            return null;
        }
    }

    public class PreviewAnimation
    {
        public string filename;
        public byte[][] previewFrames;
    }
}
#endif