#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using AndanteTribe.Unity.Extensions;
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
    /// Play mode tests for Addressables loading and resource ownership.
    /// </summary>
    public class AddressableAudioPlayerTests
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
            _root = new GameObject("AddressableAudioPlayerTests");
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
            UnityEngine.AddressableAssets.Addressables.ResourceManager.ResourceProviders.Add(_provider);

            _locator = new ResourceLocationMap("RadioTests", capacity: 11);
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
            UnityEngine.AddressableAssets.Addressables.AddResourceLocator(_locator);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.AddressableAssets.Addressables.RemoveResourceLocator(_locator);
            UnityEngine.AddressableAssets.Addressables.ResourceManager.ResourceProviders.Remove(_provider);

            DestroyImmediate(_root);
            DestroyImmediate(_bgmIntro);
            DestroyImmediate(_bgmLoop);
            DestroyImmediate(_bgmShort);
            DestroyImmediate(_se);
            DestroyImmediate(_longSe);
            DestroyImmediate(_voice);
        }

        [UnityTest]
        public IEnumerator PlayBgmAsyncLoadsBothKeyTypesKeepsHandlesAndStopAllReleasesThem() => UniTask.ToCoroutine(async () =>
        {
            var registry = new AssetsRegistry();
            using var player = new AddressableAudioPlayer(
                _root,
                bgmChannelCount: 2,
                useVoice: true,
                bgmRegistry: registry);

            player.PlayBgmAsync(BgmIntroAddress, loop: false).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmIntro);
            player.PlayBgmAsync(new AssetReferenceT<AudioClip>(BgmLoopGuid)).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[1], _bgmLoop);

            Assert.That(registry.Count, Is.EqualTo(2));
            Assert.That(player.Sources.Bgm[0].loop, Is.False);
            Assert.That(player.Sources.Bgm[1].loop, Is.True);

            player.StopAllBgm();

            Assert.That(registry.Count, Is.Zero);
            Assert.That(player.Sources.Bgm[0].clip, Is.Null);
            Assert.That(player.Sources.Bgm[1].clip, Is.Null);
            Assert.That(_provider.ReleaseCount(BgmIntroAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(BgmLoopGuid), Is.EqualTo(1));

            player.PlayBgmAsync(BgmLoopAddress).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmLoop);
            player.StopAllBgm();

            Assert.That(_provider.ReleaseCount(BgmLoopAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseCompletesAfterLoadedClipLength() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AddressableAudioPlayer(_root, bgmChannelCount: 1);

            var task = player.PlayBgmAsync(BgmShortAddress, loop: false);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmShort);
            await task.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(player.Sources.Bgm[0].clip, Is.SameAs(_bgmShort));
            player.StopAllBgm();
        });

        [UnityTest]
        public IEnumerator DirectClipOverloadsRemainAvailableOnAddressablePlayer() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AddressableAudioPlayer(
                _root,
                bgmChannelCount: 2,
                useVoice: true);

            await player.PlayBgmAsync(_bgmShort, loop: false);
            await player.PlaySeAsync(_se);
            await player.PlayVoiceAsync(_voice);

            Assert.Throws<InvalidOperationException>(
                () => _ = player.CrossFadeBgmAsync(_bgmIntro));
            Assert.That(player.Sources.Bgm[0].clip, Is.SameAs(_bgmShort));
            player.StopAllBgm();
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenCancelledStopsOnlyItsChannelAndRetainsHandleUntilStop() => UniTask.ToCoroutine(async () =>
        {
            var registry = new AssetsRegistry();
            using var player = new AddressableAudioPlayer(_root, bgmChannelCount: 2, bgmRegistry: registry);
            using var cts = new CancellationTokenSource();

            player.PlayBgmAsync(BgmIntroAddress).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmIntro);

            var task = player.PlayBgmAsync(
                new AssetReferenceT<AudioClip>(BgmLoopGuid),
                cancellationToken: cts.Token);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[1], _bgmLoop);
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
            Assert.That(player.Sources.Bgm[0].clip, Is.SameAs(_bgmIntro));
            Assert.That(player.Sources.Bgm[1].clip, Is.Null);
            Assert.That(registry.Count, Is.EqualTo(2));

            player.StopAllBgm();

            Assert.That(registry.Count, Is.Zero);
        });

        [UnityTest]
        public IEnumerator PlaySeAndVoiceAsyncLoadPlayAndReleaseBothKeyTypes() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AddressableAudioPlayer(_root, bgmChannelCount: 1, useVoice: true);
            player.SetMasterVolume(0.8f);
            player.SetSeVolume(0.5f);
            player.SetVoiceVolume(0.25f);

            await player.PlaySeAsync(SeAddress);
            await player.PlaySeAsync(new AssetReferenceT<AudioClip>(SeGuid));
            await player.PlayVoiceAsync(VoiceAddress);
            await player.PlayVoiceAsync(new AssetReferenceT<AudioClip>(VoiceGuid));

            Assert.That(player.Sources.Se.volume, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(player.Sources.Voice!.volume, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(_provider.LoadCount(SeAddress), Is.EqualTo(1));
            Assert.That(_provider.LoadCount(SeGuid), Is.EqualTo(1));
            Assert.That(_provider.LoadCount(VoiceAddress), Is.EqualTo(1));
            Assert.That(_provider.LoadCount(VoiceGuid), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(SeAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(SeGuid), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(VoiceAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(VoiceGuid), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlaySeAsyncWhenProviderReturnsNullLogsErrorAndReleasesHandle() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AddressableAudioPlayer(_root);
            LogAssert.Expect(LogType.Error, new Regex("Failed to load SE: .*"));

            await player.PlaySeAsync(NullAddress);

            Assert.That(_provider.LoadCount(NullAddress), Is.EqualTo(1));
        });

        [Test]
        public void PlaySeAndVoiceAsyncWhenCancelledBeforeLoadThrowOperationCanceledException()
        {
            using var player = new AddressableAudioPlayer(_root, bgmChannelCount: 1, useVoice: true);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => _ = player.PlaySeAsync(SeAddress, cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.PlaySeAsync(new AssetReferenceT<AudioClip>(SeGuid), cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.PlayVoiceAsync(VoiceAddress, cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.PlayVoiceAsync(new AssetReferenceT<AudioClip>(VoiceGuid), cts.Token));
            Assert.That(_provider.LoadCount(SeAddress), Is.Zero);
            Assert.That(_provider.LoadCount(VoiceAddress), Is.Zero);
        }

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenCancelledBeforeLoadThrowsOperationCanceledException() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AddressableAudioPlayer(_root);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            CancellationToken? addressCancellationToken = null;
            try
            {
                await player.PlayBgmAsync(
                    BgmIntroAddress,
                    cancellationToken: cts.Token);
            }
            catch (OperationCanceledException e)
            {
                addressCancellationToken = e.CancellationToken;
            }
            var referenceCancelled = await player
                .PlayBgmAsync(new AssetReferenceT<AudioClip>(BgmIntroGuid), cancellationToken: cts.Token)
                .SuppressCancellationThrow();

            Assert.That(addressCancellationToken, Is.EqualTo(cts.Token));
            Assert.That(referenceCancelled, Is.True);
            Assert.That(_provider.LoadCount(BgmIntroAddress), Is.Zero);
            Assert.That(_provider.LoadCount(BgmIntroGuid), Is.Zero);
        });

        [UnityTest]
        public IEnumerator CrossFadeBgmAsyncWhenCancelledBeforeLoadDoesNotStartEitherKeyType() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AddressableAudioPlayer(_root);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var addressCancelled = await player
                .CrossFadeBgmAsync(BgmIntroAddress, cancellationToken: cts.Token)
                .SuppressCancellationThrow();
            var referenceCancelled = await player
                .CrossFadeBgmAsync(
                    new AssetReferenceT<AudioClip>(BgmIntroGuid),
                    cancellationToken: cts.Token)
                .SuppressCancellationThrow();

            Assert.That(addressCancelled, Is.True);
            Assert.That(referenceCancelled, Is.True);
            Assert.That(_provider.LoadCount(BgmIntroAddress), Is.Zero);
            Assert.That(_provider.LoadCount(BgmIntroGuid), Is.Zero);
        });

        [UnityTest]
        public IEnumerator PlaySeAsyncWhenCancelledDuringPlaybackReleasesHandle() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AddressableAudioPlayer(_root);
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
            Assert.That(_provider.ReleaseCount(LongSeAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayVoiceAsyncWhenCancelledDuringPlaybackReleasesHandle() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AddressableAudioPlayer(_root, useVoice: true);
            using var cts = new CancellationTokenSource();

            var task = player.PlayVoiceAsync(VoiceAddress, cts.Token);
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
            Assert.That(_provider.LoadCount(VoiceAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(VoiceAddress), Is.EqualTo(1));
        });

        [Test]
        public void PlayVoiceAsyncWhenVoiceIsDisabledThrowsBeforeLoading()
        {
            using var player = new AddressableAudioPlayer(_root, useVoice: false);

            Assert.Throws<InvalidOperationException>(() => _ = player.PlayVoiceAsync(VoiceAddress));
            Assert.Throws<InvalidOperationException>(() => _ = player.PlayVoiceAsync(new AssetReferenceT<AudioClip>(VoiceGuid)));
            Assert.That(_provider.LoadCount(VoiceAddress), Is.Zero);
            Assert.That(_provider.LoadCount(VoiceGuid), Is.Zero);
        }

        [UnityTest]
        public IEnumerator DisposeReleasesBgmHandlesWithoutStoppingPlayback() => UniTask.ToCoroutine(async () =>
        {
            var registry = new AssetsRegistry();
            var player = new AddressableAudioPlayer(_root, bgmChannelCount: 1, bgmRegistry: registry);

            player.PlayBgmAsync(BgmIntroAddress).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmIntro);
            Assert.That(registry.Count, Is.EqualTo(1));

            player.Dispose();

            Assert.That(registry.Count, Is.Zero);
            Assert.That(_provider.ReleaseCount(BgmIntroAddress), Is.EqualTo(1));
            Assert.That(player.Sources.Bgm[0].clip, Is.SameAs(_bgmIntro));

            player.Dispose();
        });

        private void AddLocation(string key)
        {
            _locator.Add(key, new ResourceLocationBase(key, key, _provider.ProviderId, typeof(AudioClip)));
        }

        private static void PrepareAddressablesForDirectLocatorUse()
        {
            var addressablesInstanceField = typeof(UnityEngine.AddressableAssets.Addressables)
                .GetField("m_AddressablesInstance", BindingFlags.NonPublic | BindingFlags.Static);
            var addressablesImplType = addressablesInstanceField!.GetValue(null)!.GetType();
            var addressablesInstance = Activator.CreateInstance(
                addressablesImplType,
                new LRUCacheAllocationStrategy(1000, 1000, 100, 10));
            addressablesInstanceField.SetValue(null, addressablesInstance);
            addressablesImplType
                .GetField("hasStartedInitialization", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(addressablesInstance, true);
            addressablesImplType
                .GetField("m_InitializationOperation", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(addressablesInstance, default(AsyncOperationHandle<IResourceLocator>));
            addressablesImplType
                .GetField("m_OnHandleCompleteAction", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(addressablesInstance, new Action<AsyncOperationHandle>(_ => { }));
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
