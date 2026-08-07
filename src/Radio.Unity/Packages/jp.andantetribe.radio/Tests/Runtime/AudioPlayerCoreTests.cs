#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
            // Ver2は既存のAudioSourceを再利用せず、常に新規追加する。既存分はそのまま手つかずで残る。
            var existingAudioSource = _root.AddComponent<AudioSource>();
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 2, useVoice: true);

            var channels = _root.GetComponents<AudioSource>();
            Assert.That(channels, Has.Length.EqualTo(5)); // 既存1つ + BGM2 + SE1 + Voice1
            Assert.That(channels[0], Is.SameAs(existingAudioSource));
            Assert.That(existingAudioSource.playOnAwake, Is.True); // Unityのデフォルト値のまま変更されていない

            var created = new ArraySegment<AudioSource>(channels, 1, channels.Length - 1);
            Assert.That(created, Has.All.Matches<AudioSource>(channel => !channel.loop));
            Assert.That(created, Has.All.Matches<AudioSource>(channel => !channel.playOnAwake));
            Assert.That(created, Has.All.Matches<AudioSource>(channel => Mathf.Approximately(channel.volume, 0.5f)));

            player.SetMasterVolume(2.0f);
            player.SetSeVolume(-1.0f);
            player.SetVoiceVolume(0.25f);
            player.SetBgmVolume(0.4f);
            player.SetMasterVolume(0.5f);

            // channels[1],[2] = BGM, channels[3] = SE, channels[4] = Voice
            Assert.That(channels[3].volume, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(channels[4].volume, Is.EqualTo(0.125f).Within(0.0001f));
            Assert.That(channels[1].volume, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(channels[2].volume, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void VoiceVolumeWhenVoiceIsDisabledThrowsInvalidOperationException()
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 1, useVoice: false);

            Assert.Throws<InvalidOperationException>(() => player.SetVoiceVolume(1.0f));
        }

        [UnityTest]
        public IEnumerator PlayBgmAsyncLoadsByAddressRotatesChannelsAndStopAllBgmResetsState() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 2, useVoice: true);
            player.SetMasterVolume(0.8f);
            player.SetBgmVolume(0.25f);
            var channels = _root.GetComponents<AudioSource>(); // [bgm0, bgm1, se, voice]

            player.PlayBgmAsync(BgmIntroAddress, loop: false).Forget();
            await WaitUntilClipIsAssigned(channels[0], _bgmIntro);

            Assert.That(channels[0].clip, Is.SameAs(_bgmIntro));
            Assert.That(channels[0].loop, Is.False);
            Assert.That(channels[0].volume, Is.EqualTo(0.2f).Within(0.0001f));

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[1], _bgmLoop);

            Assert.That(channels[1].clip, Is.SameAs(_bgmLoop));
            Assert.That(channels[1].loop, Is.True);
            Assert.That(channels[1].volume, Is.EqualTo(0.2f).Within(0.0001f));

            player.StopAllBgm();

            Assert.That(channels[0].clip, Is.Null);
            Assert.That(channels[0].loop, Is.False);
            Assert.That(channels[1].clip, Is.Null);
            Assert.That(channels[1].loop, Is.False);
            Assert.That(_provider.ReleaseCount(BgmIntroAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(BgmLoopAddress), Is.EqualTo(1));

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[0], _bgmLoop);

            Assert.That(channels[0].clip, Is.SameAs(_bgmLoop));
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseCompletesAfterClipLength() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 2, useVoice: false);
            var channels = _root.GetComponents<AudioSource>();
            var completed = false;

            var task = player.PlayBgmAsync(BgmShortAddress, loop: false).ContinueWith(() => completed = true);
            await WaitUntilClipIsAssigned(channels[0], _bgmShort);

            Assert.That(completed, Is.False);

            await task.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(completed, Is.True);
            Assert.That(channels[0].clip, Is.SameAs(_bgmShort));
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseCompletesWhenSameChannelIsReusedBeforeClipLength() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 2, useVoice: false);
            var channels = _root.GetComponents<AudioSource>();
            var completed = false;

            var task = player.PlayBgmAsync(BgmIntroAddress, loop: false).ContinueWith(() => completed = true);
            await WaitUntilClipIsAssigned(channels[0], _bgmIntro);

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[1], _bgmLoop);

            Assert.That(completed, Is.False);

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[0], _bgmLoop);
            await task.Timeout(TimeSpan.FromSeconds(1.0f));
            player.StopAllBgm();

            Assert.That(completed, Is.True);
            Assert.That(_provider.ReleaseCount(BgmIntroAddress), Is.EqualTo(1)); // チャンネル使い回し時に前のクリップが解放される
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopTrueCompletesWhenSameChannelIsReused() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 2, useVoice: false);
            var channels = _root.GetComponents<AudioSource>();
            var completed = false;

            var task = player.PlayBgmAsync(BgmIntroAddress, loop: true).ContinueWith(() => completed = true);
            await WaitUntilClipIsAssigned(channels[0], _bgmIntro);

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[1], _bgmLoop);

            Assert.That(completed, Is.False);

            player.PlayBgmAsync(BgmLoopAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[0], _bgmLoop);
            await task.Timeout(TimeSpan.FromSeconds(1.0f));
            player.StopAllBgm();

            Assert.That(completed, Is.True);
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenCancelledAfterPlaybackStartsStopsBgm() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 2, useVoice: false);
            using var cts = new CancellationTokenSource();
            var channels = _root.GetComponents<AudioSource>();

            var task = player.PlayBgmAsync(BgmIntroAddress, loop: true, cts.Token);
            await WaitUntilClipIsAssigned(channels[0], _bgmIntro);

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
            Assert.That(channels[0].clip, Is.Null);
            Assert.That(channels[0].loop, Is.False);
            Assert.That(_provider.ReleaseCount(BgmIntroAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseIsCancelledAfterPlaybackStartsStopsBgm() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 2, useVoice: false);
            using var cts = new CancellationTokenSource();
            var channels = _root.GetComponents<AudioSource>();

            var task = player.PlayBgmAsync(BgmIntroAddress, loop: false, cts.Token);
            await WaitUntilClipIsAssigned(channels[0], _bgmIntro);

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
            Assert.That(channels[0].clip, Is.Null);
            Assert.That(channels[0].loop, Is.False);
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenSecondPlaybackIsCancelledStopsOnlyItsBgmChannel() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 2, useVoice: false);
            using var cts = new CancellationTokenSource();
            var channels = _root.GetComponents<AudioSource>();

            player.PlayBgmAsync(BgmIntroAddress, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[0], _bgmIntro);

            var task = player.PlayBgmAsync(BgmLoopAddress, loop: true, cts.Token);
            await WaitUntilClipIsAssigned(channels[1], _bgmLoop);

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
            Assert.That(channels[0].clip, Is.SameAs(_bgmIntro));
            Assert.That(channels[0].loop, Is.True);
            Assert.That(channels[1].clip, Is.Null);
            Assert.That(channels[1].loop, Is.False);
        });

        [UnityTest]
        public IEnumerator PlaySeAndVoiceAsyncLoadAndReleaseAddressableClips() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 1, useVoice: true);
            player.SetMasterVolume(0.8f);
            player.SetSeVolume(0.5f);
            player.SetVoiceVolume(0.25f);
            var channels = _root.GetComponents<AudioSource>(); // [bgm0, se, voice]

            await player.PlaySeAsync(SeAddress);
            await player.PlayVoiceAsync(VoiceAddress);

            Assert.That(channels[1].volume, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(channels[2].volume, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(_provider.LoadCount(SeAddress), Is.EqualTo(1));
            Assert.That(_provider.LoadCount(VoiceAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(SeAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(VoiceAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlaySeAsyncWhenProviderReturnsNullReleasesHandle() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad());

            await player.PlaySeAsync(NullAddress);

            Assert.That(_provider.LoadCount(NullAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(NullAddress), Is.EqualTo(1));
        });

        [Test]
        public void PlaySeAndVoiceAsyncWhenCancelledBeforeLoadThrowOperationCanceledException()
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 1, useVoice: true);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => _ = player.PlaySeAsync(SeAddress, cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.PlayVoiceAsync(VoiceAddress, cts.Token));
        }

        [UnityTest]
        public IEnumerator PlaySeAsyncWhenCancelledWhileWaitingForClipLengthThrowsOperationCanceledException() => UniTask.ToCoroutine(async () =>
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 1, useVoice: false);
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

        [Test]
        public void PlayVoiceAsyncWhenVoiceIsDisabledThrowsInvalidOperationException()
        {
            using var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 1, useVoice: false);

            Assert.Throws<InvalidOperationException>(() => _ = player.PlayVoiceAsync(VoiceAddress));
        }

        [UnityTest]
        public IEnumerator DisposeReleasesLoadedBgmHandles() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayerCore<string>(_root, new AddressableAudioClipLoad(), bgmChannels: 1, useVoice: false);

            player.PlayBgmAsync(BgmIntroAddress).Forget();
            await WaitUntilClipIsAssigned(_root.GetComponents<AudioSource>()[0], _bgmIntro);
            player.Dispose();

            Assert.That(_provider.LoadCount(BgmIntroAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(BgmIntroAddress), Is.EqualTo(1));
        });

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
