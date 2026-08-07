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
    /// <summary>
    /// Play mode tests for the Addressables-independent player.
    /// </summary>
    public class AudioPlayerTests
    {
        private GameObject _root = null!;
        private AudioClip _bgmIntro = null!;
        private AudioClip _bgmLoop = null!;
        private AudioClip _bgmShort = null!;
        private AudioClip _se = null!;
        private AudioClip _longSe = null!;
        private AudioClip _voice = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("AudioPlayerTests");
            _root.AddComponent<AudioListener>();
            _bgmIntro = CreateClip("BGM Intro", seconds: 1.0f);
            _bgmLoop = CreateClip("BGM Loop", seconds: 1.0f);
            _bgmShort = CreateClip("BGM Short", seconds: 0.05f);
            _se = CreateClip("SE", seconds: 0.01f);
            _longSe = CreateClip("Long SE", seconds: 1.0f);
            _voice = CreateClip("Voice", seconds: 0.01f);
        }

        [TearDown]
        public void TearDown()
        {
            DestroyImmediate(_root);
            DestroyImmediate(_bgmIntro);
            DestroyImmediate(_bgmLoop);
            DestroyImmediate(_bgmShort);
            DestroyImmediate(_se);
            DestroyImmediate(_longSe);
            DestroyImmediate(_voice);
        }

        [Test]
        public void ConstructorCreatesAndPublishesExpectedChannelsAndVolumeControlsClampAndApply()
        {
            var existingSeChannel = _root.AddComponent<AudioSource>();
            var player = new AudioPlayer(_root, bgmChannelCount: 2, useVoice: true);

            var channels = _root.GetComponents<AudioSource>();
            Assert.That(channels, Has.Length.EqualTo(4));
            Assert.That(channels[0], Is.SameAs(existingSeChannel));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => !channel.loop));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => !channel.playOnAwake));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => Mathf.Approximately(channel.volume, 0.5f)));

            Assert.That(player.Sources.Se, Is.SameAs(channels[0]));
            Assert.That(player.Sources.Voice, Is.SameAs(channels[1]));
            Assert.That(player.Sources.Bgm, Is.EqualTo(new[] { channels[2], channels[3] }));
            Assert.That(player.Sources.All, Is.EqualTo(channels));
            Assert.That(player, Is.Not.InstanceOf<IDisposable>());

            var allList = (IList<AudioSource>)player.Sources.All;
            var bgmList = (IList<AudioSource>)player.Sources.Bgm;
            Assert.That(allList.IsReadOnly, Is.True);
            Assert.That(bgmList.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => allList[0] = channels[1]);
            Assert.Throws<NotSupportedException>(() => bgmList.RemoveAt(0));

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
        public void ConstructorWhenEnoughChannelsAlreadyExistUsesAllWithoutReinitializingThem()
        {
            var channels = new AudioSource[4];
            for (var i = 0; i < channels.Length; i++)
            {
                var channel = channels[i] = _root.AddComponent<AudioSource>();
                channel.loop = true;
                channel.playOnAwake = true;
                channel.volume = 0.1f * (i + 1);
            }

            var player = new AudioPlayer(_root, bgmChannelCount: 1, useVoice: false);

            Assert.That(player.Sources.Se, Is.SameAs(channels[0]));
            Assert.That(player.Sources.Voice, Is.Null);
            Assert.That(player.Sources.Bgm, Is.EqualTo(new[] { channels[1], channels[2], channels[3] }));
            Assert.That(player.Sources.All, Is.EqualTo(channels));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => channel.loop));
            Assert.That(channels, Has.All.Matches<AudioSource>(channel => channel.playOnAwake));
            Assert.That(channels[0].volume, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(channels[3].volume, Is.EqualTo(0.4f).Within(0.0001f));
        }

        [Test]
        public void ConstructorRejectsZeroBgmChannels()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new AudioPlayer(_root, bgmChannelCount: 0));
        }

        [Test]
        public void VoiceMembersWhenVoiceIsDisabledExposeNullAndThrowForPlaybackOrVolume()
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 1, useVoice: false);

            Assert.That(player.Sources.Voice, Is.Null);
            Assert.Throws<InvalidOperationException>(() => player.SetVoiceVolume(1.0f));
            Assert.Throws<InvalidOperationException>(() => _ = player.PlayVoiceAsync(_voice));
        }

        [Test]
        public void CrossFadeBgmAsyncWithoutConfiguredTransitionThrowsInvalidOperationException()
        {
            var player = new AudioPlayer(_root);

            Assert.Throws<InvalidOperationException>(() => _ = player.CrossFadeBgmAsync(_bgmIntro));
        }

        [Test]
        public void CustomBgmTransitionUsesOnlyThePublishedContextOperations()
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 2);
            var transition = new RecordingBgmTransition();
            player.ConfigureBgmTransition(transition);

            _ = player.CrossFadeBgmAsync(_bgmIntro, loop: false);

            var context = transition.Context!;
            var channel = player.Sources.Bgm[0];
            Assert.That(transition.PreviousChannel, Is.Null);
            Assert.That(context.CurrentBgmChannel, Is.SameAs(channel));
            Assert.That(context.ManagedBgmVolume, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(channel.clip, Is.SameAs(_bgmIntro));
            Assert.That(channel.loop, Is.False);

            var volumeControl = context.AcquireVolumeControl(channel);
            channel.volume = 0.123f;
            player.SetBgmVolume(0.8f);
            Assert.That(channel.volume, Is.EqualTo(0.123f).Within(0.0001f));

            volumeControl.Dispose();
            player.SetBgmVolume(0.6f);
            Assert.That(channel.volume, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.DoesNotThrow(volumeControl.Dispose);

            Assert.Throws<ArgumentNullException>(() => player.ConfigureBgmTransition(null!));
        }

        [UnityTest]
        public IEnumerator PlayBgmAsyncRotatesChannelsAndStopAllBgmResetsState() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 2, useVoice: true);
            player.SetMasterVolume(0.8f);
            player.SetBgmVolume(0.25f);
            var channels = _root.GetComponents<AudioSource>();

            player.PlayBgmAsync(_bgmIntro, loop: false).Forget();
            await WaitUntilClipIsAssigned(channels[2], _bgmIntro);

            Assert.That(channels[2].loop, Is.False);
            Assert.That(channels[2].volume, Is.EqualTo(0.2f).Within(0.0001f));

            player.PlayBgmAsync(_bgmLoop, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[3], _bgmLoop);

            Assert.That(channels[3].loop, Is.True);
            Assert.That(channels[3].volume, Is.EqualTo(0.2f).Within(0.0001f));

            player.StopAllBgm();

            Assert.That(channels[2].clip, Is.Null);
            Assert.That(channels[2].loop, Is.False);
            Assert.That(channels[3].clip, Is.Null);
            Assert.That(channels[3].loop, Is.False);

            player.PlayBgmAsync(_bgmLoop, loop: true).Forget();
            await WaitUntilClipIsAssigned(channels[2], _bgmLoop);
            player.StopAllBgm();

            Assert.That(channels[2].clip, Is.Null);
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseCompletesAfterClipLength() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 2);
            var channel = player.Sources.Bgm[0];
            var completed = false;

            var task = player.PlayBgmAsync(_bgmShort, loop: false).ContinueWith(() => completed = true);
            await WaitUntilClipIsAssigned(channel, _bgmShort);

            Assert.That(completed, Is.False);

            await task.Timeout(TimeSpan.FromSeconds(1.0f));

            Assert.That(completed, Is.True);
            Assert.That(channel.clip, Is.SameAs(_bgmShort));
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseCompletesWhenSameChannelIsReused() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 2);
            var completed = false;

            var task = player.PlayBgmAsync(_bgmIntro, loop: false).ContinueWith(() => completed = true);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmIntro);

            player.PlayBgmAsync(_bgmLoop).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[1], _bgmLoop);
            Assert.That(completed, Is.False);

            player.PlayBgmAsync(_bgmLoop).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmLoop);
            await task.Timeout(TimeSpan.FromSeconds(1.0f));
            player.StopAllBgm();

            Assert.That(completed, Is.True);
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopTrueCompletesWhenSameChannelIsReused() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 2);
            var completed = false;

            var task = player.PlayBgmAsync(_bgmIntro).ContinueWith(() => completed = true);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmIntro);

            player.PlayBgmAsync(_bgmLoop).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[1], _bgmLoop);
            Assert.That(completed, Is.False);

            player.PlayBgmAsync(_bgmLoop).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmLoop);
            await task.Timeout(TimeSpan.FromSeconds(1.0f));
            player.StopAllBgm();

            Assert.That(completed, Is.True);
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenCancelledStopsOnlyItsChannel() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 2);
            using var cts = new CancellationTokenSource();

            player.PlayBgmAsync(_bgmIntro).Forget();
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmIntro);

            var task = player.PlayBgmAsync(_bgmLoop, cancellationToken: cts.Token);
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
            Assert.That(player.Sources.Bgm[0].loop, Is.True);
            Assert.That(player.Sources.Bgm[1].clip, Is.Null);
            Assert.That(player.Sources.Bgm[1].loop, Is.False);
            player.StopAllBgm();
        });

        [UnityTest]
        public IEnumerator PlayBgmAsyncWhenLoopFalseIsCancelledStopsItsChannel() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 1);
            using var cts = new CancellationTokenSource();

            var task = player.PlayBgmAsync(_bgmIntro, loop: false, cts.Token);
            await WaitUntilClipIsAssigned(player.Sources.Bgm[0], _bgmIntro);
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
            Assert.That(player.Sources.Bgm[0].clip, Is.Null);
            Assert.That(player.Sources.Bgm[0].loop, Is.False);
        });

        [UnityTest]
        public IEnumerator PlaySeAndVoiceAsyncPlayDirectClipsAndUseManagedVolumes() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 1, useVoice: true);
            player.SetMasterVolume(0.8f);
            player.SetSeVolume(0.5f);
            player.SetVoiceVolume(0.25f);

            await player.PlaySeAsync(_se);
            await player.PlayVoiceAsync(_voice);

            Assert.That(player.Sources.Se.volume, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(player.Sources.Voice!.volume, Is.EqualTo(0.2f).Within(0.0001f));
        });

        [Test]
        public void PlaybackWhenCancelledBeforeItStartsThrowsOperationCanceledException()
        {
            var player = new AudioPlayer(_root, bgmChannelCount: 1, useVoice: true);
            player.PlayBgmAsync(_bgmLoop).Forget();
            Assert.That(player.Sources.Bgm[0].clip, Is.SameAs(_bgmLoop));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => _ = player.PlayBgmAsync(_bgmIntro, cancellationToken: cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.CrossFadeBgmAsync(_bgmIntro, cancellationToken: cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.PlaySeAsync(_se, cts.Token));
            Assert.Throws<OperationCanceledException>(() => _ = player.PlayVoiceAsync(_voice, cts.Token));
            Assert.That(player.Sources.Bgm[0].clip, Is.SameAs(_bgmLoop));
        }

        [UnityTest]
        public IEnumerator PlaySeAsyncWhenCancelledWhileWaitingThrowsOperationCanceledException() => UniTask.ToCoroutine(async () =>
        {
            var player = new AudioPlayer(_root);
            using var cts = new CancellationTokenSource();

            var task = player.PlaySeAsync(_longSe, cts.Token);
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
        });

        private static AudioClip CreateClip(string name, float seconds)
        {
            const int sampleRate = 44100;
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

        private sealed class RecordingBgmTransition : IBgmTransition
        {
            public BgmTransitionContext? Context { get; private set; }
            public AudioSource? PreviousChannel { get; private set; }

            public UniTask TransitionAsync(
                BgmTransitionContext context,
                AudioClip clip,
                bool loop,
                CancellationToken cancellationToken)
            {
                Context = context;
                PreviousChannel = context.CurrentBgmChannel;

                var channel = context.GetAvailableBgmChannel();
                channel.clip = clip;
                channel.loop = loop;
                channel.volume = context.ManagedBgmVolume;
                return UniTask.CompletedTask;
            }
        }

    }
}
