#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.Util;
using UnityEngine.TestTools;

namespace Radio.Tests
{
    /// <summary>
    /// Play mode tests for AudioPlayerCore.
    /// </summary>
    public class AudioPlayerCoreTests
    {
        private const string BgmIntroAddress = "radio-tests-bgm-intro";
        private const string BgmLoopAddress = "radio-tests-bgm-loop";
        private const string BgmShortAddress = "radio-tests-bgm-short";
        private const string SeAddress = "radio-tests-se";
        private const string LongSeAddress = "radio-tests-long-se";
        private const string VoiceAddress = "radio-tests-voice";
        private const string NullAddress = "radio-tests-null";
        private const string BgmIntroGuid = "1d6b962d6b6b4ef6b8e93a47cf983f12";
        private const string BgmLoopGuid = "2ce73caef02a488da8c077c3a5821059";
        private const string SeGuid = "3ac05b9eb68a4306998b67f1df97bc29";
        private const string VoiceGuid = "4e7c481f31cc4c61915d19879d73c2ab";

        private GameObject _root = null!;
        private AudioClip _bgmIntro = null!;
        private AudioClip _bgmLoop = null!;
        private AudioClip _bgmShort = null!;
        private AudioClip _se = null!;
        private AudioClip _longSe = null!;
        private AudioClip _voice = null!;
        private InMemoryAudioClipProvider _provider = null!;
        private ResourceLocationMap _locator = null!;

        [SetUp]
        public void SetUp()
        {
            PrepareAddressablesForDirectLocatorUse();
            _root = new GameObject("AudioPlayerCoreTests");
            _root.AddComponent<AudioListener>();
            _bgmIntro = CreateClip("BGM Intro", seconds: 1.0f);
            _bgmLoop = CreateClip("BGM Loop", seconds: 1.0f);
            _bgmShort = CreateClip("BGM Short", seconds: 0.05f);
            _se = CreateClip("SE", seconds: 0.01f);
            _longSe = CreateClip("Long SE", seconds: 1.0f);
            _voice = CreateClip("Voice", seconds: 0.01f);

            _provider = new InMemoryAudioClipProvider(new Dictionary<string, AudioClip?>
            {
                [BgmIntroAddress] = _bgmIntro,
                [BgmLoopAddress] = _bgmLoop,
                [BgmShortAddress] = _bgmShort,
                [SeAddress] = _se,
                [LongSeAddress] = _longSe,
                [VoiceAddress] = _voice,
                [BgmIntroGuid] = _bgmIntro,
                [BgmLoopGuid] = _bgmLoop,
                [SeGuid] = _se,
                [VoiceGuid] = _voice,
                [NullAddress] = null,
            });
            Addressables.ResourceManager.ResourceProviders.Add(_provider);

            _locator = new ResourceLocationMap("RadioTests", capacity: 10);
            AddLocation(BgmIntroAddress);
            AddLocation(BgmLoopAddress);
            AddLocation(BgmShortAddress);
            AddLocation(SeAddress);
            AddLocation(LongSeAddress);
            AddLocation(VoiceAddress);
            AddLocation(BgmIntroGuid);
            AddLocation(BgmLoopGuid);
            AddLocation(SeGuid);
            AddLocation(VoiceGuid);
            AddLocation(NullAddress);
            Addressables.AddResourceLocator(_locator);
        }

        [TearDown]
        public void TearDown()
        {
            Addressables.RemoveResourceLocator(_locator);
            Addressables.ResourceManager.ResourceProviders.Remove(_provider);

            DestroyImmediate(_root);
            DestroyImmediate(_bgmIntro);
            DestroyImmediate(_bgmLoop);
            DestroyImmediate(_bgmShort);
            DestroyImmediate(_se);
            DestroyImmediate(_longSe);
            DestroyImmediate(_voice);
        }

        [Test]
        public void ConstructorCreatesExpectedChannelsAndVolumeControlsClampAndApply()
        {
            var existingSeChannel = _root.AddComponent<AudioSource>();
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 2, useVoice: true);

