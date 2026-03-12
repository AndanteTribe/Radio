#if ENABLE_LITMOTION
#nullable enable

using System;
using System.Threading;
using AndanteTribe.Unity.Extensions;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Radio
{
    public partial class AudioPlayerCore
    {
        public readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(3.0f);

        private CancellationTokenSource? _crossFadeCts;

        /// <summary>
        /// Initialize a new instance of <see cref="AudioPlayerCore"/>.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fadeDuration"></param>
        /// <param name="bgmChannelCount"></param>
        /// <param name="useVoice"></param>
        /// <param name="bgmRegistry"></param>
        public AudioPlayerCore(GameObject root, TimeSpan fadeDuration, uint bgmChannelCount = 3, bool useVoice = false, AssetsRegistry? bgmRegistry = null)
            : this (root, bgmChannelCount, useVoice, bgmRegistry)
        {
            FadeDuration = fadeDuration;
        }

        /// <summary>
        /// Asynchronously loads a BGM track and performs a cross-fade transition.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="loop"></param>
        /// <param name="cancellationToken"></param>
        public async UniTask CrossFadeBgmAsync(string address, bool loop = true, CancellationToken cancellationToken = default)
        {
            var clip = await _bgmRegistry.LoadAsync<AudioClip>(address, cancellationToken);
            await CrossFadeBgmCore(clip, loop, cancellationToken);
        }

        /// <summary>
        /// Asynchronously loads a BGM track and performs a cross-fade transition.
        /// </summary>
        /// <param name="reference"></param>
        /// <param name="loop"></param>
        /// <param name="cancellationToken"></param>
        public async UniTask CrossFadeBgmAsync(AssetReferenceT<AudioClip> reference, bool loop = true, CancellationToken cancellationToken = default)
        {
            var clip = await _bgmRegistry.LoadAsync(reference, cancellationToken);
            await CrossFadeBgmCore(clip, loop, cancellationToken);
        }

        // Cancels any in-progress crossfade Tween and cleans up its state so that a new
        // crossfade can start cleanly.  After this returns:
        //   • _crossFadeCts is null
        //   • _excludeVolumeManagementChannels is empty
        //   • every BGM channel that was being *faded out* has been stopped
        //   • _currentBgmChannelIndex still points to the channel that was being *faded in*
        //     (it becomes the starting point for the next crossfade)
        private void InterruptCrossFade()
        {
            if (_crossFadeCts == null) return;

            // Remove all exclusions so that volume management is immediately re-enabled.
            _excludeVolumeManagementChannels.Clear();

            // Stop every BGM channel except the one currently fading in.
            // Those channels were being faded out and are no longer needed.
            if (_currentBgmChannelIndex >= 0)
            {
                var bgmChannels = BgmChannels;
                for (var i = 0; i < bgmChannels.Length; i++)
                {
                    if (i != _currentBgmChannelIndex)
                    {
                        bgmChannels[i].Stop();
                        bgmChannels[i].clip = null;
                    }
                }
            }

            var oldCts = _crossFadeCts;
            _crossFadeCts = null;
            oldCts.Cancel();
            oldCts.Dispose();
        }

        partial void CancelCrossFadeIfActive()
        {
            if (_crossFadeCts == null) return;

            _excludeVolumeManagementChannels.Clear();

            var oldCts = _crossFadeCts;
            _crossFadeCts = null;
            oldCts.Cancel();
            oldCts.Dispose();
        }

        private async UniTask CrossFadeBgmCore(AudioClip clip, bool loop, CancellationToken cancellationToken)
        {
            // Cancel any in-progress crossfade so that at most one Tween is running at a time.
            InterruptCrossFade();

            // Create a linked CTS so the Tween can be cancelled either by the caller's token
            // or by a subsequent CrossFadeBgmCore call (via InterruptCrossFade).
            var myCts = _crossFadeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var linkedToken = myCts.Token;

            // If no track is currently playing, start with a fade-in
            if (_currentBgmChannelIndex == -1)
            {
                var channel = GetAvailableBgmChannel();
                channel.Stop();
                channel.clip = clip;
                channel.loop = loop;
                channel.volume = 0.0f;
                channel.Play();
                _excludeVolumeManagementChannels.Add(channel);

                try
                {
                    // Fade in from 0.0 to PI/2
                    await LMotion.Create(0.0f, 1.0f, (float)FadeDuration.TotalSeconds)
                        .Bind((self: this, channel), static (rate, args) => args.self.ApplyBgmVolume(args.channel, Mathf.PI * 0.5f * rate))
                        .ToUniTask(linkedToken);
                }
                finally
                {
                    // Only clean up if this is still the active crossfade.
                    // If InterruptCrossFade() was called, _crossFadeCts was replaced and we
                    // must not touch state that the new crossfade already set up.
                    if (ReferenceEquals(_crossFadeCts, myCts))
                    {
                        _excludeVolumeManagementChannels.Remove(channel);
                        _crossFadeCts = null;
                        myCts.Cancel();
                        myCts.Dispose();
                    }
                }

                return;
            }

            var currentChannel = BgmChannels[_currentBgmChannelIndex];
            var nextChannel = GetAvailableBgmChannel();
            nextChannel.Stop();
            nextChannel.clip = clip;
            nextChannel.loop = loop;
            nextChannel.volume = 0.0f;
            nextChannel.time = currentChannel.time;
            nextChannel.Play();
            _excludeVolumeManagementChannels.Add(currentChannel);
            _excludeVolumeManagementChannels.Add(nextChannel);

            try
            {
                await LMotion.Create(0.0f, 1.0f, (float)FadeDuration.TotalSeconds)
                    .Bind((self: this, cur: currentChannel, next: nextChannel), static (rate, args) =>
                    {
                        // NOTE:
                        // Using Sin/Cos curves for fading keeps the perceived volume constant throughout.
                        // A linear fade would cause a momentary volume dip at the midpoint of the fade duration.
                        var (self, cur, next) = args;
                        var f = Mathf.PI * 0.5f * rate;
                        self.ApplyBgmVolume(cur, Mathf.Cos(f));
                        self.ApplyBgmVolume(next, Mathf.Sin(f));
                    })
                    .ToUniTask(linkedToken);
            }
            finally
            {
                // Only clean up if this is still the active crossfade (same reasoning as above).
                if (ReferenceEquals(_crossFadeCts, myCts))
                {
                    _excludeVolumeManagementChannels.Remove(currentChannel);
                    _excludeVolumeManagementChannels.Remove(nextChannel);
                    _crossFadeCts = null;
                    myCts.Cancel();
                    myCts.Dispose();
                }
            }

            currentChannel.Stop();
            currentChannel.clip = null;
        }

        private void ApplyBgmVolume(AudioSource channel, float rate)
        {
            channel.volume = _masterVolume * _bgmVolume * rate;
        }
    }
}

#endif