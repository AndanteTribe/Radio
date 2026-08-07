#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    /// <summary>
    /// Plays overlapping one-shot clips through a single <see cref="AudioSource"/>.
    /// </summary>
    public class SingleChannelAudioHub : IAudioHub<AudioClip>
    {
#if NET7_0_OR_GREATER
        private readonly AudioSource _source;
#else
        private AudioSource _source;
#endif
        private float _volume;

        /// <inheritdoc />
        public ReadOnlySpan<AudioSource> AudioSources
        {
#if NET7_0_OR_GREATER
            get => MemoryMarshal.CreateReadOnlySpan(in _source, 1);
#else
            get => MemoryMarshal.CreateReadOnlySpan(ref _source, 1);
#endif
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleChannelAudioHub"/> class.
        /// </summary>
        /// <param name="source">The audio source used for one-shot playback.</param>
        /// <param name="volume">The initial volume scale.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="volume"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
        /// </exception>
        public SingleChannelAudioHub(AudioSource source, float volume = 0.5f)
        {
            ApplyVolume(volume);
            _source = source;
        }

        /// <inheritdoc />
        public UniTask PlayAsync(AudioClip key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _source.PlayOneShot(key, _volume);
            return UniTask.Delay(TimeSpan.FromSeconds(key.length), cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public void StopAll() => _source.Stop();

        /// <inheritdoc />
        public void ApplyVolume(float value)
        {
            ThrowHelper.ThrowIfVolumeOutOfRange(value);
            _volume = value;
        }
    }
}