            var channels = _root.GetComponents<AudioSource>();
            Assert.That(channels, Has.Length.EqualTo(4));
            Assert.That(channels[0], Is.SameAs(existingSeChannel));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => !channel.loop));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => !channel.playOnAwake));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => Mathf.Approximately(channel.volume, 0.5f)));

            player.SetMasterVolume(2.0f);
            player.SetSeVolume(-1.0f);
            player.SetVoiceVolume(0.25f);
            player.SetBgmVolume(0.4f);
            player.SetMasterVolume(0.5f);

            Assert.That(channels[0].volume, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(channels[1].volume, Is.EqualTo(0.125f).Within(0.0001f));
            Assert.That(channels[2].volume, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(channels[3].volume, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void VoiceVolumeWhenVoiceIsDisabledThrowsInvalidOperationException()
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 1, useVoice: false);

            Assert.Throws<InvalidOperationException>(() => player.SetVoiceVolume(1.0f));
        }

        [UnityTest]
        public IEnumerator PlayBgmAsyncLoadsByAddressAndReferenceRotatesChannelsAndStopAllBgmResetsState() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 2, useVoice: true);
            player.SetMasterVolume(0.8f);
            player.SetBgmVolume(0.25f);
            var channels = _root.GetComponents<AudioSource>();

            player.PlayBgmAsync(BgmIntroAddress, loop: false).Forget();
            await WaitUntilClipIsAssigned(channels[2], _bgmIntro);

            Assert.That(channels[2].clip, Is.SameAs(_bgmIntro));
            Assert.That(channels[2].loop, Is.False);
            Assert.That(channels[2].volume, Is.EqualTo(0.2f).Within(0.0001f));

            player.PlayBgmAsync(new AssetReferenceT<AudioClip>(BgmLoopGuid), loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[3], _bgmLoop);

            Assert.That(channels[3].clip, Is.SameAs(_bgmLoop));
            Assert.That(channels[3].loop, Is.True);
            Assert.That(channels[3].volume, Is.EqualTo(0.2f).Within(0.0001f));

            player.StopAllBgm();

            Assert.That(channels[2].clip, Is.Null);
            Assert.That(channels[2].loop, Is.False);
            Assert.That(channels[3].clip, Is.Null);
            Assert.That(channels[3].loop, Is.False);

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[2], _bgmLoop);

            Assert.That(channels[2].clip, Is.SameAs(_bgmLoop));
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseCompletesAfterClipLength() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 2, useVoice: false);
            var channels = _root.GetComponents<AudioSource>();
            var completed = false;

            var task = player.PlayBgmAsync(BgmShortAddress, loop: false).ContinueWith(() => completed = true);
            await WaitUntilClipIsAssigned(channels[1], _bgmShort);

            Assert.That(completed, Is.False);

            await task.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(completed, Is.True);
            Assert.That(channels[1].clip, Is.SameAs(_bgmShort));
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseCompletesWhenSameChannelIsReusedBeforeClipLength() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 2, useVoice: false);
            var channels = _root.GetComponents<AudioSource>();
            var completed = false;

            var task = player.PlayBgmAsync(BgmIntroAddress, loop: false).ContinueWith(() => completed = true);
            await WaitUntilClipIsAssigned(channels[1], _bgmIntro);

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[2], _bgmLoop);

            Assert.That(completed, Is.False);

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[1], _bgmLoop);
            await task.Timeout(TimeSpan.FromSeconds(1.0f));
            player.StopAllBgm();

            Assert.That(completed, Is.True);
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopTrueCompletesWhenSameChannelIsReused() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 2, useVoice: false);
            var channels = _root.GetComponents<AudioSource>();
            var completed = false;

            var task = player.PlayBgmAsync(BgmIntroAddress, loop: true).ContinueWith(() => completed = true);
            await WaitUntilClipIsAssigned(channels[1], _bgmIntro);

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[2], _bgmLoop);

            Assert.That(completed, Is.False);

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[1], _bgmLoop);
            await task.Timeout(TimeSpan.FromSeconds(1.0f));
            player.StopAllBgm();

            Assert.That(completed, Is.True);
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenCancelledAfterPlaybackStartsStopsBgm() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 2, useVoice: false);
            using var cts = new CancellationTokenSource();
            var channels = _root.GetComponents<AudioSource>();

            var task = player.PlayBgmAsync(BgmIntroAddress, loop: true, cts.Token);
            await WaitUntilClipIsAssigned(channels[1], _bgmIntro);

            cts.Cancel();

            var exceptionThrown = false;
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                exceptionThrown = true;
            }

            Assert.That(exceptionThrown, Is.True);
            Assert.That(channels[1].clip, Is.Null);
            Assert.That(channels[1].loop, Is.False);
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseIsCancelledAfterPlaybackStartsStopsBgm() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 2, useVoice: false);
            using var cts = new CancellationTokenSource();
            var channels = _root.GetComponents<AudioSource>();

            var task = player.PlayBgmAsync(BgmIntroAddress, loop: false, cts.Token);
            await WaitUntilClipIsAssigned(channels[1], _bgmIntro);

            cts.Cancel();

            var exceptionThrown = false;
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                exceptionThrown = true;
            }

            Assert.That(exceptionThrown, Is.True);
            Assert.That(channels[1].clip, Is.Null);
            Assert.That(channels[1].loop, Is.False);
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenReferencePlaybackIsCancelledStopsOnlyItsBgmChannel() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 2, useVoice: false);
            using var cts = new CancellationTokenSource();
            var channels = _root.GetComponents<AudioSource>();

            player.PlayBgmAsync(BgmIntroAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[1], _bgmIntro);

            var task = player.PlayBgmAsync(new AssetReferenceT<AudioClip>(BgmLoopGuid), loop: true, cts.Token);
            await WaitUntilClipIsAssigned(channels[2], _bgmLoop);

            cts.Cancel();

            var exceptionThrown = false;
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                exceptionThrown = true;
            }

            Assert.That(exceptionThrown, Is.True);
            Assert.That(channels[1].clip, Is.SameAs(_bgmIntro));
            Assert.That(channels[1].loop, Is.True);
            Assert.That(channels[2].clip, Is.Null);
            Assert.That(channels[2].loop, Is.False);
        });

        [UnityTest]
        public IEnumerator PlaySeAndVoiceAsyncLoadAndReleaseAddressableClips() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 1, useVoice: true);
            player.SetMasterVolume(0.8f);
            player.SetSeVolume(0.5f);
            player.SetVoiceVolume(0.25f);
            var channels = _root.GetComponents<AudioSource>();

            await player.PlaySeAsync(SeAddress);
            await player.PlaySeAsync(new AssetReferenceT<AudioClip>(SeGuid));
            await player.PlayVoiceAsync(VoiceAddress);
            await player.PlayVoiceAsync(new AssetReferenceT<AudioClip>(VoiceGuid));

            Assert.That(channels[0].volume, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(channels[1].volume, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(_provider.LoadCount(SeAddress), Is.EqualTo(1));
            Assert.That(_provider.LoadCount(SeGuid), Is.EqualTo(1));
            Assert.That(_provider.LoadCount(VoiceAddress), Is.EqualTo(1));
            Assert.That(_provider.LoadCount(VoiceGuid), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlaySeAsyncWhenProviderReturnsNullLogsErrorAndReleasesHandle() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root);
            LogAssert.Expect(LogType.Error, new Regex("Failed to load SE: .*"));

            await player.PlaySeAsync(NullAddress);

            Assert.That(_provider.LoadCount(NullAddress), Is.EqualTo(1));
        });

        [Test]
        public void PlaySeAndVoiceAsyncWhenCancelledBeforeLoadThrowOperationCanceledException()
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 1, useVoice: true);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => _ = player.PlaySeAsync(SeAddress, cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.PlaySeAsync(new AssetReferenceT<AudioClip>(SeGuid), cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.PlayVoiceAsync(VoiceAddress, cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.PlayVoiceAsync(new AssetReferenceT<AudioClip>(VoiceGuid), cts.Token));
        }

        [UnityTest]
        public IEnumerator PlaySeAsyncWhenCancelledWhileWaitingForClipLengthThrowsOperationCanceledException() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 1, useVoice: false);
            using var cts = new CancellationTokenSource();

            var task = player.PlaySeAsync(LongSeAddress, cts.Token);
            await UniTask.Yield();
            cts.Cancel();

            var exceptionThrown = false;
            try
            {
                await task;
            }
            catch (OperationCanceledException e) when (e.CancellationToken == cts.Token)
            {
                exceptionThrown = true;
            }

            Assert.That(exceptionThrown, Is.True);
            Assert.That(_provider.LoadCount(LongSeAddress), Is.EqualTo(1));
        });

        [Test]
        public void PlayVoiceAsyncWhenVoiceIsDisabledThrowsInvalidOperationException()
        {
            using var player = new AudioPlayerCore(_root, bgmChannelCount: 1, useVoice: false);

            Assert.Throws<InvalidOperationException>(() => _ = player.PlayVoiceAsync(VoiceAddress));
        }

        [UnityTest]
        public IEnumerator DisposeReleasesLoadedBgmHandles() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayerCore(_root, bgmChannelCount: 1, useVoice: false);

            player.PlayBgmAsync(BgmIntroAddress).Forget();
            await WaitUntilClipIsAssigned(_root.GetComponents<AudioSource>()[1], _bgmIntro);
            player.Dispose();

            Assert.That(_provider.LoadCount(BgmIntroAddress), Is.EqualTo(1));
        });

