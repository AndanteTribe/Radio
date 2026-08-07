#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    public class MultiChannelsAudioHub : IAudioHub<AudioClip>
    {
        private readonly ReadOnlyMemory<AudioSource> _channels;
        private readonly AsyncReactiveProperty<int> _currentChannelIndex = new(-1);
        private float _volume;

        public bool Loop { get; set; }

        public ReadOnlySpan<AudioSource> AudioSources => _channels.Span;

        public MultiChannelsAudioHub(ReadOnlyMemory<AudioSource> channels, float volume = 0.5f, bool loop = true)
        {
            _channels = channels;
            Loop = loop;
            ApplyVolume(volume);

            foreach (var channel in _channels.Span)
            {
                channel.loop = loop;
                channel.playOnAwake = false;
            }
        }

        public async UniTask PlayAsync(AudioClip key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var channel = GetAvailableChannel();
            channel.Stop();
            channel.clip = key;
            channel.loop = Loop;
            channel.volume = _volume;
            channel.Play();
            AdvanceCurrentChannelIndex();

            try
            {
                if (Loop)
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

        public void ApplyVolume(float value)
        {
            ThrowHelper.ThrowIfVolumeOutOfRange(value);
            foreach (var channel in _channels.Span)
            {
                channel.volume = value;
            }
            _volume = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AudioSource GetAvailableChannel() => _channels.Span[(_currentChannelIndex.Value + 1) % _channels.Length];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdvanceCurrentChannelIndex() => _currentChannelIndex.Value = (_currentChannelIndex.Value + 1) % _channels.Length;
    }
}