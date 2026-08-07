#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Radio.Tests
{
    internal class InMemoryAudioClipProvider : ResourceProviderBase
    {
        private readonly IReadOnlyDictionary<string, AudioClip?> _clips;
        private readonly Dictionary<string, int> _loadCounts = new();
        private readonly Dictionary<string, int> _releaseCounts = new();
        private readonly Dictionary<string, Exception> _failures = new();
        private readonly Dictionary<string, UniTaskCompletionSource> _loadGates = new();

        public InMemoryAudioClipProvider(IReadOnlyDictionary<string, AudioClip?> clips) => _clips = clips;

        public int LoadCount(string key) => _loadCounts.GetValueOrDefault(key, 0);

        public int ReleaseCount(string key) => _releaseCounts.GetValueOrDefault(key, 0);

        public void Delay(string key)
        {
            if (!_loadGates.TryAdd(key, new UniTaskCompletionSource()))
            {
                throw new InvalidOperationException($"A load gate already exists for '{key}'.");
            }
        }

        public void CompleteDelayed(string key)
        {
            if (!_loadGates.TryGetValue(key, out var gate))
            {
                throw new InvalidOperationException($"No load gate exists for '{key}'.");
            }

            _loadGates.Remove(key);
            gate.TrySetResult();
        }

        public void CompleteAllDelayed()
        {
            foreach (var gate in _loadGates.Values)
            {
                gate.TrySetResult();
            }

            _loadGates.Clear();
        }

        public void Fail(string key, Exception exception) => _failures[key] = exception;

        public override Type GetDefaultType(IResourceLocation location) => typeof(AudioClip);

        public override bool CanProvide(Type type, IResourceLocation location) => typeof(AudioClip).IsAssignableFrom(type);

        public override void Provide(ProvideHandle provideHandle) => CompleteAsync(provideHandle).Forget();

        private async UniTaskVoid CompleteAsync(ProvideHandle provideHandle)
        {
            await UniTask.Yield();
            var key = provideHandle.Location.PrimaryKey;
            _loadCounts[key] = LoadCount(key) + 1;

            if (_loadGates.TryGetValue(key, out var gate))
            {
                await gate.Task;
            }

            if (_failures.TryGetValue(key, out var exception))
            {
                provideHandle.Complete<AudioClip?>(null, status: false, exception: exception);
                return;
            }

            if (!_clips.TryGetValue(key, out var clip))
            {
                _clips.TryGetValue(provideHandle.Location.InternalId, out clip);
            }
            provideHandle.Complete(clip, status: true, exception: null);
        }

        public override void Release(IResourceLocation location, object asset)
        {
            var key = location.PrimaryKey;
            _releaseCounts[key] = ReleaseCount(key) + 1;
        }
    }
}
