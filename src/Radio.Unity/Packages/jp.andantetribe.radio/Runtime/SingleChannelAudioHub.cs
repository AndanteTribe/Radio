#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    public class SingleChannelAudioHub : IAudioHub<AudioClip>
    {
#if NET7_0_OR_GREATER
        private readonly AudioSource _source;
#else
        private AudioSource _source;
#endif
        private float _volume;

        public ReadOnlySpan<AudioSource> AudioSources
        {
#if NET7_0_OR_GREATER
            get => MemoryMarshal.CreateReadOnlySpan(in _source, 1);
#else
            get => MemoryMarshal.CreateReadOnlySpan(ref _source, 1);
#endif
        }

        public SingleChannelAudioHub(AudioSource source, float volume = 0.5f)
        {
            ApplyVolume(volume);
            _source = source;
        }

        public UniTask PlayAsync(AudioClip key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _source.PlayOneShot(key, _volume);
            return UniTask.Delay(TimeSpan.FromSeconds(key.length), cancellationToken: cancellationToken);
        }

        public void StopAll() => _source.Stop();

        public void ApplyVolume(float value)
        {
            ThrowHelper.ThrowIfVolumeOutOfRange(value);
            _volume = value;
        }
    }
}