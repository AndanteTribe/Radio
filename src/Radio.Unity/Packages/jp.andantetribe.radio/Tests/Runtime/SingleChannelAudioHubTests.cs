#nullable enable

using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Radio.Tests
{
    public class SingleChannelAudioHubTests
    {
        private GameObject _gameObject = null!;
        private AudioSource _source = null!;
        private AudioClip _shortClip = null!;
        private AudioClip _longClip = null!;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(SingleChannelAudioHubTests));
            _source = _gameObject.AddComponent<AudioSource>();
            _shortClip = AudioHubTestUtility.CreateClip("Short", 0.01f);
            _longClip = AudioHubTestUtility.CreateClip("Long", 1.0f);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_shortClip);
            UnityEngine.Object.DestroyImmediate(_longClip);
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void ConstructorExposesSpecifiedSource()
        {
            var hub = new SingleChannelAudioHub(_source, 0.4f);

            Assert.That(hub.AudioSources.Length, Is.EqualTo(1));
            Assert.That(hub.AudioSources[0], Is.SameAs(_source));
        }

        [TestCase(0.0f)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        public void ConstructorRejectsInvalidVolume(float volume)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SingleChannelAudioHub(_source, volume));
        }

        [UnityTest]
        public IEnumerator PlayAsyncCompletesAfterClipDuration() => UniTask.ToCoroutine(async () =>
        {
            var hub = new SingleChannelAudioHub(_source);

            await hub.PlayAsync(_shortClip, CancellationToken.None).Timeout(TimeSpan.FromSeconds(1));
        });

        [Test]
        public void PlayAsyncWithPreCanceledTokenDoesNotStart()
        {
            var hub = new SingleChannelAudioHub(_source);
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => hub.PlayAsync(_longClip, cancellationTokenSource.Token));
        }

        [UnityTest]
        public IEnumerator PlayAsyncWhenCanceledPropagatesCancellation() => UniTask.ToCoroutine(async () =>
        {
            var hub = new SingleChannelAudioHub(_source);
            using var cancellationTokenSource = new CancellationTokenSource();

            var task = hub.PlayAsync(_longClip, cancellationTokenSource.Token);
            cancellationTokenSource.Cancel();

            await AudioHubTestUtility.AssertCanceled(task).Timeout(TimeSpan.FromSeconds(1));
        });

        [UnityTest]
        public IEnumerator StopAllStopsTheSource() => UniTask.ToCoroutine(async () =>
        {
            _source.clip = _longClip;
            _source.Play();
            await UniTask.Yield();
            var hub = new SingleChannelAudioHub(_source);

            hub.StopAll();

            Assert.That(_source.isPlaying, Is.False);
        });

        [Test]
        public void ApplyVolumeStoresPlaybackScaleWithoutOverwritingSourceVolume()
        {
            _source.volume = 0.8f;
            var hub = new SingleChannelAudioHub(_source);

            hub.ApplyVolume(0.25f);

            Assert.That(_source.volume, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [TestCase(0.0f)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        public void ApplyVolumeRejectsInvalidVolume(float volume)
        {
            var hub = new SingleChannelAudioHub(_source);

            Assert.Throws<ArgumentOutOfRangeException>(() => hub.ApplyVolume(volume));
        }
    }
}
