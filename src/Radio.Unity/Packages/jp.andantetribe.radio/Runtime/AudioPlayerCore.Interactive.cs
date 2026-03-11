#if ENABLE_LITMOTION
#nullable enable

using System;
using System.Threading;
using AndanteTribe.Unity.Extensions;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;

namespace Radio
{
    public partial class AudioPlayerCore
    {
        public readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(3.0f);

        /// <summary>
        /// Initialize a new instance of <see cref="AudioPlayerCore"/>.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="bgmChannelCount"></param>
        /// <param name="useVoice"></param>
        /// <param name="bgmRegistry"></param>
        /// <param name="fadeDuration"></param>
        public AudioPlayerCore(GameObject root, int bgmChannelCount = 3, bool useVoice = false, AssetsRegistry? bgmRegistry = null, TimeSpan fadeDuration = default)
            :this (root, bgmChannelCount, useVoice, bgmRegistry)
        {
            FadeDuration = fadeDuration;
        }

        public async UniTask CrossFadeBgmAsync(string address, bool loop = true, CancellationToken cancellationToken = default)
        {
            var clip = await _bgmRegistry.LoadAsync<AudioClip>(address, cancellationToken);

            // 再生曲がなければフェードインで再生開始
            if (_currentBgmChannelIndex == -1)
            {
                var channel = GetAvailableBgmChannel();
                channel.Stop();
                channel.clip = clip;
                channel.loop = loop;
                channel.volume = 0.0f;
                channel.Play();

                // 0.0~PI/2
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

            await LMotion.Create(0.0f, 1.0f, (float)FadeDuration.TotalSeconds)
                .Bind((self: this, cur: currentChannel, next: nextChannel), static (rate, args) =>
                {
                    // NOTE:
                    // Sin/Cosカーブでフェードすると,聴覚上の音量が常に均等になる.
                    // 直線にすると,FadeDurationの中間地点で一回音量が下がる瞬間が発生する.
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