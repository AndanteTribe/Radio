#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    /// <summary>
    /// Rotates playback requests across multiple <see cref="AudioSource"/> channels.
    /// </summary>
    public class MultiChannelsAudioHub : ILoopableAudioHub<AudioClip>, IAudioHub<AudioClip>
    {
        private readonly ReadOnlyMemory<AudioSource> _channels;
        private readonly AsyncReactiveProperty<int> _currentChannelIndex = new(-1);
        private float _volume;
        private bool _loop;

        /// <inheritdoc />
        public ReadOnlySpan<AudioSource> AudioSources => _channels.Span;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiChannelsAudioHub"/> class.
        /// </summary>
        /// <param name="channels">The audio sources to rotate through in order.</param>
        /// <param name="volume">The initial volume applied to every channel.</param>
        /// <param name="loop">Whether playback loops by default.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="volume"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
        /// </exception>
        public MultiChannelsAudioHub(ReadOnlyMemory<AudioSource> channels, float volume = 0.5f, bool loop = true)
        {
            _channels = channels;
            _loop = loop;
            ApplyVolume(volume);

            foreach (var channel in _channels.Span)
            {
                channel.loop = loop;
                channel.playOnAwake = false;
            }
        }

        /// <inheritdoc />
        public async UniTask PlayAsync(AudioClip key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var channel = GetAvailableChannel();
            channel.Stop();
            channel.clip = key;
            channel.loop = _loop;
            channel.volume = _volume;
            channel.Play();
            AdvanceCurrentChannelIndex();

            try
            {
                if (_loop)
                {
                    await WaitUntilChannelCyclesAsync(cancellationToken);
                }
                else
                {
                    using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    try
                    {
                        await UniTask.WhenAny(
                            UniTask.Delay(TimeSpan.FromSeconds(key.length), cancellationToken: linkedCancellationTokenSource.Token).AsAsyncUnitUniTask(),
                            WaitUntilChannelCyclesAsync(linkedCancellationTokenSource.Token));
                    }
                    finally
                    {
                        linkedCancellationTokenSource.Cancel();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                channel.Stop();
                channel.clip = null;
                channel.loop = false;
                throw;
            }

            async UniTask<AsyncUnit> WaitUntilChannelCyclesAsync(CancellationToken token)
            {
                for (var i = 0; i < _channels.Length; i++)
                {
                    var chIndex = await _currentChannelIndex.WaitAsync(token);
                    if (chIndex < 0)
                    {
                        break;
                    }
                }
                return AsyncUnit.Default;
            }
        }

        /// <inheritdoc />
        public void StopAll()
        {
            foreach (var channel in _channels.Span)
            {
                channel.Stop();
                channel.clip = null;
                channel.loop = false;
            }
            _currentChannelIndex.Value = -1;
        }

        /// <inheritdoc />
        public void ApplyVolume(float value)
        {
            ThrowHelper.ThrowIfVolumeOutOfRange(value);
            foreach (var channel in _channels.Span)
            {
                channel.volume = value;
            }
            _volume = value;
        }

        /// <inheritdoc />
        public void ApplyLoop(bool value)
        {
            _loop = value;
            foreach (var channel in _channels.Span)
            {
                channel.loop = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AudioSource GetAvailableChannel() => _channels.Span[(_currentChannelIndex.Value + 1) % _channels.Length];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdvanceCurrentChannelIndex() => _currentChannelIndex.Value = (_currentChannelIndex.Value + 1) % _channels.Length;
    }
}
