#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    /// <summary>
    /// Plays <see cref="AudioClip"/> instances without owning external asset handles.
    /// </summary>
    public class AudioPlayer
    {
        private const float DefaultVolume = 0.5f;

        private readonly AudioSource[] _allChannels;
        private readonly BgmTransitionContext _bgmTransitionContext;
        private readonly bool _useVoice;
        private readonly HashSet<AudioSource> _excludeVolumeManagementChannels = new();
        private readonly AsyncReactiveProperty<int> _currentBgmChannelIndex = new(-1);

        private IBgmTransition? _bgmTransition;
        private float _masterVolume = DefaultVolume;
        private float _bgmVolume = DefaultVolume;
        private float _seVolume = DefaultVolume;
        private float _voiceVolume = DefaultVolume;

        private ReadOnlySpan<AudioSource> BgmChannels => _allChannels.AsSpan(_useVoice ? 2 : 1);
        private AudioSource SeChannel => _allChannels[0];
        private AudioSource VoiceChannel => _useVoice ? _allChannels[1] : throw new InvalidOperationException("Voice channel is not enabled.");

        /// <summary>
        /// Initializes a new player and attaches missing channels to <paramref name="root"/>.
        /// </summary>
        /// <param name="root">The GameObject that owns the managed AudioSource components.</param>
        /// <param name="bgmChannelCount">The minimum number of BGM channels.</param>
        /// <param name="useVoice">Whether to reserve a dedicated voice channel.</param>
        public AudioPlayer(GameObject root, uint bgmChannelCount = 3, bool useVoice = false)
        {
            if (bgmChannelCount == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bgmChannelCount),
                    "At least one BGM channel is required.");
            }

            _allChannels = root.GetComponents<AudioSource>();
            var existingChannels = _allChannels.AsSpan();

            var allChannelCount = bgmChannelCount + 1 + (useVoice ? 1 : 0);
            if (existingChannels.Length < allChannelCount)
            {
                var channels = new AudioSource[allChannelCount];
                existingChannels.CopyTo(channels);
                for (var i = 0; i < channels.Length; i++)
                {
                    var channel = channels[i];
                    if (channel == null)
                    {
                        channel = channels[i] = root.AddComponent<AudioSource>();
                    }
                    channel.loop = false;
                    channel.playOnAwake = false;
                    channel.volume = DefaultVolume;
                }
                _allChannels = channels;
            }

            _useVoice = useVoice;
            Sources = new AudioSources(_allChannels, useVoice);
            _bgmTransitionContext = new BgmTransitionContext(this);
        }

        /// <summary>
        /// Gets the channels managed by this player.
        /// </summary>
        public AudioSources Sources { get; }

        /// <summary>
        /// Plays a BGM clip on the next channel in rotation.
        /// </summary>
        /// <param name="clip">The clip to play.</param>
        /// <param name="loop">Whether the clip loops.</param>
        /// <param name="cancellationToken">Cancels this playback operation.</param>
        public UniTask PlayBgmAsync(AudioClip clip, bool loop = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PlayBgmCoreAsync(clip, loop, cancellationToken);
        }

        /// <summary>
        /// Plays a BGM clip using the configured transition.
        /// </summary>
        /// <remarks>
        /// Configure a transition provider such as <c>UseLitMotionCrossFade</c> before calling this method.
        /// </remarks>
        public UniTask CrossFadeBgmAsync(
            AudioClip clip,
            bool loop = true,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transition = _bgmTransition ??
                throw new InvalidOperationException("No BGM transition is configured.");
            return transition.TransitionAsync(
                _bgmTransitionContext,
                clip,
                loop,
                cancellationToken);
        }

        private async UniTask PlayBgmCoreAsync(AudioClip clip, bool loop, CancellationToken cancellationToken)
        {
            var channel = GetAvailableBgmChannel();
            channel.Stop();
            channel.clip = clip;
            channel.loop = loop;
            channel.volume = _bgmVolume * _masterVolume;
            channel.Play();

            try
            {
                if (loop)
                {
                    await WaitUntilBgmChannelCyclesAsync(cancellationToken);
                }
                else
                {
                    using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    try
                    {
                        await UniTask.WhenAny(
                            UniTask.Delay(TimeSpan.FromSeconds(clip.length), cancellationToken: linkedCancellationTokenSource.Token).AsAsyncUnitUniTask(),
                            WaitUntilBgmChannelCyclesAsync(linkedCancellationTokenSource.Token));
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

            async UniTask<AsyncUnit> WaitUntilBgmChannelCyclesAsync(CancellationToken token)
            {
                for (var i = 0; i < BgmChannels.Length; i++)
                {
                    var channelIndex = await _currentBgmChannelIndex.WaitAsync(token);
                    if (channelIndex < 0)
                    {
                        break;
                    }
                }
                return AsyncUnit.Default;
            }
        }

        /// <summary>
        /// Stops and clears every managed BGM channel.
        /// </summary>
        public virtual void StopAllBgm()
        {
            foreach (var channel in BgmChannels)
            {
                channel.Stop();
                channel.clip = null;
                channel.loop = false;
            }
            _currentBgmChannelIndex.Value = -1;
        }

        /// <summary>
        /// Plays a sound effect and waits for its clip length.
        /// </summary>
        /// <param name="clip">The clip to play.</param>
        /// <param name="cancellationToken">Cancels the wait operation.</param>
        public UniTask PlaySeAsync(AudioClip clip, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PlayNonBgmCoreAsync(clip, SeChannel, cancellationToken);
        }

        /// <summary>
        /// Plays a voice clip and waits for its clip length.
        /// </summary>
        /// <param name="clip">The clip to play.</param>
        /// <param name="cancellationToken">Cancels the wait operation.</param>
        public UniTask PlayVoiceAsync(AudioClip clip, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PlayNonBgmCoreAsync(clip, VoiceChannel, cancellationToken);
        }

        protected static async UniTask PlayNonBgmCoreAsync(AudioClip clip, AudioSource channel, CancellationToken cancellationToken)
        {
            channel.PlayOneShot(clip);
            await UniTask.Delay(TimeSpan.FromSeconds(clip.length), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Sets the master volume affecting all managed channels.
        /// </summary>
        /// <param name="volume">The requested volume, clamped to 0 through 1.</param>
        public void SetMasterVolume(float volume)
        {
            _masterVolume = Mathf.Clamp01(volume);
            SeChannel.volume = _seVolume * _masterVolume;
            if (_useVoice)
            {
                VoiceChannel.volume = _voiceVolume * _masterVolume;
            }
            foreach (var channel in BgmChannels)
            {
                if (!_excludeVolumeManagementChannels.Contains(channel))
                {
                    channel.volume = _bgmVolume * _masterVolume;
                }
            }
        }

        /// <summary>
        /// Sets the BGM volume.
        /// </summary>
        /// <param name="volume">The requested volume, clamped to 0 through 1.</param>
        public void SetBgmVolume(float volume)
        {
            _bgmVolume = Mathf.Clamp01(volume);
            foreach (var channel in BgmChannels)
            {
                if (!_excludeVolumeManagementChannels.Contains(channel))
                {
                    channel.volume = _bgmVolume * _masterVolume;
                }
            }
        }

        /// <summary>
        /// Sets the sound-effect volume.
        /// </summary>
        /// <param name="volume">The requested volume, clamped to 0 through 1.</param>
        public void SetSeVolume(float volume)
        {
            _seVolume = Mathf.Clamp01(volume);
            SeChannel.volume = _seVolume * _masterVolume;
        }

        /// <summary>
        /// Sets the voice volume.
        /// </summary>
        /// <param name="volume">The requested volume, clamped to 0 through 1.</param>
        public void SetVoiceVolume(float volume)
        {
            _voiceVolume = Mathf.Clamp01(volume);
            VoiceChannel.volume = _voiceVolume * _masterVolume;
        }

        /// <summary>
        /// Configures the implementation used by <see cref="CrossFadeBgmAsync"/>.
        /// </summary>
        public void ConfigureBgmTransition(IBgmTransition transition) =>
            _bgmTransition = transition ?? throw new ArgumentNullException(nameof(transition));

        internal AudioSource? CurrentBgmChannel =>
            _currentBgmChannelIndex.Value < 0
                ? null
                : Sources.Bgm[_currentBgmChannelIndex.Value];

        internal float ManagedBgmVolume => _masterVolume * _bgmVolume;

        internal void ExcludeFromVolumeManagement(AudioSource channel) =>
            _excludeVolumeManagementChannels.Add(channel);

        internal void IncludeInVolumeManagement(AudioSource channel) =>
            _excludeVolumeManagementChannels.Remove(channel);

        internal AudioSource GetAvailableBgmChannel() =>
            BgmChannels[_currentBgmChannelIndex.Value = (_currentBgmChannelIndex.Value + 1) % BgmChannels.Length];
    }
}
