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
    /// <summary>
    /// Loads an addressable clip for each playback request and releases its handle when playback ends.
    /// </summary>
    public class AddressableAudioHub : IAudioHub<string>, IAudioHub<AssetReferenceT<AudioClip>>
    {
        private readonly IAudioHub<AudioClip> _original;

        /// <inheritdoc cref="IAudioHub{T}.AudioSources" />
        public ReadOnlySpan<AudioSource> AudioSources => _original.AudioSources;

        /// <summary>
        /// Initializes a new instance of the <see cref="AddressableAudioHub"/> class.
        /// </summary>
        /// <param name="original">The clip hub that performs playback after loading.</param>
        public AddressableAudioHub(IAudioHub<AudioClip> original)
        {
            _original = original;
        }

        /// <inheritdoc cref="IAudioHub{T}.PlayAsync(T, CancellationToken)" />
        public UniTask PlayAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PlayCoreAsync(Addressables.LoadAssetAsync<AudioClip>(key), cancellationToken);
        }

        /// <inheritdoc cref="IAudioHub{T}.PlayAsync(T, CancellationToken)" />
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

        /// <inheritdoc cref="IAudioHub{T}.StopAll" />
        public void StopAll() => _original.StopAll();

        /// <inheritdoc cref="IAudioHub{T}.ApplyVolume(float)" />
        public void ApplyVolume(float value) => _original.ApplyVolume(value);
    }
}

#endif
