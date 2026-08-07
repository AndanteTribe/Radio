#nullable enable

using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Radio.Tests
{
    /// <summary>
    /// Play mode tests for the optional LitMotion transition.
    /// </summary>
    public class LitMotionCrossFadeTests
    {
        private GameObject _root = null!;
        private AudioClip _bgmIntro = null!;
        private AudioClip _bgmLoop = null!;
        private AudioClip _bgmShort = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("LitMotionCrossFadeTests");
            _root.AddComponent<AudioListener>();
            _bgmIntro = CreateClip("BGM Intro", seconds: 1.0f);
            _bgmLoop = CreateClip("BGM Loop", seconds: 1.0f);
            _bgmShort = CreateClip("BGM Short", seconds: 0.05f);
        }

        [TearDown]
        public void TearDown()
        {
            DestroyImmediate(_root);
            DestroyImmediate(_bgmIntro);
            DestroyImmediate(_bgmLoop);
            DestroyImmediate(_bgmShort);
        }

        [Test]
        public void UseLitMotionCrossFadeReturnsTheConfiguredPlayer()
        {
            var player = new AudioPlayer(_root);

            var configuredPlayer = player.UseLitMotionCrossFade(TimeSpan.FromSeconds(0.05f));

            Assert.That(configuredPlayer, Is.SameAs(player));
        }

        [Test]
        public void UseLitMotionCrossFadeRejectsFewerThanTwoBgmChannels()
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 1);

            Assert.Throws<InvalidOperationException>(
                () => player.UseLitMotionCrossFade(TimeSpan.FromSeconds(0.05f)));
        }

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncFirstTrackFadesInToManagedBgmVolume() => UniTask.ToCoroutine(async () =>
        {
            var player = CreatePlayer(fadeSeconds: 0.05f);
            player.SetMasterVolume(0.8f);
            player.SetBgmVolume(0.25f);

            await player.CrossFadeBgmAsync(_bgmIntro, loop: false);

            Assert.That(player.Sources.Bgm[0].clip, Is.SameAs(_bgmIntro));
            Assert.That(player.Sources.Bgm[0].loop, Is.False);
            Assert.That(player.Sources.Bgm[0].volume, Is.EqualTo(0.2f).Within(0.02f));
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenTrackAlreadyPlayingCrossFadesAndClearsPreviousClip() => UniTask.ToCoroutine(async () =>
        {
            var player = CreatePlayer(fadeSeconds: 0.05f);

            await player.CrossFadeBgmAsync(_bgmIntro);
            player.Sources.Bgm[0].time = 0.1f;
            await player.CrossFadeBgmAsync(_bgmLoop, loop: false);

            Assert.That(player.Sources.Bgm[0].clip, Is.Null);
            Assert.That(player.Sources.Bgm[1].clip, Is.SameAs(_bgmLoop));
            Assert.That(player.Sources.Bgm[1].loop, Is.False);
            Assert.That(player.Sources.Bgm[1].time, Is.GreaterThanOrEqualTo(0.0f).And.LessThan(_bgmLoop.length));
            Assert.That(player.Sources.Bgm[1].volume, Is.EqualTo(0.25f).Within(0.02f));
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenInterruptedCancelsPreviousFadeAndKeepsLatestTrack() => UniTask.ToCoroutine(async () =>
        {
            var player = CreatePlayer(fadeSeconds: 0.2f);
            player.SetMasterVolume(1.0f);
            player.SetBgmVolume(1.0f);

            var firstTask = player.CrossFadeBgmAsync(_bgmIntro);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmIntro);
            await UniTask
                .WaitUntil(() => player.Sources.Bgm[0].volume > 0.05f && player.Sources.Bgm[0].volume < 0.95f)
                .Timeout(TimeSpan.FromSeconds(1.0f));

            var secondTask = player.CrossFadeBgmAsync(_bgmLoop);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[1], _bgmLoop);
            await UniTask
                .WaitUntil(() => player.Sources.Bgm[1].volume > 0.05f && player.Sources.Bgm[1].volume < 0.95f)
                .Timeout(TimeSpan.FromSeconds(1.0f));

            var thirdTask = player.CrossFadeBgmAsync(_bgmShort);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmShort);

            var firstCancelled = await firstTask.SuppressCancellationThrow();
            var secondCancelled = await secondTask.SuppressCancellationThrow();
            await thirdTask.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(firstCancelled, Is.True);
            Assert.That(secondCancelled, Is.True);
            Assert.That(player.Sources.Bgm[0].clip, Is.SameAs(_bgmShort));
            Assert.That(player.Sources.Bgm[0].loop, Is.True);
            Assert.That(player.Sources.Bgm[0].time, Is.LessThan(_bgmShort.length));
            Assert.That(player.Sources.Bgm[0].volume, Is.EqualTo(1.0f).Within(0.02f));
            Assert.That(player.Sources.Bgm[1].clip, Is.Null);
            Assert.That(player.Sources.Bgm[1].isPlaying, Is.False);
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenBgmVolumeChangesDuringFadeKeepsFadeOwnedVolumes() => UniTask.ToCoroutine(async () =>
        {
            var player = CreatePlayer(fadeSeconds: 0.25f);
            player.SetMasterVolume(1.0f);
            player.SetBgmVolume(1.0f);
            await player.CrossFadeBgmAsync(_bgmIntro);

            var task = player.CrossFadeBgmAsync(_bgmLoop);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[1], _bgmLoop);
            await UniTask
                .WaitUntil(() => player.Sources.Bgm[1].volume > 0.2f && player.Sources.Bgm[1].volume < 0.4f)
                .Timeout(TimeSpan.FromSeconds(1.0f));
            var currentVolumeBefore = player.Sources.Bgm[0].volume;
            var nextVolumeBefore = player.Sources.Bgm[1].volume;

            player.SetBgmVolume(0.5f);
            player.SetMasterVolume(1.0f);

            Assert.That(player.Sources.Bgm[0].volume, Is.EqualTo(currentVolumeBefore).Within(0.02f));
            Assert.That(player.Sources.Bgm[1].volume, Is.EqualTo(nextVolumeBefore).Within(0.02f));

            await task.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(player.Sources.Bgm[0].clip, Is.Null);
            Assert.That(player.Sources.Bgm[1].clip, Is.SameAs(_bgmLoop));
            Assert.That(player.Sources.Bgm[1].volume, Is.EqualTo(0.5f).Within(0.02f));
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenInterruptedStartsFromCurrentVolumeWithoutBoosting() => UniTask.ToCoroutine(async () =>
        {
            var player = CreatePlayer(fadeSeconds: 0.4f);
            player.SetMasterVolume(1.0f);
            player.SetBgmVolume(1.0f);

            var firstTask = player.CrossFadeBgmAsync(_bgmIntro);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmIntro);
            await UniTask
                .WaitUntil(() => player.Sources.Bgm[0].volume > 0.2f && player.Sources.Bgm[0].volume < 0.4f)
                .Timeout(TimeSpan.FromSeconds(1.0f));
            var volumeBeforeInterrupt = player.Sources.Bgm[0].volume;

            var secondTask = player.CrossFadeBgmAsync(_bgmLoop);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[1], _bgmLoop);
            await UniTask
                .WaitUntil(() => player.Sources.Bgm[1].volume > 0.02f)
                .Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(player.Sources.Bgm[0].volume, Is.LessThanOrEqualTo(volumeBeforeInterrupt + 0.05f));

            var firstCancelled = await firstTask.SuppressCancellationThrow();
            await secondTask.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(firstCancelled, Is.True);
            Assert.That(player.Sources.Bgm[0].clip, Is.Null);
            Assert.That(player.Sources.Bgm[1].clip, Is.SameAs(_bgmLoop));
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenManagedBgmVolumeIsZeroKeepsTransitionSilent() => UniTask.ToCoroutine(async () =>
        {
            var player = CreatePlayer(fadeSeconds: 0.05f);
            player.SetMasterVolume(0.0f);
            player.SetBgmVolume(1.0f);

            await player.CrossFadeBgmAsync(_bgmIntro);
            await player.CrossFadeBgmAsync(_bgmLoop);

            Assert.That(player.Sources.Bgm[0].clip, Is.Null);
            Assert.That(player.Sources.Bgm[0].volume, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(player.Sources.Bgm[1].clip, Is.SameAs(_bgmLoop));
            Assert.That(player.Sources.Bgm[1].volume, Is.EqualTo(0.0f).Within(0.0001f));
        });

        private AudioPlayer CreatePlayer(float fadeSeconds)
        {
            return new AudioPlayer(_root, bgmChannelCount: 2, useVoice: true)
                .UseLitMotionCrossFade(TimeSpan.FromSeconds(fadeSeconds));
        }

        private static AudioClip CreateClip(string name, float seconds)
        {
            const int sampleRate = 44100;
            var samples = Mathf.CeilToInt(sampleRate * seconds);
            return AudioClip.Create(name, samples, channels: 1, frequency: sampleRate, stream: false);
        }

        private static UniTask WaitUntilClipIsAssigned(AudioSource channel, AudioClip clip)
        {
            return UniTask.WaitUntil(
                    (channel, clip),
                    static args => args.channel.clip == args.clip)
                .Timeout(TimeSpan.FromSeconds(1.0f));
        }

        private static void DestroyImmediate(UnityEngine.Object obj)
        {
            if (obj != null)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
