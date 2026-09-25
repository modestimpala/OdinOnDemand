using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace OdinOnDemand.MPlayer
{
    /// <summary>How a player with linked speakers places its sound.</summary>
    public enum SpeakerMode
    {
        /// <summary>One source at the average position of the speakers.</summary>
        Center = 0,

        /// <summary>A source at every speaker, all playing the same audio.</summary>
        EachSpeaker = 1
    }

    /// <summary>
    ///     Copies what the player's audio source plays so speaker emitters can replay it. It sits
    ///     in the source's filter chain, which Unity runs before distance attenuation and panning,
    ///     so every backend (VLC, clips, stations, video) arrives here as the plain signal.
    /// </summary>
    internal sealed class AudioTap : MonoBehaviour
    {
        private const int Capacity = 1 << 16; // samples: about 0.7 s of stereo at 48 kHz
        private const int Mask = Capacity - 1;

        private readonly float[] ring = new float[Capacity];
        private readonly object sync = new object();
        private long written;
        private int channels;
        private long lastWriteTimestamp;

        /// <summary>Copy the audio. Off, the filter returns immediately.</summary>
        internal volatile bool Active;

        /// <summary>False silences the source itself after copying, leaving only the emitters.</summary>
        internal volatile bool PassThrough = true;

        /// <summary>True while audio arrived in the last half second.</summary>
        internal bool IsFlowing =>
            Active && Stopwatch.GetTimestamp() - Interlocked.Read(ref lastWriteTimestamp) < Stopwatch.Frequency / 2;

        internal sealed class Reader
        {
            internal long Cursor = -1;
            internal bool Primed;
        }

        private void OnAudioFilterRead(float[] data, int dataChannels)
        {
            if (Active)
            {
                lock (sync)
                {
                    if (dataChannels != channels)
                    {
                        channels = dataChannels;
                        written = 0;
                    }
                    for (int i = 0; i < data.Length; i++)
                        ring[(written + i) & Mask] = data[i];
                    written += data.Length;
                }
                Interlocked.Exchange(ref lastWriteTimestamp, Stopwatch.GetTimestamp());
            }
            if (!PassThrough) Array.Clear(data, 0, data.Length);
        }

        /// <summary>
        ///     Fills <paramref name="data" /> from the copy. Readers trail the tap by two blocks:
        ///     the mixer runs sources in no set order, and a reader mixed before the tap in a pass
        ///     would otherwise find nothing new and click. A stalled tap (pause, stop) gives
        ///     silence rather than a repeated block.
        /// </summary>
        internal void Read(float[] data, int dataChannels, Reader reader)
        {
            lock (sync)
            {
                int frames = data.Length / dataChannels;
                long needed = (long)frames * channels;
                if (channels == 0 || needed == 0 || needed * 2 > Capacity)
                {
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                long behind = written - reader.Cursor;
                if (reader.Cursor < 0 || behind < 0 || behind > needed * 4)
                {
                    // New, restarted after being out of earshot, or lost after a format change.
                    reader.Cursor = Math.Max(0, written - needed * 2);
                    reader.Primed = written >= needed * 2;
                    behind = written - reader.Cursor;
                }

                if (!reader.Primed)
                {
                    if (behind < needed * 2)
                    {
                        Array.Clear(data, 0, data.Length);
                        return;
                    }
                    reader.Primed = true;
                }

                if (behind < needed)
                {
                    reader.Primed = false;
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                long start = reader.Cursor;
                for (int frame = 0; frame < frames; frame++)
                {
                    long source = start + (long)frame * channels;
                    int destination = frame * dataChannels;
                    for (int channel = 0; channel < dataChannels; channel++)
                        data[destination + channel] = ring[(source + channel % channels) & Mask];
                }
                reader.Cursor += needed;
            }
        }
    }

    /// <summary>
    ///     A 3D source at one linked speaker, replaying the player's audio from its tap. It
    ///     plays a clip of constant 1.0, which reaches its filter already scaled by Unity's
    ///     distance falloff, panning and volume for this position. Multiplying the copied audio
    ///     by those samples gives it the same placement a real clip would get; writing the
    ///     copy over them, as before, threw the placement away.
    /// </summary>
    internal sealed class SpeakerEmitter : MonoBehaviour
    {
        private static AudioClip carrier;

        private readonly AudioTap.Reader reader = new AudioTap.Reader();
        private AudioSource source;
        private AudioSource main;
        private BasePlayer player;
        private AudioTap tap;
        private float[] copy = new float[0];

        internal AudioSource Source => source;

        internal static SpeakerEmitter Create(BasePlayer player, AudioTap tap, float spatialBlend)
        {
            var main = player.mAudio;
            var emitterObj = new GameObject("OODSpeakerEmitter");
            emitterObj.transform.SetParent(player.transform, false);
            // The source must come first: a filter only runs on its own object's source.
            var source = emitterObj.AddComponent<AudioSource>();
            var emitter = emitterObj.AddComponent<SpeakerEmitter>();
            emitter.source = source;
            emitter.main = main;
            emitter.player = player;
            emitter.tap = tap;

            source.playOnAwake = false;
            source.loop = true;
            source.clip = Carrier();
            source.outputAudioMixerGroup = main.outputAudioMixerGroup;
            source.spatialBlend = spatialBlend;
            source.spatialize = main.spatialize;
            source.spatializePostEffects = main.spatializePostEffects;
            source.rolloffMode = main.rolloffMode;
            if (main.rolloffMode == AudioRolloffMode.Custom)
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                    main.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            source.minDistance = main.minDistance;
            source.maxDistance = main.maxDistance;
            source.spread = main.spread;
            // Doppler would only bend the constant carrier, never the copied audio.
            source.dopplerLevel = 0f;
            source.priority = main.priority;
            source.reverbZoneMix = main.reverbZoneMix;
            source.volume = player.OutputVolume;
            return emitter;
        }

        private static AudioClip Carrier()
        {
            if (carrier) return carrier;
            const int frames = 4096;
            // Not streamed: Unity calls the reader once, up front, to fill the whole clip.
            carrier = AudioClip.Create("OODSpeakerCarrier", frames, 2, AudioSettings.outputSampleRate, false,
                data =>
                {
                    for (int i = 0; i < data.Length; i++) data[i] = 1f;
                });
            return carrier;
        }

        private void Update()
        {
            if (!main || !tap || !player) return;
            source.volume = player.OutputVolume;
            source.mute = main.mute;
            source.maxDistance = main.maxDistance;

            bool flowing = tap.IsFlowing;
            if (flowing && !source.isPlaying) source.Play();
            else if (!flowing && source.isPlaying) source.Stop();
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            // Audio thread: compare by reference, Unity's null check is main-thread only.
            var currentTap = tap;
            if (ReferenceEquals(currentTap, null))
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            if (copy.Length != data.Length) copy = new float[data.Length];
            currentTap.Read(copy, channels, reader);
            for (int i = 0; i < data.Length; i++)
                data[i] *= copy[i];
        }
    }
}
