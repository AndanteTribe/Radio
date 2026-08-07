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
    public class MultiChannelsAudioHubTests
    {
        private readonly List<AudioClip> _clips = new();
        private GameObject _gameObject = null!;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(MultiChannelsAudioHubTests));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var clip in _clips)
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void ConstructorInitializesAndExposesChannelsInOrder()
        {
            var channels = CreateChannels(2);
            channels[0].playOnAwake = true;
            channels[1].playOnAwake = true;

            var hub = new MultiChannelsAudioHub(channels, 0.3f, false);

            Assert.That(hub.Loop, Is.False);
            Assert.That(hub.AudioSources.Length, Is.EqualTo(2));
            Assert.That(hub.AudioSources[0], Is.SameAs(channels[0]));
            Assert.That(hub.AudioSources[1], Is.SameAs(channels[1]));
            Assert.That(channels[0].volume, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(channels[1].volume, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(channels[0].loop, Is.False);
            Assert.That(channels[1].loop, Is.False);
            Assert.That(channels[0].playOnAwake, Is.False);
            Assert.That(channels[1].playOnAwake, Is.False);
        }

        [TestCase(0.0f)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        public void ConstructorRejectsInvalidVolume(float volume)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MultiChannelsAudioHub(CreateChannels(1), volume));
        }

        [Test]
        public void ApplyVolumeUpdatesEveryChannel()
        {
            var channels = CreateChannels(2);
            var hub = new MultiChannelsAudioHub(channels);

            hub.ApplyVolume(0.75f);

            Assert.That(channels[0].volume, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(channels[1].volume, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [TestCase(0.0f)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        public void ApplyVolumeRejectsInvalidVolume(float volume)
        {
            var hub = new MultiChannelsAudioHub(CreateChannels(1));

            Assert.Throws<ArgumentOutOfRangeException>(() => hub.ApplyVolume(volume));
        }

        [UnityTest]
        public IEnumerator PlayAsyncRotatesChannelsAndLoopingPlayCompletesAfterOneCycle() =>
            UniTask.ToCoroutine(async () =>
            {
                var channels = CreateChannels(2);
                var clips = new[]
                {
                    CreateClip("First", 1.0f),
                    CreateClip("Second", 1.0f),
                    CreateClip("Third", 1.0f)
                };
                var hub = new MultiChannelsAudioHub(channels, 0.4f, true);

                var first = hub.PlayAsync(clips[0], CancellationToken.None);
                Assert.That(channels[0].clip, Is.SameAs(clips[0]));
                Assert.That(channels[0].loop, Is.True);
                Assert.That(channels[0].volume, Is.EqualTo(0.4f).Within(0.0001f));

                var second = hub.PlayAsync(clips[1], CancellationToken.None);
                Assert.That(channels[1].clip, Is.SameAs(clips[1]));

                var third = hub.PlayAsync(clips[2], CancellationToken.None);
                Assert.That(channels[0].clip, Is.SameAs(clips[2]));

                await first.Timeout(TimeSpan.FromSeconds(1));
                hub.StopAll();
                await UniTask.WhenAll(second, third).Timeout(TimeSpan.FromSeconds(1));
            });

        [UnityTest]
        public IEnumerator PlayAsyncNonLoopingCompletesAtClipEnd() => UniTask.ToCoroutine(async () =>
        {
            var channel = CreateChannels(1)[0];
            var clip = CreateClip("Short", 0.01f);
            var hub = new MultiChannelsAudioHub(new[] { channel }, loop: false);

            await hub.PlayAsync(clip, CancellationToken.None).Timeout(TimeSpan.FromSeconds(1));

            Assert.That(channel.clip, Is.SameAs(clip));
            Assert.That(channel.loop, Is.False);
        });

        [UnityTest]
        public IEnumerator PlayAsyncNonLoopingCompletesWhenChannelIsReused() => UniTask.ToCoroutine(async () =>
        {
            var channel = CreateChannels(1)[0];
            var firstClip = CreateClip("FirstLong", 1.0f);
            var secondClip = CreateClip("SecondLong", 1.0f);
            var hub = new MultiChannelsAudioHub(new[] { channel }, loop: false);

            var first = hub.PlayAsync(firstClip, CancellationToken.None);
            var second = hub.PlayAsync(secondClip, CancellationToken.None);

            await first.Timeout(TimeSpan.FromSeconds(0.5f));
            Assert.That(channel.clip, Is.SameAs(secondClip));
            hub.StopAll();
            await second.Timeout(TimeSpan.FromSeconds(1));
        });

        [UnityTest]
        public IEnumerator PlayAsyncUsesCurrentLoopProperty() => UniTask.ToCoroutine(async () =>
        {
            var channel = CreateChannels(1)[0];
            var clip = CreateClip("Short", 0.01f);
            var hub = new MultiChannelsAudioHub(new[] { channel }, loop: true)
            {
                Loop = false
            };

            await hub.PlayAsync(clip, CancellationToken.None).Timeout(TimeSpan.FromSeconds(1));

            Assert.That(channel.loop, Is.False);
        });

        [UnityTest]
        public IEnumerator PlayAsyncWithPreCanceledTokenDoesNotModifyChannel() => UniTask.ToCoroutine(async () =>
        {
            var channel = CreateChannels(1)[0];
            var originalClip = CreateClip("Original", 1.0f);
            var replacementClip = CreateClip("Replacement", 1.0f);
            channel.clip = originalClip;
            var hub = new MultiChannelsAudioHub(new[] { channel });
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            await AudioHubTestUtility.AssertCanceled(
                hub.PlayAsync(replacementClip, cancellationTokenSource.Token));

            Assert.That(channel.clip, Is.SameAs(originalClip));
        });

        [UnityTest]
        public IEnumerator PlayAsyncCancellationStopsOnlyCapturedChannel() => UniTask.ToCoroutine(async () =>
        {
            foreach (var loop in new[] { true, false })
            {
                var channels = CreateChannels(2);
                var firstClip = CreateClip($"First-{loop}", 1.0f);
                var secondClip = CreateClip($"Second-{loop}", 1.0f);
                var hub = new MultiChannelsAudioHub(channels, loop: loop);
                using var cancellationTokenSource = new CancellationTokenSource();

                var canceledTask = hub.PlayAsync(firstClip, cancellationTokenSource.Token);
                var survivingTask = hub.PlayAsync(secondClip, CancellationToken.None);
                cancellationTokenSource.Cancel();

                await AudioHubTestUtility.AssertCanceled(canceledTask).Timeout(TimeSpan.FromSeconds(1));
                Assert.That(channels[0].clip, Is.Null);
                Assert.That(channels[0].loop, Is.False);
                Assert.That(channels[1].clip, Is.SameAs(secondClip));

                hub.StopAll();
                await survivingTask.Timeout(TimeSpan.FromSeconds(1));
            }
        });

        [UnityTest]
        public IEnumerator StopAllClearsChannelsReleasesWaitersAndRestartsAtFirstChannel() =>
            UniTask.ToCoroutine(async () =>
            {
                var channels = CreateChannels(2);
                var firstClip = CreateClip("BeforeStop", 1.0f);
                var secondClip = CreateClip("AfterStop", 1.0f);
                var hub = new MultiChannelsAudioHub(channels);

                var beforeStop = hub.PlayAsync(firstClip, CancellationToken.None);
                hub.StopAll();
                await beforeStop.Timeout(TimeSpan.FromSeconds(1));

                Assert.That(channels[0].clip, Is.Null);
                Assert.That(channels[1].clip, Is.Null);
                Assert.That(channels[0].loop, Is.False);
                Assert.That(channels[1].loop, Is.False);

                var afterStop = hub.PlayAsync(secondClip, CancellationToken.None);
                Assert.That(channels[0].clip, Is.SameAs(secondClip));
                hub.StopAll();
                await afterStop.Timeout(TimeSpan.FromSeconds(1));
            });

        [UnityTest]
        public IEnumerator EmptyChannelsFailNaturallyOnlyWhenPlaying() => UniTask.ToCoroutine(async () =>
        {
            var hub = new MultiChannelsAudioHub(Array.Empty<AudioSource>());
            var clip = CreateClip("Unused", 0.01f);

            Assert.That(hub.AudioSources.Length, Is.Zero);
            Assert.DoesNotThrow(hub.StopAll);
            Assert.DoesNotThrow(() => hub.ApplyVolume(0.5f));
            await AudioHubTestUtility.AssertThrows<DivideByZeroException>(
                hub.PlayAsync(clip, CancellationToken.None));
        });

        private AudioSource[] CreateChannels(int count)
        {
            var channels = new AudioSource[count];
            for (var i = 0; i < count; i++)
            {
                channels[i] = _gameObject.AddComponent<AudioSource>();
            }
            return channels;
        }

        private AudioClip CreateClip(string name, float seconds)
        {
            var clip = AudioHubTestUtility.CreateClip(name, seconds);
            _clips.Add(clip);
            return clip;
        }
    }
}
