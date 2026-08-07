using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Radio.Tests
{
    public class InMemoryAudioClipProvider : ResourceProviderBase
    {
        private readonly IReadOnlyDictionary<string, AudioClip> _clips;
        private readonly Dictionary<string, int> _loadCounts = new();
        private readonly Dictionary<string, int> _releaseCounts = new();

        public InMemoryAudioClipProvider(IReadOnlyDictionary<string, AudioClip> clips) => _clips = clips;

        public int LoadCount(string key) => _loadCounts.GetValueOrDefault(key, 0);

        public int ReleaseCount(string key) => _releaseCounts.GetValueOrDefault(key, 0);

        public override Type GetDefaultType(IResourceLocation location) => typeof(AudioClip);

        public override bool CanProvide(Type type, IResourceLocation location) => typeof(AudioClip).IsAssignableFrom(type);

        public override void Provide(ProvideHandle provideHandle) => CompleteAsync(provideHandle).Forget();

        private async UniTaskVoid CompleteAsync(ProvideHandle provideHandle)
        {
            await UniTask.Yield();
            var key = provideHandle.Location.PrimaryKey;
            _loadCounts[key] = LoadCount(key) + 1;
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
