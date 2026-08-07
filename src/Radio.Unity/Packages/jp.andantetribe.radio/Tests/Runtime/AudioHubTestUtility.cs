#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Radio.Tests
{
    internal sealed class RecordingAudioHub<T> : IAudioHub<T>
    {
        private readonly AudioSource[] _audioSources;

        public readonly List<(T Key, CancellationToken CancellationToken)> PlayCalls = new();
        public readonly List<float> AppliedVolumes = new();

        public Func<T, CancellationToken, UniTask>? PlayHandler { get; set; }

        public int StopCount { get; private set; }

        public ReadOnlySpan<AudioSource> AudioSources => _audioSources;

        public RecordingAudioHub(params AudioSource[] audioSources)
        {
            _audioSources = audioSources;
        }

        public UniTask PlayAsync(T key, CancellationToken cancellationToken)
        {
            PlayCalls.Add((key, cancellationToken));
            return PlayHandler?.Invoke(key, cancellationToken) ?? UniTask.CompletedTask;
        }

        public void StopAll() => StopCount++;

        public void ApplyVolume(float value) => AppliedVolumes.Add(value);
    }

    internal static class AudioHubTestUtility
    {
        public static AudioClip CreateClip(string name, float seconds)
        {
            const int frequency = 44100;
            var samples = Math.Max(1, Mathf.CeilToInt(seconds * frequency));
            return AudioClip.Create(name, samples, 1, frequency, false);
        }

        public static async UniTask AssertCanceled(UniTask task)
        {
            try
            {
                await task;
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        public static async UniTask<TException> AssertThrows<TException>(UniTask task)
            where TException : Exception
        {
            try
            {
                await task;
                Assert.Fail($"Expected {typeof(TException).Name}.");
                throw new InvalidOperationException();
            }
            catch (TException exception)
            {
                return exception;
            }
        }
    }
}
