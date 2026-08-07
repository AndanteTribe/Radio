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
    /// <see cref="IAudioClipLoad{T}"/> implementation that loads clips directly through Addressables, keyed by address.
    /// Inject an instance of this into <see cref="AudioPlayerCore{T}"/> to back it with Addressables without
    /// the core class itself depending on Addressables.
    /// </summary>
    public class AddressableAudioClipLoad : IAudioClipLoad<string>
    {
        // valueごとにhandleを溜めておく。同じvalueで同時に複数回ロードされても、Releaseは古い方から順に(FIFOで)1件ずつ解放する。
        private readonly Dictionary<string, Queue<AsyncOperationHandle<AudioClip>>> _handles = new();

        /// <inheritdoc />
        public async UniTask<AudioClip> LoadAsync(string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = Addressables.LoadAssetAsync<AudioClip>(value);
            var result = await handle.ToUniTask(cancellationToken: cancellationToken, autoReleaseWhenCanceled: true);

            if (!_handles.TryGetValue(value, out var queue))
            {
                _handles[value] = queue = new Queue<AsyncOperationHandle<AudioClip>>();
            }
            queue.Enqueue(handle);

            return result;
        }

        /// <inheritdoc />
        public void Release(string value)
        {
            if (!_handles.TryGetValue(value, out var queue) || queue.Count == 0)
            {
                return;
            }

            var handle = queue.Dequeue();
            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }

            if (queue.Count == 0)
            {
                _handles.Remove(value);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Releaseされないまま残ったhandleをまとめて解放する(取りこぼし用のセーフティネット)。
            foreach (var queue in _handles.Values)
            {
                while (queue.Count > 0)
                {
                    var handle = queue.Dequeue();
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }
                }
            }
            _handles.Clear();
        }
    }
}
