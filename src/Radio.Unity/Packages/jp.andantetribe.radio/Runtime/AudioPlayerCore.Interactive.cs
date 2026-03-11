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

        private async UniTask CrossFadeBgmCore(AudioClip clip, bool loop, CancellationToken cancellationToken)
        {
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

                // Fade in from 0.0 to PI/2
                await LMotion.Create(0.0f, 1.0f, (float)FadeDuration.TotalSeconds)
                    .Bind((self: this, channel), static (rate, args) => args.self.ApplyBgmVolume(args.channel, Mathf.PI * 0.5f * rate))
                    .ToUniTask(cancellationToken);

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
            _excludeVolumeManagementChannels.Add(nextChannel);

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
                .ToUniTask(cancellationToken);

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