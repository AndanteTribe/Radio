#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Radio.Tests
{
    public class CompositeVolumeAudioHubTests
    {
        private enum VolumeKind
        {
            Bgm,
            Se
        }

        [Test]
        public void DefaultBuilderBuildsEmptyCompositeWithDefaultMasterVolume()
        {
            var builder = default(CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder);

            var composite = builder.Build();

            Assert.That(composite.Count, Is.Zero);
            Assert.That(composite.MasterVolume, Is.EqualTo(0.5f));
            Assert.DoesNotThrow(() => composite.ApplyMasterVolume(0.75f));
            Assert.That(composite.MasterVolume, Is.EqualTo(0.75f));
        }

        [Test]
        public void BuildAppliesMasterAndPerIdVolumeToEveryRegisteredHub()
        {
            var bgm1 = new RecordingAudioHub<AudioClip>();
            var bgm2 = new RecordingAudioHub<AudioClip>();
            var se = new RecordingAudioHub<AudioClip>();
            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder(masterVolume: 0.8f);
            builder.AddHub(VolumeKind.Bgm, bgm1);
            builder.AddHub(VolumeKind.Bgm, bgm2);
            builder.AddHub(VolumeKind.Se, se);
            builder.SetVolume(VolumeKind.Bgm, 0.25f);

            var composite = builder.Build();

            Assert.That(composite.Count, Is.EqualTo(2));
            Assert.That(composite.MasterVolume, Is.EqualTo(0.8f));
            Assert.That(composite.GetVolume(VolumeKind.Bgm), Is.EqualTo(0.25f));
            Assert.That(composite.GetVolume(VolumeKind.Se), Is.EqualTo(0.5f));
            Assert.That(composite.GetHubs(VolumeKind.Bgm), Is.EqualTo(new[] { bgm1, bgm2 }));
            Assert.That(composite.GetHubs(VolumeKind.Se), Is.EqualTo(new[] { se }));
            Assert.That(bgm1.AppliedVolumes, Is.EqualTo(new[] { 0.2f }).Within(0.0001f));
            Assert.That(bgm2.AppliedVolumes, Is.EqualTo(new[] { 0.2f }).Within(0.0001f));
            Assert.That(se.AppliedVolumes, Is.EqualTo(new[] { 0.4f }).Within(0.0001f));
        }

        [Test]
        public void BuilderCustomComparerControlsIdEquality()
        {
            var hub1 = new RecordingAudioHub<AudioClip>();
            var hub2 = new RecordingAudioHub<AudioClip>();
            var builder = new CompositeVolumeAudioHub<AudioClip, string>.Builder(
                StringComparer.OrdinalIgnoreCase);
            builder.AddHub("BGM", hub1);
            builder.AddHub("bgm", hub2);

            var composite = builder.Build();

            Assert.That(composite.Count, Is.EqualTo(1));
            Assert.That(composite.GetHubs("BgM"), Is.EqualTo(new[] { hub1, hub2 }));
        }

        [Test]
        public void BuilderCanSetMasterVolumeAfterConstruction()
        {
            var hub = new RecordingAudioHub<AudioClip>();
            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder();
            builder.AddHub(VolumeKind.Bgm, hub);

            builder.SetMasterVolume(0.6f);
            var composite = builder.Build();

            Assert.That(composite.MasterVolume, Is.EqualTo(0.6f));
            Assert.That(hub.AppliedVolumes[^1], Is.EqualTo(0.3f).Within(0.0001f));
        }

        [Test]
        public void BuilderPreservesExplicitVerySmallPositiveMasterVolume()
        {
            var hub = new RecordingAudioHub<AudioClip>();
            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder(
                masterVolume: float.Epsilon);
            builder.AddHub(VolumeKind.Bgm, hub);

            var composite = builder.Build();

            Assert.That(composite.MasterVolume, Is.EqualTo(float.Epsilon));
        }

        [Test]
        public void BuilderAfterBuildStartsWithNewDictionary()
        {
            var firstHub = new RecordingAudioHub<AudioClip>();
            var secondHub = new RecordingAudioHub<AudioClip>();
            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder();
            builder.AddHub(VolumeKind.Bgm, firstHub);
            var first = builder.Build();

            builder.AddHub(VolumeKind.Se, secondHub);
            var second = builder.Build();

            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(first.GetHubs(VolumeKind.Bgm), Is.EqualTo(new[] { firstHub }));
            Assert.That(second.Count, Is.EqualTo(1));
            Assert.That(second.GetHubs(VolumeKind.Se), Is.EqualTo(new[] { secondHub }));
            Assert.Throws<KeyNotFoundException>(() => second.GetVolume(VolumeKind.Bgm));
        }

        [Test]
        public void BuilderSetVolumeForUnknownIdThrowsForBothUninitializedAndInitializedBuilder()
        {
            var defaultBuilder = default(CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder);
            var initializedBuilder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder();
            initializedBuilder.AddHub(VolumeKind.Bgm, new RecordingAudioHub<AudioClip>());

            var first = Assert.Throws<KeyNotFoundException>(
                () => defaultBuilder.SetVolume(VolumeKind.Se, 0.5f));
            var second = Assert.Throws<KeyNotFoundException>(
                () => initializedBuilder.SetVolume(VolumeKind.Se, 0.5f));

            StringAssert.Contains(nameof(VolumeKind.Se), first!.Message);
            StringAssert.Contains(nameof(VolumeKind.Se), second!.Message);
        }

        [TestCase(0.0f)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        public void BuilderRejectsInvalidVolumes(float volume)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder(masterVolume: volume));

            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder();
            builder.AddHub(VolumeKind.Bgm, new RecordingAudioHub<AudioClip>());
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetMasterVolume(volume));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetVolume(VolumeKind.Bgm, volume));
        }

        [Test]
        public void ApplyVolumeUpdatesOnlySpecifiedId()
        {
            var bgm1 = new RecordingAudioHub<AudioClip>();
            var bgm2 = new RecordingAudioHub<AudioClip>();
            var se = new RecordingAudioHub<AudioClip>();
            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder(masterVolume: 0.5f);
            builder.AddHub(VolumeKind.Bgm, bgm1);
            builder.AddHub(VolumeKind.Bgm, bgm2);
            builder.AddHub(VolumeKind.Se, se);
            var composite = builder.Build();

            composite.ApplyVolume(VolumeKind.Bgm, 0.8f);

            Assert.That(composite.GetVolume(VolumeKind.Bgm), Is.EqualTo(0.8f));
            Assert.That(bgm1.AppliedVolumes, Is.EqualTo(new[] { 0.25f, 0.4f }).Within(0.0001f));
            Assert.That(bgm2.AppliedVolumes, Is.EqualTo(new[] { 0.25f, 0.4f }).Within(0.0001f));
            Assert.That(se.AppliedVolumes, Is.EqualTo(new[] { 0.25f }).Within(0.0001f));
        }

        [Test]
        public void ApplyMasterVolumeUpdatesAllIds()
        {
            var bgm = new RecordingAudioHub<AudioClip>();
            var se = new RecordingAudioHub<AudioClip>();
            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder(masterVolume: 0.5f);
            builder.AddHub(VolumeKind.Bgm, bgm);
            builder.AddHub(VolumeKind.Se, se);
            builder.SetVolume(VolumeKind.Se, 0.2f);
            var composite = builder.Build();

            composite.ApplyMasterVolume(0.75f);

            Assert.That(composite.MasterVolume, Is.EqualTo(0.75f));
            Assert.That(bgm.AppliedVolumes[^1], Is.EqualTo(0.375f).Within(0.0001f));
            Assert.That(se.AppliedVolumes[^1], Is.EqualTo(0.15f).Within(0.0001f));
        }

        [TestCase(0.0f)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        public void CompositeRejectsInvalidVolumes(float volume)
        {
            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder();
            builder.AddHub(VolumeKind.Bgm, new RecordingAudioHub<AudioClip>());
            var composite = builder.Build();

            Assert.Throws<ArgumentOutOfRangeException>(() => composite.ApplyMasterVolume(volume));
            Assert.Throws<ArgumentOutOfRangeException>(() => composite.ApplyVolume(VolumeKind.Bgm, volume));
        }

        [Test]
        public void CompositeUnknownIdThrows()
        {
            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder();
            builder.AddHub(VolumeKind.Bgm, new RecordingAudioHub<AudioClip>());
            var composite = builder.Build();

            Assert.Throws<KeyNotFoundException>(() => composite.GetVolume(VolumeKind.Se));
            Assert.Throws<KeyNotFoundException>(() => composite.GetHubs(VolumeKind.Se));
            Assert.Throws<KeyNotFoundException>(() => composite.ApplyVolume(VolumeKind.Se, 0.5f));
        }

        [Test]
        public void BuildWhenHubThrowsDoesNotConsumeBuilder()
        {
            var throwingHub = new ThrowingVolumeAudioHub();
            var builder = new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder();
            builder.AddHub(VolumeKind.Bgm, throwingHub);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        private sealed class ThrowingVolumeAudioHub : IAudioHub<AudioClip>
        {
            public ReadOnlySpan<AudioSource> AudioSources => ReadOnlySpan<AudioSource>.Empty;

            public Cysharp.Threading.Tasks.UniTask PlayAsync(
                AudioClip key,
                System.Threading.CancellationToken cancellationToken) =>
                Cysharp.Threading.Tasks.UniTask.CompletedTask;

            public void StopAll()
            {
            }

            public void ApplyVolume(float value) => throw new InvalidOperationException();
        }
    }
}
