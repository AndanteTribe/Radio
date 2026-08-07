#if ENABLE_LITMOTION
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Radio.Tests
{
    public sealed class InteractiveAudioHubTests
    {
        private static readonly TimeSpan s_testTimeout = TimeSpan.FromSeconds(2.0f);

        private GameObject _root = null!;
        private readonly List<AudioClip> _clips = new();
        private readonly List<InteractiveAudioHub> _hubs = new();

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject(nameof(InteractiveAudioHubTests));
            _root.AddComponent<AudioListener>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var hub in _hubs)
            {
                hub.StopAll();
            }
            yield return null;

            DestroyImmediate(_root);
            foreach (var clip in _clips)
            {
                DestroyImmediate(clip);
            }
            _clips.Clear();
            _hubs.Clear();
        }

        [Test]
        public void DefaultConstructorInitializesSourcesAndUsesThreeSecondFade()
        {
            var channels = CreateChannels(2);

            var hub = Track(new InteractiveAudioHub(channels, volume: 0.25f, loop: false));

            Assert.That(hub.FadeDuration, Is.EqualTo(TimeSpan.FromSeconds(3.0f)));
            Assert.That(hub.Loop, Is.False);
            Assert.That(hub.AudioSources.Length, Is.EqualTo(2));
            Assert.That(hub.AudioSources[0], Is.SameAs(channels[0]));
            Assert.That(hub.AudioSources[1], Is.SameAs(channels[1]));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => !channel.loop));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => !channel.playOnAwake));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => Mathf.Approximately(channel.volume, 0.25f)));

            hub.StopAll();

            Assert.That(channels, Has.All.Matches<AudioSource>(channel => channel.clip == null));
        }

        [Test]
        public void InvalidVolumeIsRejectedByConstructorAndApplyVolume()
        {
            var channels = CreateChannels(1);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.03f), volume: 0.0f));

            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.03f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => hub.ApplyVolume(1.01f));
        }

        [Test]
        public void EmptyChannelCollectionFailsWhenPlaybackIsRequested()
        {
            var hub = Track(new InteractiveAudioHub(Array.Empty<AudioSource>(), TimeSpan.FromSeconds(0.03f)));
            var clip = CreateClip("Empty Channels", 0.04f);

            Assert.DoesNotThrow(() => hub.ApplyVolume(0.75f));
            Assert.DoesNotThrow(hub.StopAll);
            Assert.Throws<DivideByZeroException>(() => _ = hub.PlayAsync(clip, CancellationToken.None));
        }

        [Test]
        public void CancellationRequestedBeforePlaybackDoesNotChangeAnySource()
        {
            var channels = CreateChannels(2);
            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.03f)));
            var clip = CreateClip("Pre-cancelled", 0.04f);
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                _ = hub.PlayAsync(clip, cancellationTokenSource.Token));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => channel.clip == null));
        }

        [UnityTest]
        public IEnumerator FirstPlaybackFadesInFromTheBeginningAndCompletesAfterClipLifetime() => UniTask.ToCoroutine(async () =>
        {
            var channels = CreateChannels(2);
            var fadeDuration = TimeSpan.FromSeconds(0.03f);
            var hub = Track(new InteractiveAudioHub(channels, fadeDuration, volume: 0.6f, loop: false));
            var clip = CreateClip("First Fade", 0.08f);
            var completed = false;

            var task = hub.PlayAsync(clip, CancellationToken.None).ContinueWith(() => completed = true);

            Assert.That(hub.FadeDuration, Is.EqualTo(fadeDuration));
            Assert.That(channels[0].clip, Is.SameAs(clip));
            Assert.That(channels[0].loop, Is.False);
            Assert.That(channels[0].time, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(channels[0].volume, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(channels[1].clip, Is.Null);

            await UniTask.DelayFrame(1).Timeout(s_testTimeout);
            Assert.That(completed, Is.False);

            await task.Timeout(s_testTimeout);

            Assert.That(completed, Is.True);
            Assert.That(channels[0].clip, Is.SameAs(clip));
            Assert.That(channels[0].volume, Is.EqualTo(0.6f).Within(0.03f));
        });

        [UnityTest]
        public IEnumerator CrossFadeSynchronizesTimeAndClearsOnlyPreviousChannel() => UniTask.ToCoroutine(async () =>
        {
            var channels = CreateChannels(2);
            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.03f), volume: 0.7f, loop: false));
            var firstClip = CreateClip("Time Source", 0.12f);
            var nextClip = CreateClip("Time Destination", 0.05f);

            await hub.PlayAsync(firstClip, CancellationToken.None).Timeout(s_testTimeout);
            channels[0].time = 0.09f;
            var expectedTime = Mathf.Repeat(channels[0].time, nextClip.length);

            var task = hub.PlayAsync(nextClip, CancellationToken.None);

            Assert.That(channels[1].clip, Is.SameAs(nextClip));
            Assert.That(channels[1].loop, Is.False);
            Assert.That(channels[1].time, Is.EqualTo(expectedTime).Within(0.015f));

            await task.Timeout(s_testTimeout);

            Assert.That(channels[0].clip, Is.Null);
            Assert.That(channels[1].clip, Is.SameAs(nextClip));
            Assert.That(channels[1].volume, Is.EqualTo(0.7f).Within(0.03f));
        });

        [UnityTest]
        public IEnumerator ConsecutiveInterruptionsCancelPreviousTransitionsAndKeepLatestTrack() => UniTask.ToCoroutine(async () =>
        {
            var channels = CreateChannels(2);
            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.08f), volume: 1.0f, loop: false));
            var firstClip = CreateClip("Interrupted A", 0.2f);
            var secondClip = CreateClip("Interrupted B", 0.2f);
            var thirdClip = CreateClip("Interrupted C", 0.2f);

            var firstTask = hub.PlayAsync(firstClip, CancellationToken.None);
            await WaitUntilAsync(() => channels[0].volume > 0.15f && channels[0].volume < 0.75f);

            var secondTask = hub.PlayAsync(secondClip, CancellationToken.None);
            await WaitUntilAsync(() =>
                channels[1].clip == secondClip && channels[1].volume > 0.1f && channels[1].volume < 0.75f);

            var thirdTask = hub.PlayAsync(thirdClip, CancellationToken.None);

            var firstCancelled = await firstTask.SuppressCancellationThrow().Timeout(s_testTimeout);
            var secondCancelled = await secondTask.SuppressCancellationThrow().Timeout(s_testTimeout);
            await thirdTask.Timeout(s_testTimeout);

            Assert.That(firstCancelled, Is.True);
            Assert.That(secondCancelled, Is.True);
            Assert.That(channels[0].clip, Is.SameAs(thirdClip));
            Assert.That(channels[0].volume, Is.EqualTo(1.0f).Within(0.03f));
            Assert.That(channels[1].clip, Is.Null);
        });

        [UnityTest]
        public IEnumerator ApplyVolumeDuringCrossFadeLeavesOwnedChannelsUntouchedAndUpdatesIdleChannel() => UniTask.ToCoroutine(async () =>
        {
            var channels = CreateChannels(3);
            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.08f), volume: 1.0f, loop: false));
            var firstClip = CreateClip("Volume A", 0.06f);
            var secondClip = CreateClip("Volume B", 0.14f);

            await hub.PlayAsync(firstClip, CancellationToken.None).Timeout(s_testTimeout);
            var task = hub.PlayAsync(secondClip, CancellationToken.None);
            await WaitUntilAsync(() => channels[1].volume > 0.15f && channels[1].volume < 0.75f);
            var currentVolume = channels[0].volume;
            var nextVolume = channels[1].volume;

            hub.ApplyVolume(0.4f);

            Assert.That(channels[0].volume, Is.EqualTo(currentVolume).Within(0.001f));
            Assert.That(channels[1].volume, Is.EqualTo(nextVolume).Within(0.001f));
            Assert.That(channels[2].volume, Is.EqualTo(0.4f).Within(0.001f));

            await task.Timeout(s_testTimeout);

            Assert.That(channels[0].clip, Is.Null);
            Assert.That(channels[1].clip, Is.SameAs(secondClip));
            Assert.That(channels[1].volume, Is.EqualTo(0.4f).Within(0.03f));
        });

        [UnityTest]
        public IEnumerator NonLoopingPlaybackCompletesEarlyWhenItsChannelCycles() => UniTask.ToCoroutine(async () =>
        {
            var channels = CreateChannels(2);
            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.01f), loop: false));
            var firstClip = CreateClip("Non-loop A", 0.4f);
            var secondClip = CreateClip("Non-loop B", 0.4f);
            var thirdClip = CreateClip("Non-loop C", 0.4f);
            var firstCompleted = false;

            var firstTask = hub.PlayAsync(firstClip, CancellationToken.None).ContinueWith(() => firstCompleted = true);
            await WaitUntilAsync(() => channels[0].volume > 0.48f);
            await UniTask.DelayFrame(3).Timeout(s_testTimeout);

            var secondTask = hub.PlayAsync(secondClip, CancellationToken.None);
            await WaitUntilAsync(() => channels[0].clip == null && channels[1].volume > 0.48f);
            Assert.That(firstCompleted, Is.False);

            var thirdTask = hub.PlayAsync(thirdClip, CancellationToken.None);
            await firstTask.Timeout(s_testTimeout);
            await WaitUntilAsync(() => channels[1].clip == null && channels[0].volume > 0.48f);

            Assert.That(firstCompleted, Is.True);

            hub.StopAll();
            await secondTask.SuppressCancellationThrow().Timeout(s_testTimeout);
            await thirdTask.SuppressCancellationThrow().Timeout(s_testTimeout);
        });

        [UnityTest]
        public IEnumerator LoopingPlaybackCompletesOnlyAfterItsChannelCycles() => UniTask.ToCoroutine(async () =>
        {
            var channels = CreateChannels(2);
            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.01f), loop: true));
            var firstClip = CreateClip("Loop A", 0.2f);
            var secondClip = CreateClip("Loop B", 0.2f);
            var thirdClip = CreateClip("Loop C", 0.2f);
            var firstCompleted = false;

            var firstTask = hub.PlayAsync(firstClip, CancellationToken.None).ContinueWith(() => firstCompleted = true);
            await WaitUntilAsync(() => channels[0].volume > 0.48f);
            await UniTask.DelayFrame(3).Timeout(s_testTimeout);

            var secondTask = hub.PlayAsync(secondClip, CancellationToken.None);
            await WaitUntilAsync(() => channels[0].clip == null && channels[1].volume > 0.48f);
            Assert.That(firstCompleted, Is.False);

            var thirdTask = hub.PlayAsync(thirdClip, CancellationToken.None);
            await firstTask.Timeout(s_testTimeout);
            await WaitUntilAsync(() => channels[1].clip == null && channels[0].volume > 0.48f);

            Assert.That(firstCompleted, Is.True);

            hub.StopAll();
            await secondTask.SuppressCancellationThrow().Timeout(s_testTimeout);
            await thirdTask.SuppressCancellationThrow().Timeout(s_testTimeout);
        });

        [UnityTest]
        public IEnumerator CancellationDuringInitialNonLoopingFadeStopsAndReleasesTheChannel() => UniTask.ToCoroutine(async () =>
        {
            var channels = CreateChannels(2);
            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.08f), loop: false));
            var clip = CreateClip("Cancel Initial", 0.3f);
            using var cancellationTokenSource = new CancellationTokenSource();

            var task = hub.PlayAsync(clip, cancellationTokenSource.Token);
            await WaitUntilAsync(() => channels[0].volume > 0.1f);
            cancellationTokenSource.Cancel();

            var cancelled = await task.SuppressCancellationThrow().Timeout(s_testTimeout);
            await WaitUntilAsync(() => channels[0].clip == null);

            Assert.That(cancelled, Is.True);
            Assert.That(channels[0].clip, Is.Null);
            Assert.That(channels[0].loop, Is.False);

            hub.ApplyVolume(0.4f);
            Assert.That(channels[0].volume, Is.EqualTo(0.4f).Within(0.001f));
        });

        [UnityTest]
        public IEnumerator CancellationDuringLoopingCrossFadeClearsBothTracks() => UniTask.ToCoroutine(async () =>
        {
            var channels = CreateChannels(2);
            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.08f), loop: false));
            var firstClip = CreateClip("Cancel Cross A", 0.05f);
            var secondClip = CreateClip("Cancel Cross B", 0.3f);

            await hub.PlayAsync(firstClip, CancellationToken.None).Timeout(s_testTimeout);
            hub.Loop = true;
            using var cancellationTokenSource = new CancellationTokenSource();
            var task = hub.PlayAsync(secondClip, cancellationTokenSource.Token);
            await WaitUntilAsync(() => channels[1].volume > 0.1f);
            cancellationTokenSource.Cancel();

            var cancelled = await task.SuppressCancellationThrow().Timeout(s_testTimeout);
            await WaitUntilAsync(() => channels[0].clip == null && channels[1].clip == null);

            Assert.That(cancelled, Is.True);
            Assert.That(channels[0].clip, Is.Null);
            Assert.That(channels[1].clip, Is.Null);
            Assert.That(channels[1].loop, Is.False);
        });

        [UnityTest]
        public IEnumerator StopAllDuringFadeClearsStateAndNextPlaybackStartsAtFirstChannel() => UniTask.ToCoroutine(async () =>
        {
            var channels = CreateChannels(2);
            var hub = Track(new InteractiveAudioHub(channels, TimeSpan.FromSeconds(0.08f), loop: true));
            var firstClip = CreateClip("Stopped Fade", 0.3f);
            var nextClip = CreateClip("After Stop", 0.05f);

            var firstTask = hub.PlayAsync(firstClip, CancellationToken.None);
            await WaitUntilAsync(() => channels[0].volume > 0.1f);

            hub.StopAll();
            var firstCancelled = await firstTask.SuppressCancellationThrow().Timeout(s_testTimeout);

            Assert.That(firstCancelled, Is.True);
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => channel.clip == null && !channel.loop));

            hub.Loop = false;
            await hub.PlayAsync(nextClip, CancellationToken.None).Timeout(s_testTimeout);

            Assert.That(channels[0].clip, Is.SameAs(nextClip));
            Assert.That(channels[1].clip, Is.Null);
        });

        private AudioSource[] CreateChannels(int count)
        {
            var result = new AudioSource[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = _root.AddComponent<AudioSource>();
            }
            return result;
        }

        private InteractiveAudioHub Track(InteractiveAudioHub hub)
        {
            _hubs.Add(hub);
            return hub;
        }

        private AudioClip CreateClip(string name, float seconds)
        {
            const int sampleRate = 44100;
            var sampleCount = Mathf.Max(1, Mathf.CeilToInt(sampleRate * seconds));
            var clip = AudioClip.Create(name, sampleCount, channels: 1, frequency: sampleRate, stream: false);
            _clips.Add(clip);
            return clip;
        }

        private static UniTask WaitUntilAsync(Func<bool> predicate) =>
            UniTask.WaitUntil(predicate).Timeout(s_testTimeout);

        private static void DestroyImmediate(UnityEngine.Object obj)
        {
            if (obj != null)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}

#endif
