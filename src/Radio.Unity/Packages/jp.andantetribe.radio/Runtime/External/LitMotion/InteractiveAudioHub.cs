#if ENABLE_LITMOTION
#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;

namespace Radio
{
    /// <summary>
    /// Plays clips with fade-in, cross-fade, and playback-position synchronization across multiple channels.
    /// </summary>
    public class InteractiveAudioHub : ILoopableAudioHub<AudioClip>,  IAudioHub<AudioClip>
    {
        /// <summary>
        /// The duration of each fade-in or cross-fade transition.
        /// </summary>
        public readonly TimeSpan FadeDuration;

        private readonly ReadOnlyMemory<AudioSource> _channels;
        private readonly HashSet<AudioSource> _excludeVolumeManagementChannels;
        private readonly AsyncReactiveProperty<int> _currentChannelIndex = new(-1);
        private MotionHandle _crossFadeMotionHandle;
        private float _volume;
        private bool _loop;

        /// <inheritdoc />
        public ReadOnlySpan<AudioSource> AudioSources => _channels.Span;

        /// <summary>
        /// Initializes a new instance of the <see cref="InteractiveAudioHub"/> class.
        /// </summary>
        /// <param name="channels">The audio sources used for transitions.</param>
        /// <param name="fadeDuration">The duration of each fade-in or cross-fade transition.</param>
        /// <param name="volume">The initial volume applied to every channel.</param>
        /// <param name="loop">Whether playback loops by default.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="volume"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
        /// </exception>
        public InteractiveAudioHub(ReadOnlyMemory<AudioSource> channels, TimeSpan fadeDuration, float volume = 0.5f, bool loop = true)
        {
            _channels = channels;
            _excludeVolumeManagementChannels = new();
            FadeDuration = fadeDuration;
            _loop = loop;
            ApplyVolume(volume);

            foreach (var channel in _channels.Span)
            {
                channel.loop = loop;
                channel.playOnAwake = false;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InteractiveAudioHub"/> class with a three-second fade duration.
        /// </summary>
        /// <param name="channels">The audio sources used for transitions.</param>
        /// <param name="volume">The initial volume applied to every channel.</param>
        /// <param name="loop">Whether playback loops by default.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="volume"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
        /// </exception>
        public InteractiveAudioHub(ReadOnlyMemory<AudioSource> channels, float volume = 0.5f, bool loop = true)
            : this(channels, TimeSpan.FromSeconds(3.0f), volume, loop)
        {
        }

        /// <inheritdoc />
        public UniTask PlayAsync(AudioClip key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _crossFadeMotionHandle.TryCancel();

            // If no track is currently playing, start with a fade-in
            if (_currentChannelIndex.Value == -1)
            {
                var channel = GetAvailableChannel();
                channel.Stop();
                channel.clip = key;
                channel.loop = _loop;
                channel.volume = 0.0f;
                channel.time = 0.0f;
                channel.Play();
                AdvanceCurrentChannelIndex();

                // Fade in with the same sine curve used by cross-fades.
                var fadeInHandle = LMotion.Create(0.0f, 1.0f, (float)FadeDuration.TotalSeconds)
                    .Bind((self: this, channel), static (rate, args) =>
                    {
                        args.channel.volume = args.self._volume * Mathf.Sin(Mathf.PI * 0.5f * rate);
                    });
                _crossFadeMotionHandle = fadeInHandle;
                _excludeVolumeManagementChannels.Add(channel);
                return UniTask.WhenAll(
                    WaitWhileFadeInAsync(channel, cancellationToken),
                    PlayInternalAsync(channel, key, cancellationToken)
                );
            }

            var currentChannel = AudioSources[_currentChannelIndex.Value];
            var currentChannelRate = Mathf.Clamp01(currentChannel.volume / _volume);
            var nextChannel = GetAvailableChannel();
            nextChannel.Stop();
            nextChannel.clip = key;
            nextChannel.loop = _loop;
            nextChannel.volume = 0.0f;
            nextChannel.time = Mathf.Repeat(currentChannel.time, key.length);
            nextChannel.Play();
            AdvanceCurrentChannelIndex();

            var crossFadeHandle = LMotion.Create(0.0f, 1.0f, (float)FadeDuration.TotalSeconds)
                .Bind((self: this, cur: currentChannel, next: nextChannel, curRate: currentChannelRate), static (rate, args) =>
                {
                    // NOTE:
                    // Using Sin/Cos curves for fading keeps the perceived volume constant throughout.
                    // A linear fade would cause a momentary volume dip at the midpoint of the fade duration.
                    var (self, cur, next, curRate) = args;
                    var f = Mathf.PI * 0.5f * rate;
                    cur.volume = self._volume * Mathf.Cos(f) * curRate;
                    next.volume = self._volume * Mathf.Sin(f);
                });
            _crossFadeMotionHandle = crossFadeHandle;
            _excludeVolumeManagementChannels.Add(currentChannel);
            _excludeVolumeManagementChannels.Add(nextChannel);
            return UniTask.WhenAll(
                WaitWhileCrossFadeAsync(currentChannel, nextChannel, cancellationToken),
                PlayInternalAsync(nextChannel, key, cancellationToken));
        }

        private async UniTask<AsyncUnit> PlayInternalAsync(AudioSource channel, AudioClip key, CancellationToken cancellationToken)
        {
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

            return AsyncUnit.Default;

            async UniTask<AsyncUnit> WaitUntilChannelCyclesAsync(CancellationToken token)
            {
                for (var i = 0; i < _channels.Length; i++)
                {
                    var channelIndex = await _currentChannelIndex.WaitAsync(token);
                    if (channelIndex < 0)
                    {
                        break;
                    }
                }
                return AsyncUnit.Default;
            }
        }

        private async UniTask<AsyncUnit> WaitWhileFadeInAsync(AudioSource channel, CancellationToken cancellationToken)
        {
            var fadeInHandle = _crossFadeMotionHandle;
            try
            {
                await fadeInHandle.ToUniTask(cancellationToken);
            }
            finally
            {
                _excludeVolumeManagementChannels.Remove(channel);
                if (_crossFadeMotionHandle == fadeInHandle)
                {
                    _crossFadeMotionHandle = MotionHandle.None;
                }
            }

            return AsyncUnit.Default;
        }

        private async UniTask<AsyncUnit> WaitWhileCrossFadeAsync(AudioSource current, AudioSource next, CancellationToken cancellationToken)
        {
            var crossFadeHandle = _crossFadeMotionHandle;
            try
            {
                await crossFadeHandle.ToUniTask(cancellationToken);
            }
            finally
            {
                _excludeVolumeManagementChannels.Remove(current);
                _excludeVolumeManagementChannels.Remove(next);
                if (_crossFadeMotionHandle == crossFadeHandle)
                {
                    _crossFadeMotionHandle = MotionHandle.None;
                }

                current.Stop();
                current.clip = null;
            }
            return AsyncUnit.Default;
        }

        /// <inheritdoc />
        public void StopAll()
        {
            _crossFadeMotionHandle.TryCancel();
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
            foreach (var channel in AudioSources)
            {
                if (!_excludeVolumeManagementChannels.Contains(channel))
                {
                    channel.volume = value;
                }
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

#endif