#if ENABLE_LITMOTION
        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenCancelledBeforeLoadThrowsOperationCanceledException() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, TimeSpan.FromSeconds(0.05f), bgmChannelCount: 2, useVoice: true);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var addressExceptionThrown = false;
            try
            {
                await player.CrossFadeBgmAsync(BgmIntroAddress, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException e) when (e.CancellationToken == cts.Token)
            {
                addressExceptionThrown = true;
            }

            var referenceExceptionThrown = false;
            try
            {
                await player.CrossFadeBgmAsync(new AssetReferenceT<AudioClip>(BgmIntroGuid), cancellationToken: cts.Token);
            }
            catch (OperationCanceledException e) when (e.CancellationToken == cts.Token)
            {
                referenceExceptionThrown = true;
            }

            Assert.That(addressExceptionThrown, Is.True);
            Assert.That(referenceExceptionThrown, Is.True);
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncFirstTrackFadesInToManagedBgmVolume() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, TimeSpan.FromSeconds(0.05f), bgmChannelCount: 2, useVoice: true);
            player.SetMasterVolume(0.8f);
            player.SetBgmVolume(0.25f);
            var channels = _root.GetComponents<AudioSource>();

            await player.CrossFadeBgmAsync(BgmIntroAddress, loop: false);

            Assert.That(channels[2].clip, Is.SameAs(_bgmIntro));
            Assert.That(channels[2].loop, Is.False);
            Assert.That(channels[2].volume, Is.EqualTo(0.2f).Within(0.02f));
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenTrackAlreadyPlayingCrossFadesToNextChannelAndClearsPreviousClip() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, TimeSpan.FromSeconds(0.05f), bgmChannelCount: 2, useVoice: true);
            var channels = _root.GetComponents<AudioSource>();

            await player.CrossFadeBgmAsync(BgmIntroAddress, loop: true);
            channels[2].time = 0.1f;

            await player.CrossFadeBgmAsync(new AssetReferenceT<AudioClip>(BgmLoopGuid), loop: false);

            Assert.That(channels[2].clip, Is.Null);
            Assert.That(channels[3].clip, Is.SameAs(_bgmLoop));
            Assert.That(channels[3].loop, Is.False);
            Assert.That(channels[3].time, Is.GreaterThan(0.0f));
            Assert.That(channels[3].volume, Is.EqualTo(0.25f).Within(0.02f));
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenInterruptedCancelsPreviousFadeAndKeepsLatestTrack() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, TimeSpan.FromSeconds(0.2f), bgmChannelCount: 2, useVoice: true);
            player.SetMasterVolume(1.0f);
            player.SetBgmVolume(1.0f);
            var channels = _root.GetComponents<AudioSource>();

            var firstTask = player.CrossFadeBgmAsync(BgmIntroAddress, loop: true);
            await WaitUntilClipIsAssigned(channels[2], _bgmIntro);
            await UniTask.WaitUntil(() => channels[2].volume > 0.05f && channels[2].volume < 0.95f).Timeout(TimeSpan.FromSeconds(1.0f));

            var secondTask = player.CrossFadeBgmAsync(BgmLoopAddress, loop: true);
            await WaitUntilClipIsAssigned(channels[3], _bgmLoop);
            await UniTask.WaitUntil(() => channels[3].volume > 0.05f && channels[3].volume < 0.95f).Timeout(TimeSpan.FromSeconds(1.0f));

            var thirdTask = player.CrossFadeBgmAsync(BgmShortAddress, loop: true);
            await WaitUntilClipIsAssigned(channels[2], _bgmShort);

            var firstCancelled = await firstTask.SuppressCancellationThrow();
            var secondCancelled = await secondTask.SuppressCancellationThrow();
            await thirdTask.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(firstCancelled, Is.True);
            Assert.That(secondCancelled, Is.True);
            Assert.That(channels[2].clip, Is.SameAs(_bgmShort));
            Assert.That(channels[2].loop, Is.True);
            Assert.That(channels[2].volume, Is.EqualTo(1.0f).Within(0.02f));
            Assert.That(channels[3].clip, Is.Null);
            Assert.That(channels[3].isPlaying, Is.False);
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenBgmVolumeChangesDuringFadeKeepsFadeOwnedVolumes() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, TimeSpan.FromSeconds(0.25f), bgmChannelCount: 2, useVoice: true);
            player.SetMasterVolume(1.0f);
            player.SetBgmVolume(1.0f);
            var channels = _root.GetComponents<AudioSource>();

            await player.CrossFadeBgmAsync(BgmIntroAddress, loop: true);

            var task = player.CrossFadeBgmAsync(BgmLoopAddress, loop: true);
            await WaitUntilClipIsAssigned(channels[3], _bgmLoop);
            await UniTask.WaitUntil(() => channels[3].volume > 0.2f && channels[3].volume < 0.4f).Timeout(TimeSpan.FromSeconds(1.0f));
            var currentVolumeBefore = channels[2].volume;
            var nextVolumeBefore = channels[3].volume;

            player.SetBgmVolume(0.5f);

            Assert.That(channels[2].volume, Is.EqualTo(currentVolumeBefore).Within(0.02f));
            Assert.That(channels[3].volume, Is.EqualTo(nextVolumeBefore).Within(0.02f));

            await task.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(channels[2].clip, Is.Null);
            Assert.That(channels[3].clip, Is.SameAs(_bgmLoop));
            Assert.That(channels[3].volume, Is.EqualTo(0.5f).Within(0.02f));
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenInterruptedStartsFromCurrentVolumeWithoutBoosting() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, TimeSpan.FromSeconds(0.4f), bgmChannelCount: 2, useVoice: true);
            player.SetMasterVolume(1.0f);
            player.SetBgmVolume(1.0f);
            var channels = _root.GetComponents<AudioSource>();

            var firstTask = player.CrossFadeBgmAsync(BgmIntroAddress, loop: true);
            await WaitUntilClipIsAssigned(channels[2], _bgmIntro);
            await UniTask.WaitUntil(() => channels[2].volume > 0.2f && channels[2].volume < 0.4f).Timeout(TimeSpan.FromSeconds(1.0f));
            var volumeBeforeInterrupt = channels[2].volume;

            var secondTask = player.CrossFadeBgmAsync(BgmLoopAddress, loop: true);
            await WaitUntilClipIsAssigned(channels[3], _bgmLoop);
            await UniTask.WaitUntil(() => channels[3].volume > 0.02f).Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(channels[2].volume, Is.LessThanOrEqualTo(volumeBeforeInterrupt + 0.05f));

            var firstCancelled = await firstTask.SuppressCancellationThrow();
            await secondTask.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(firstCancelled, Is.True);
            Assert.That(channels[2].clip, Is.Null);
            Assert.That(channels[3].clip, Is.SameAs(_bgmLoop));
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenManagedBgmVolumeIsZeroKeepsTransitionSilent() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore(_root, TimeSpan.FromSeconds(0.05f), bgmChannelCount: 2, useVoice: true);
            player.SetMasterVolume(0.0f);
            player.SetBgmVolume(1.0f);
            var channels = _root.GetComponents<AudioSource>();

            await player.CrossFadeBgmAsync(BgmIntroAddress, loop: true);
            await player.CrossFadeBgmAsync(BgmLoopAddress, loop: true);

            Assert.That(channels[2].clip, Is.Null);
            Assert.That(channels[2].volume, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(channels[3].clip, Is.SameAs(_bgmLoop));
            Assert.That(channels[3].volume, Is.EqualTo(0.0f).Within(0.0001f));
        });
