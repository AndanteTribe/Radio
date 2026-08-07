#if ENABLE_ADDRESSABLES
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Radio
{
    /// <summary>
    /// Retains successful addressable clip acquisitions until this hub is disposed.
    /// </summary>
    public class CachedAddressableAudioHub : IAudioHub<string>, IAudioHub<AssetReferenceT<AudioClip>>, IDisposable
    {
        private readonly IAudioHub<AudioClip> _original;
        private readonly Dictionary<AsyncOperationHandle, int> _handleReferenceCounts = new(AsyncOperationHandleEqualityComparer.Default);

        /// <inheritdoc cref="IAudioHub{T}.AudioSources" />
        public ReadOnlySpan<AudioSource> AudioSources => _original.AudioSources;

        /// <summary>
        /// Initializes a new instance of the <see cref="CachedAddressableAudioHub"/> class.
        /// </summary>
        /// <param name="original">The clip hub that performs playback after loading.</param>
        public CachedAddressableAudioHub(IAudioHub<AudioClip> original)
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
            var result = default(AudioClip);
            try
            {
                result = await handle.ToUniTask(cancellationToken: cancellationToken);
                if (result == null)
                {
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }
                    return;
                }
                if (!_handleReferenceCounts.TryAdd(handle, 1))
                {
                    _handleReferenceCounts[handle]++;
                }
            }
            catch
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                throw;
            }

            await _original.PlayAsync(result, cancellationToken);
        }

        /// <inheritdoc cref="IAudioHub{T}.StopAll" />
        public void StopAll() => _original.StopAll();

        /// <inheritdoc cref="IAudioHub{T}.ApplyVolume(float)" />
        public void ApplyVolume(float value) => _original.ApplyVolume(value);

        /// <inheritdoc cref="IDisposable.Dispose" />
        public void Dispose()
        {
            foreach (var (handle, refCount) in _handleReferenceCounts)
            {
                for (var i = 0; i < refCount && handle.IsValid(); i++)
                {
                    Addressables.Release(handle);
                }
            }
            _handleReferenceCounts.Clear();
        }
    }
}

#endif
