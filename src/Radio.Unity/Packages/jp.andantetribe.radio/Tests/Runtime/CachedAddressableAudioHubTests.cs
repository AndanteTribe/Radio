#nullable enable

using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;

namespace Radio.Tests
{
    public class CachedAddressableAudioHubTests : AddressableAudioHubTestBase
    {
        [UnityTest]
        public IEnumerator PlayAsyncByStringRetainsHandleUntilDispose() => UniTask.ToCoroutine(async () =>
        {
            var hub = new CachedAddressableAudioHub(Original);
            using var cts = new CancellationTokenSource();

            await hub.PlayAsync(StringAddress, cts.Token);

            Assert.That(Original.PlayedClips, Is.EqualTo(new[] { StringClip }));
            Assert.That(Original.PlayedCancellationTokens[0], Is.EqualTo(cts.Token));
            Assert.That(Provider.LoadCount(StringAddress), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(StringAddress), Is.Zero);

            hub.Dispose();
            await WaitUntilReleasedAsync(StringAddress);

            Assert.That(Provider.ReleaseCount(StringAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayAsyncByAssetReferenceAndDisposeReleasesEveryDistinctHandle() => UniTask.ToCoroutine(async () =>
        {
            var hub = new CachedAddressableAudioHub(Original);

            await hub.PlayAsync(StringAddress, CancellationToken.None);
            await hub.PlayAsync(new AssetReferenceT<AudioClip>(ReferenceGuid), CancellationToken.None);

            Assert.That(Original.PlayedClips, Is.EqualTo(new[] { StringClip, ReferenceClip }));
            Assert.That(Provider.LoadCount(StringAddress), Is.EqualTo(1));
            Assert.That(Provider.LoadCount(ReferenceGuid), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(StringAddress), Is.Zero);
            Assert.That(Provider.ReleaseCount(ReferenceGuid), Is.Zero);

            hub.Dispose();
            await WaitUntilReleasedAsync(StringAddress);
            await WaitUntilReleasedAsync(ReferenceGuid);

            Assert.That(Provider.ReleaseCount(StringAddress), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(ReferenceGuid), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator RepeatedLoadsAreReferenceCountedAndDisposeIsIdempotent() => UniTask.ToCoroutine(async () =>
        {
            var hub = new CachedAddressableAudioHub(Original);

            await hub.PlayAsync(StringAddress, CancellationToken.None);
            await hub.PlayAsync(StringAddress, CancellationToken.None);

            Assert.That(Original.PlayedClips, Is.EqualTo(new[] { StringClip, StringClip }));
            Assert.That(Provider.LoadCount(StringAddress), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(StringAddress), Is.Zero);

            hub.Dispose();
            await WaitUntilReleasedAsync(StringAddress);
            hub.Dispose();
            await UniTask.Yield();

            Assert.That(Provider.ReleaseCount(StringAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayAsyncWhenLoadReturnsNullDoesNotCacheOrDelegate() => UniTask.ToCoroutine(async () =>
        {
            var hub = new CachedAddressableAudioHub(Original);

            await hub.PlayAsync(NullAddress, CancellationToken.None);
            await WaitUntilReleasedAsync(NullAddress);
            hub.Dispose();

            Assert.That(Original.PlayedClips, Is.Empty);
            Assert.That(Provider.LoadCount(NullAddress), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(NullAddress), Is.EqualTo(1));
        });

        [Test]
        public void PlayAsyncWhenAlreadyCancelledDoesNotStartEitherLoadOverload()
        {
            var hub = new CachedAddressableAudioHub(Original);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => _ = hub.PlayAsync(StringAddress, cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = hub.PlayAsync(new AssetReferenceT<AudioClip>(ReferenceGuid), cts.Token));
            Assert.That(Provider.LoadCount(StringAddress), Is.Zero);
            Assert.That(Provider.LoadCount(ReferenceGuid), Is.Zero);
        }

        [UnityTest]
        public IEnumerator PlayAsyncWhenCancelledDuringLoadDoesNotCacheAndReleasesAfterProviderCompletes() => UniTask.ToCoroutine(async () =>
        {
            var hub = new CachedAddressableAudioHub(Original);
            using var cts = new CancellationTokenSource();

            var task = hub.PlayAsync(DelayedAddress, cts.Token);
            cts.Cancel();
            var cancelled = await task.SuppressCancellationThrow();

            await WaitUntilLoadedAsync(DelayedAddress);
            await WaitUntilReleasedAsync(DelayedAddress);
            hub.Dispose();

            Assert.That(cancelled, Is.True);
            Assert.That(Original.PlayedClips, Is.Empty);
            Assert.That(Provider.LoadCount(DelayedAddress), Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(DelayedAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayAsyncWhenLoadFailsPropagatesAndDoesNotCacheFailedOperation() => UniTask.ToCoroutine(async () =>
        {
            var hub = new CachedAddressableAudioHub(Original);
            var expected = new InvalidOperationException("Cached addressable test load failure.");
            Provider.Fail(FailureAddress, expected);

            var firstException = await CaptureExceptionAsync(hub.PlayAsync(FailureAddress, CancellationToken.None));
            var secondException = await CaptureExceptionAsync(hub.PlayAsync(FailureAddress, CancellationToken.None));
            hub.Dispose();

            Assert.That(firstException, Is.Not.Null);
            Assert.That(firstException!.ToString(), Does.Contain(expected.Message));
            Assert.That(secondException, Is.Not.Null);
            Assert.That(secondException!.ToString(), Does.Contain(expected.Message));
            Assert.That(Original.PlayedClips, Is.Empty);
            Assert.That(Provider.LoadCount(FailureAddress), Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator PlayAsyncWhenOriginalThrowsRetainsHandleUntilDisposeAndPreservesException() => UniTask.ToCoroutine(async () =>
        {
            var hub = new CachedAddressableAudioHub(Original);
            var expected = new InvalidOperationException("Original hub failure.");
            Original.PlayException = expected;

            var actual = await CaptureExceptionAsync(hub.PlayAsync(StringAddress, CancellationToken.None));

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(Original.PlayedClips, Is.EqualTo(new[] { StringClip }));
            Assert.That(Provider.ReleaseCount(StringAddress), Is.Zero);

            hub.Dispose();
            await WaitUntilReleasedAsync(StringAddress);

            Assert.That(Provider.ReleaseCount(StringAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PlayAsyncWhenOriginalIsCancelledRetainsHandleUntilDispose() => UniTask.ToCoroutine(async () =>
        {
            var hub = new CachedAddressableAudioHub(Original);
            using var cts = new CancellationTokenSource();
            Original.PlayHandler = static (_, token) =>
                UniTask.Delay(TimeSpan.FromSeconds(10), cancellationToken: token);

            var task = hub.PlayAsync(StringAddress, cts.Token);
            await UniTask.WaitUntil(Original, static original => original.PlayedClips.Count == 1)
                .Timeout(TimeSpan.FromSeconds(1));
            cts.Cancel();

            var cancelled = await task.SuppressCancellationThrow();
            Assert.That(cancelled, Is.True);
            Assert.That(Provider.ReleaseCount(StringAddress), Is.Zero);

            hub.Dispose();
            await WaitUntilReleasedAsync(StringAddress);
            Assert.That(Provider.ReleaseCount(StringAddress), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator StopAllDoesNotReleaseRetainedHandles() => UniTask.ToCoroutine(async () =>
        {
            var hub = new CachedAddressableAudioHub(Original);
            await hub.PlayAsync(StringAddress, CancellationToken.None);

            hub.StopAll();
            await UniTask.Yield();

            Assert.That(Original.StopAllCallCount, Is.EqualTo(1));
            Assert.That(Provider.ReleaseCount(StringAddress), Is.Zero);

            hub.Dispose();
            await WaitUntilReleasedAsync(StringAddress);
        });

        [Test]
        public void AudioSourcesStopAllAndApplyVolumeDelegateToOriginal()
        {
            var hub = new CachedAddressableAudioHub(Original);

            var sources = hub.AudioSources;
            hub.StopAll();
            hub.ApplyVolume(0.75f);

            Assert.That(sources.Length, Is.EqualTo(1));
            Assert.That(sources[0], Is.SameAs(Source));
            Assert.That(Original.StopAllCallCount, Is.EqualTo(1));
            Assert.That(Original.AppliedVolumes, Is.EqualTo(new[] { 0.75f }));
        }
    }
}