#endif

        private void AddLocation(string key)
        {
            _locator.Add(key, new ResourceLocationBase(key, key, _provider.ProviderId, typeof(AudioClip)));
        }

        private static void PrepareAddressablesForDirectLocatorUse()
        {
            var addressablesInstanceField = typeof(Addressables).GetField("m_AddressablesInstance", BindingFlags.NonPublic | BindingFlags.Static);
            var addressablesImplType = addressablesInstanceField!.GetValue(null)!.GetType();
            var addressablesInstance = Activator.CreateInstance(addressablesImplType, new LRUCacheAllocationStrategy(1000, 1000, 100, 10));
            addressablesInstanceField.SetValue(null, addressablesInstance);
            addressablesImplType.GetField("hasStartedInitialization", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(addressablesInstance, true);
            addressablesImplType.GetField("m_InitializationOperation", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(addressablesInstance, default(AsyncOperationHandle<IResourceLocator>));
            addressablesImplType.GetField("m_OnHandleCompleteAction", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(addressablesInstance, new Action<AsyncOperationHandle>(_ => { }));
        }

        private static AudioClip CreateClip(string name, float seconds)
        {
            var sampleRate = 44100;
            var samples = Mathf.CeilToInt(sampleRate * seconds);
            return AudioClip.Create(name, samples, channels: 1, frequency: sampleRate, stream: false);
        }

        private static UniTask WaitUntilClipIsAssigned(AudioSource channel, AudioClip clip)
        {
            return UniTask.WaitUntil((channel, clip), static args => args.channel.clip == args.clip).Timeout(TimeSpan.FromSeconds(1.0f));
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