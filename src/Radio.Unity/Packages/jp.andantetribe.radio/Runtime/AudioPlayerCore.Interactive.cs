#if ENABLE_LITMOTION

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;

namespace Radio
{
    public partial class AudioPlayerCore<T>
    {
        public readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(3.0f);
        private MotionHandle _crossFadeMotionHandle;

        /// <summary>
        /// Initialize a new instance of <see cref="AudioPlayerCore{T}"/> with a custom cross-fade duration.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="audioClipLoad"></param>
        /// <param name="fadeDuration"></param>
        /// <param name="bgmChannels"></param>
        /// <param name="useVoice"></param>
        public AudioPlayerCore(GameObject root, IAudioClipLoad<T> audioClipLoad, TimeSpan fadeDuration, int bgmChannels = 3, bool useVoice = false)
            : this(root, audioClipLoad, bgmChannels, useVoice)
        {
            FadeDuration = fadeDuration;
        }

        /// <summary>
        /// addressに用意されているBGMを読み込み、クロスフェードで再生を切り替える。
        /// </summary>
        /// <param name="address"></param>
        /// <param name="loop"></param>
        /// <param name="cancellationToken"></param>
        public async UniTask CrossFadeBgmAsync(T address, bool loop = true, CancellationToken cancellationToken = default)
        {
            var clip = await _audioClipLoad.LoadAsync(address, cancellationToken);
            await CrossFadeBgmCoreAsync(address, clip, loop, cancellationToken);
        }

        private async UniTask CrossFadeBgmCoreAsync(T address, AudioClip clip, bool loop, CancellationToken cancellationToken)
        {
            CancelCrossFadeMotion();

            // 現在再生中のBGMが無ければフェードインだけ行う
            if (_currentBgmChannelIndex.Value == -1)
            {
                var channelIndex = GetAvailableBgmChannelIndex();
                var channel = BgmChannels[channelIndex];
                channel.Stop();
                ReleaseBgmChannel(channelIndex); // 使い回すチャンネルに前のクリップが残っていれば先に解放する

                channel.clip = clip;
                channel.loop = loop;
                channel.volume = 0.0f;
                channel.Play();
                _bgmChannelKeys[channelIndex] = address;

                // クロスフェードと同じサインカーブでフェードインする。
                var fadeInHandle = LMotion.Create(0.0f, 1.0f, (float)FadeDuration.TotalSeconds)
                    .Bind((self: this, channel), static (rate, args) => args.self.ApplyBgmVolume(args.channel, Mathf.Sin(Mathf.PI * 0.5f * rate)));
                _crossFadeMotionHandle = fadeInHandle;

                try
                {
                    await fadeInHandle.ToUniTask(cancellationToken);
                }
                finally
                {
                    ClearCrossFadeMotionHandleIfCurrent(fadeInHandle);
                }

                return;
            }

            var currentChannelIndex = _currentBgmChannelIndex.Value;
            var currentChannel = BgmChannels[currentChannelIndex];
            var currentChannelRate = GetBgmVolumeRate(currentChannel);
            var nextChannelIndex = GetAvailableBgmChannelIndex();
            var nextChannel = BgmChannels[nextChannelIndex];
            nextChannel.Stop();
            ReleaseBgmChannel(nextChannelIndex); // 使い回すチャンネルに前のクリップが残っていれば先に解放する

            nextChannel.clip = clip;
            nextChannel.loop = loop;
            nextChannel.volume = 0.0f;
            nextChannel.time = Mathf.Repeat(currentChannel.time, clip.length);
            nextChannel.Play();
            _bgmChannelKeys[nextChannelIndex] = address;

            var crossFadeHandle = LMotion.Create(0.0f, 1.0f, (float)FadeDuration.TotalSeconds)
                .Bind((self: this, cur: currentChannel, next: nextChannel, curRate: currentChannelRate), static (rate, args) =>
                {
                    // NOTE:
                    // Using Sin/Cos curves for fading keeps the perceived volume constant throughout.
                    // A linear fade would cause a momentary volume dip at the midpoint of the fade duration.
                    var (self, cur, next, curRate) = args;
                    var f = Mathf.PI * 0.5f * rate;
                    self.ApplyBgmVolume(cur, curRate * Mathf.Cos(f));
                    self.ApplyBgmVolume(next, Mathf.Sin(f));
                });
            _crossFadeMotionHandle = crossFadeHandle;

            try
            {
                await crossFadeHandle.ToUniTask(cancellationToken);
            }
            finally
            {
                ClearCrossFadeMotionHandleIfCurrent(crossFadeHandle);
            }

            currentChannel.Stop();
            ReleaseBgmChannel(currentChannelIndex); // クロスフェードで切り替え終えた旧チャンネルのクリップを解放する
            currentChannel.clip = null;
        }

        private void CancelCrossFadeMotion()
        {
            if (!_crossFadeMotionHandle.IsActive())
            {
                return;
            }

            _crossFadeMotionHandle.Cancel();
        }

        private void ClearCrossFadeMotionHandleIfCurrent(MotionHandle handle)
        {
            if (_crossFadeMotionHandle == handle)
            {
                _crossFadeMotionHandle = MotionHandle.None;
            }
        }

        private float GetBgmVolumeRate(AudioSource channel)
        {
            var managedVolume = _masterVolume * _bgmVolume;
            if (managedVolume <= 0.0f)
            {
                return 0.0f;
            }

            return Mathf.Clamp01(channel.volume / managedVolume);
        }

        private void ApplyBgmVolume(AudioSource channel, float rate)
        {
            channel.volume = _masterVolume * _bgmVolume * rate;
        }
    }
}

#endif
