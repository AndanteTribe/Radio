#if ENABLE_ADDRESSABLES
#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Radio
{
    public class AddressableAudioHub : IAudioHub<string>, IAudioHub<AssetReferenceT<AudioClip>>
    {
        private readonly IAudioHub<AudioClip> _original;

        public ReadOnlySpan<AudioSource> AudioSources => _original.AudioSources;

        public AddressableAudioHub(IAudioHub<AudioClip> original)
        {
            _original = original;
        }

        public UniTask PlayAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PlayCoreAsync(Addressables.LoadAssetAsync<AudioClip>(key), cancellationToken);
        }

        public UniTask PlayAsync(AssetReferenceT<AudioClip> key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PlayCoreAsync(Addressables.LoadAssetAsync<AudioClip>(key), cancellationToken);
        }

        private async UniTask PlayCoreAsync(AsyncOperationHandle<AudioClip> handle, CancellationToken cancellationToken)
        {
            try
            {
                var result = await handle.ToUniTask(cancellationToken: cancellationToken);
                if (result != null)
                {
                    await _original.PlayAsync(result, cancellationToken);
                }
            }
            finally
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
        }

        public void StopAll() => _original.StopAll();

        public void ApplyVolume(float value) => _original.ApplyVolume(value);
    }
}

#endif
