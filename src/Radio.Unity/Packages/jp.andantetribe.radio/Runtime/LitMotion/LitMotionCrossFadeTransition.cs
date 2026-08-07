#if ENABLE_LITMOTION
#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;

namespace Radio
{
    internal sealed class LitMotionCrossFadeTransition : IBgmTransition
    {
        private readonly TimeSpan _fadeDuration;

        private MotionHandle _crossFadeMotionHandle;

        public LitMotionCrossFadeTransition(TimeSpan fadeDuration)
        {
            _fadeDuration = fadeDuration;
        }

        public async UniTask TransitionAsync(
            BgmTransitionContext context,
            AudioClip clip,
            bool loop,
            CancellationToken cancellationToken)
        {
            CancelCrossFadeMotion();

            var currentChannel = context.CurrentBgmChannel;
            if (currentChannel == null)
            {
                var channel = context.GetAvailableBgmChannel();
                channel.Stop();
                channel.clip = clip;
                channel.loop = loop;
                channel.volume = 0.0f;
                channel.Play();

                var fadeInHandle = LMotion
                    .Create(0.0f, 1.0f, (float)_fadeDuration.TotalSeconds)
                    .Bind(
                        (context, channel),
                        static (rate, args) =>
                            args.channel.volume = args.context.ManagedBgmVolume *
                                Mathf.Sin(Mathf.PI * 0.5f * rate));
                _crossFadeMotionHandle = fadeInHandle;

                using (context.AcquireVolumeControl(channel))
                {
                    try
                    {
                        await fadeInHandle.ToUniTask(cancellationToken);
                    }
                    finally
                    {
                        ClearCrossFadeMotionHandleIfCurrent(fadeInHandle);
                    }
                }

                return;
            }

            var currentChannelRate = GetBgmVolumeRate(context, currentChannel);
            var nextChannel = context.GetAvailableBgmChannel();
            nextChannel.Stop();
            nextChannel.clip = clip;
            nextChannel.loop = loop;
            nextChannel.volume = 0.0f;
            nextChannel.time = Mathf.Repeat(currentChannel.time, clip.length);
            nextChannel.Play();

            var crossFadeHandle = LMotion
                .Create(0.0f, 1.0f, (float)_fadeDuration.TotalSeconds)
                .Bind(
                    (
                        context,
                        current: currentChannel,
                        next: nextChannel,
                        currentRate: currentChannelRate
                    ),
                    static (rate, args) =>
                    {
                        var f = Mathf.PI * 0.5f * rate;
                        args.current.volume = args.context.ManagedBgmVolume *
                            args.currentRate * Mathf.Cos(f);
                        args.next.volume = args.context.ManagedBgmVolume * Mathf.Sin(f);
                    });
            _crossFadeMotionHandle = crossFadeHandle;

            using (context.AcquireVolumeControl(currentChannel, nextChannel))
            {
                try
                {
                    await crossFadeHandle.ToUniTask(cancellationToken);
                }
                finally
                {
                    ClearCrossFadeMotionHandleIfCurrent(crossFadeHandle);
                }
            }

            currentChannel.Stop();
            currentChannel.clip = null;
        }

        private static float GetBgmVolumeRate(
            BgmTransitionContext context,
            AudioSource channel)
        {
            var managedVolume = context.ManagedBgmVolume;
            if (managedVolume <= 0.0f)
            {
                return 0.0f;
            }

            return Mathf.Clamp01(channel.volume / managedVolume);
        }

        private void CancelCrossFadeMotion()
        {
            if (_crossFadeMotionHandle.IsActive())
            {
                _crossFadeMotionHandle.Cancel();
            }
        }

        private void ClearCrossFadeMotionHandleIfCurrent(MotionHandle handle)
        {
            if (_crossFadeMotionHandle == handle)
            {
                _crossFadeMotionHandle = MotionHandle.None;
            }
        }
    }
}
#endif
