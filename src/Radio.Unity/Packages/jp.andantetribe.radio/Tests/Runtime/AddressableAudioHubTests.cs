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
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.Util;
using UnityEngine.TestTools;

namespace Radio.Tests
{
    public class AddressableAudioHubTests : AddressableAudioHubTestBase
    {
        [UnityTest]
        public IEnumerator PlayAsyncByStringLoadsDelegatesAndReleases() => UniTask.ToCoroutine(async () =>
        {
            var hub = new AddressableAudioHub(Original);
            using var cts = new CancellationTokenSource();

            await hub.PlayAsync(StringAddress, cts.Token);
            await WaitUntilReleasedAsync(StringAddress);

            Assert.That(Original.PlayedClips, Is.EqualTo(new[] { StringClip }));
            Assert.That(Original.PlayedCancellationTokens[0], Is.EqualTo(cts.Token));
            Assert.That(Provider.LoadCount(StringAddress), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(StringAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayAsyncByAssetReferenceLoadsDelegatesAndReleases() => UniTask.ToCoroutine(async () =>
        {
            var hub = new AddressableAudioHub(Original);

            await hub.PlayAsync(new AssetReferenceT<AudioClip>(ReferenceGuid), CancellationToken.None);
            await WaitUntilReleasedAsync(ReferenceGuid);

            Assert.That(Original.PlayedClips, Is.EqualTo(new[] { ReferenceClip }));
            Assert.That(Provider.LoadCount(ReferenceGuid), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(ReferenceGuid), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator RepeatedRequestsReloadAfterEachRelease() => UniTask.ToCoroutine(async () =>
        {
            var hub = new AddressableAudioHub(Original);

            await hub.PlayAsync(StringAddress, CancellationToken.None);
            await WaitUntilReleasedAsync(StringAddress);
            await hub.PlayAsync(StringAddress, CancellationToken.None);
            await WaitUntilReleasedAsync(StringAddress, 2);

            Assert.That(Original.PlayedClips, Is.EqualTo(new[] { StringClip, StringClip }));
            Assert.That(Provider.LoadCount(StringAddress), Is.EqualTo(2));
            Assert.That(Provider.ReleaseCount(StringAddress), Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator PlayAsyncWhenLoadReturnsNullDoesNotDelegateAndReleases() => UniTask.ToCoroutine(async () =>
        {
            var hub = new AddressableAudioHub(Original);

            await hub.PlayAsync(NullAddress, CancellationToken.None);
            await WaitUntilReleasedAsync(NullAddress);

            Assert.That(Original.PlayedClips, Is.Empty);
            Assert.That(Provider.LoadCount(NullAddress), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(NullAddress), Is.EqualTo(1));
        });

        [Test]
        public void PlayAsyncWhenAlreadyCancelledDoesNotStartEitherLoadOverload()
        {
            var hub = new AddressableAudioHub(Original);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => _ = hub.PlayAsync(StringAddress, cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = hub.PlayAsync(new AssetReferenceT<AudioClip>(ReferenceGuid), cts.Token));
            Assert.That(Provider.LoadCount(StringAddress), Is.Zero);
            Assert.That(Provider.LoadCount(ReferenceGuid), Is.Zero);
        }

        [UnityTest]
        public IEnumerator PlayAsyncWhenCancelledDuringLoadCancelsAndReleasesAfterProviderCompletes() => UniTask.ToCoroutine(async () =>
        {
            var hub = new AddressableAudioHub(Original);
            using var cts = new CancellationTokenSource();

            var task = hub.PlayAsync(DelayedAddress, cts.Token);
            cts.Cancel();
            var cancelled = await task.SuppressCancellationThrow();

            await WaitUntilLoadedAsync(DelayedAddress);
            await WaitUntilReleasedAsync(DelayedAddress);
            Assert.That(cancelled, Is.True);
            Assert.That(Original.PlayedClips, Is.Empty);
            Assert.That(Provider.LoadCount(DelayedAddress), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(DelayedAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayAsyncWhenLoadFailsPropagatesAndDoesNotRetainFailedOperation() => UniTask.ToCoroutine(async () =>
        {
            var hub = new AddressableAudioHub(Original);
            var expected = new InvalidOperationException("Addressable test load failure.");
            Provider.Fail(FailureAddress, expected);

            var firstException = await CaptureExceptionAsync(hub.PlayAsync(FailureAddress, CancellationToken.None));
            var secondException = await CaptureExceptionAsync(hub.PlayAsync(FailureAddress, CancellationToken.None));

            Assert.That(firstException, Is.Not.Null);
            Assert.That(firstException!.ToString(), Does.Contain(expected.Message));
            Assert.That(secondException, Is.Not.Null);
            Assert.That(secondException!.ToString(), Does.Contain(expected.Message));
            Assert.That(Original.PlayedClips, Is.Empty);
            Assert.That(Provider.LoadCount(FailureAddress), Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator PlayAsyncWhenOriginalThrowsStillReleasesAndPreservesException() => UniTask.ToCoroutine(async () =>
        {
            var hub = new AddressableAudioHub(Original);
            var expected = new InvalidOperationException("Original hub failure.");
            Original.PlayException = expected;

            var actual = await CaptureExceptionAsync(hub.PlayAsync(StringAddress, CancellationToken.None));
            await WaitUntilReleasedAsync(StringAddress);

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(Original.PlayedClips, Is.EqualTo(new[] { StringClip }));
            Assert.That(Provider.ReleaseCount(StringAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayAsyncWhenOriginalIsCancelledStillReleases() => UniTask.ToCoroutine(async () =>
        {
            var hub = new AddressableAudioHub(Original);
            using var cts = new CancellationTokenSource();
            Original.PlayHandler = static (_, token) =>
                UniTask.Delay(TimeSpan.FromSeconds(10), cancellationToken: token);

            var task = hub.PlayAsync(StringAddress, cts.Token);
            await UniTask.WaitUntil(Original, static original => original.PlayedClips.Count == 1)
                .Timeout(TimeSpan.FromSeconds(1));
            cts.Cancel();

            var cancelled = await task.SuppressCancellationThrow();
            await WaitUntilReleasedAsync(StringAddress);

            Assert.That(cancelled, Is.True);
            Assert.That(Provider.ReleaseCount(StringAddress), Is.EqualTo(1));
        });

        [Test]
        public void AudioSourcesStopAllAndApplyVolumeDelegateToOriginal()
        {
            var hub = new AddressableAudioHub(Original);

            var sources = hub.AudioSources;
            hub.StopAll();
            hub.ApplyVolume(0.25f);

            Assert.That(sources.Length, Is.EqualTo(1));
            Assert.That(sources[0], Is.SameAs(Source));
            Assert.That(Original.StopAllCallCount, Is.EqualTo(1));
            Assert.That(Original.AppliedVolumes, Is.EqualTo(new[] { 0.25f }));
        }
    }

    public abstract class AddressableAudioHubTestBase
    {
        protected const string StringAddress = "radio-tests-addressable-string";
        protected const string NullAddress = "radio-tests-addressable-null";
        protected const string DelayedAddress = "radio-tests-addressable-delayed";
        protected const string FailureAddress = "radio-tests-addressable-failure";
        protected const string ReferenceGuid = "1236ef8650e9473ba1065cfaab57f952";

        private const float TimeoutSeconds = 1.0f;

        private GameObject _root = null!;
        private AudioClip _delayedClip = null!;
        private ResourceLocationMap _locator = null!;
        private Action<AsyncOperationHandle, Exception>? _previousExceptionHandler;

        protected AudioClip StringClip { get; private set; } = null!;

        protected AudioClip ReferenceClip { get; private set; } = null!;

        protected AudioSource Source { get; private set; } = null!;

        private protected InMemoryAudioClipProvider Provider { get; private set; } = null!;

        protected RecordingAudioHub Original { get; private set; } = null!;

        [SetUp]
        public void SetUp()
        {
            PrepareAddressablesForDirectLocatorUse();
            _previousExceptionHandler = ResourceManager.ExceptionHandler;
            ResourceManager.ExceptionHandler = static (_, _) => { };

            _root = new GameObject(GetType().Name);
            Source = _root.AddComponent<AudioSource>();
            StringClip = CreateClip("Addressable String Clip");
            ReferenceClip = CreateClip("Addressable Reference Clip");
            _delayedClip = CreateClip("Addressable Delayed Clip");

            Provider = new InMemoryAudioClipProvider(new Dictionary<string, AudioClip?>
            {
                [StringAddress] = StringClip,
                [ReferenceGuid] = ReferenceClip,
                [NullAddress] = null,
                [DelayedAddress] = _delayedClip,
            });
            Addressables.ResourceManager.ResourceProviders.Add(Provider);

            _locator = new ResourceLocationMap($"{GetType().Name}-Locations", capacity: 5);
            AddLocation(StringAddress);
            AddLocation(ReferenceGuid);
            AddLocation(NullAddress);
            AddLocation(DelayedAddress);
            AddLocation(FailureAddress);
            Addressables.AddResourceLocator(_locator);

            Original = new RecordingAudioHub(Source);
        }

        [TearDown]
        public void TearDown()
        {
            Provider.CompleteAllDelayed();
            Addressables.RemoveResourceLocator(_locator);
            Addressables.ResourceManager.ResourceProviders.Remove(Provider);
            ResourceManager.ExceptionHandler = _previousExceptionHandler;

            DestroyImmediate(StringClip);
            DestroyImmediate(ReferenceClip);
            DestroyImmediate(_delayedClip);
            DestroyImmediate(_root);
        }

        protected UniTask WaitUntilLoadedAsync(string key, int expectedCount = 1) =>
            UniTask.WaitUntil((Provider, key, expectedCount),
                    static args => args.Provider.LoadCount(args.key) >= args.expectedCount)
                .Timeout(TimeSpan.FromSeconds(TimeoutSeconds));

        protected UniTask WaitUntilReleasedAsync(string key, int expectedCount = 1) =>
            UniTask.WaitUntil((Provider, key, expectedCount),
                    static args => args.Provider.ReleaseCount(args.key) >= args.expectedCount)
                .Timeout(TimeSpan.FromSeconds(TimeoutSeconds));

        protected static async UniTask<Exception?> CaptureExceptionAsync(UniTask task)
        {
            try
            {
                await task;
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private void AddLocation(string key) =>
            _locator.Add(key, new ResourceLocationBase(key, key, Provider.ProviderId, typeof(AudioClip)));

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

        private static AudioClip CreateClip(string name) =>
            AudioClip.Create(name, lengthSamples: 441, channels: 1, frequency: 44100, stream: false);

        private static void DestroyImmediate(UnityEngine.Object obj)
        {
            if (obj != null)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        protected sealed class RecordingAudioHub : IAudioHub<AudioClip>
        {
            private readonly AudioSource[] _sources;

            public RecordingAudioHub(AudioSource source) => _sources = new[] { source };

            public List<AudioClip> PlayedClips { get; } = new();

            public List<CancellationToken> PlayedCancellationTokens { get; } = new();

            public List<float> AppliedVolumes { get; } = new();

            public int StopAllCallCount { get; private set; }

            public Exception? PlayException { get; set; }

            public Func<AudioClip, CancellationToken, UniTask>? PlayHandler { get; set; }

            public ReadOnlySpan<AudioSource> AudioSources => _sources;

            public UniTask PlayAsync(AudioClip key, CancellationToken cancellationToken)
            {
                PlayedClips.Add(key);
                PlayedCancellationTokens.Add(cancellationToken);
                if (PlayException != null)
                {
                    return UniTask.FromException(PlayException);
                }
                return PlayHandler?.Invoke(key, cancellationToken) ?? UniTask.CompletedTask;
            }

            public void StopAll() => StopAllCallCount++;

            public void ApplyVolume(float value) => AppliedVolumes.Add(value);
        }
    }
}
