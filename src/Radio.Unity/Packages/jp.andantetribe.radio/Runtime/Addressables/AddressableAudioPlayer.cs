#nullable enable

using System;
using System.Threading;
using AndanteTribe.Unity.Extensions;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Radio
{
    /// <summary>
    /// Loads Addressable audio clips and delegates playback to <see cref="AudioPlayer"/>.
    /// </summary>
    public class AddressableAudioPlayer : AudioPlayer, IDisposable
    {
        private readonly AssetsRegistry _bgmRegistry;

        /// <summary>
        /// Initializes a new Addressables-backed player.
        /// </summary>
        /// <param name="root">The GameObject that owns the managed AudioSource components.</param>
        /// <param name="bgmChannelCount">The minimum number of BGM channels.</param>
        /// <param name="useVoice">Whether to reserve a dedicated voice channel.</param>
        /// <param name="bgmRegistry">An optional registry owned and disposed by this player.</param>
        public AddressableAudioPlayer(
            GameObject root,
            uint bgmChannelCount = 3,
            bool useVoice = false,
            AssetsRegistry? bgmRegistry = null)
            : base(root, bgmChannelCount, useVoice)
        {
            _bgmRegistry = bgmRegistry ?? new AssetsRegistry();
        }

        /// <summary>
        /// Loads a BGM by address and plays it on the next channel.
        /// </summary>
        public async UniTask PlayBgmAsync(string address, bool loop = true, CancellationToken cancellationToken = default)
        {
            var clip = await _bgmRegistry.LoadAsync<AudioClip>(
                address,
                cancellationToken);
            await base.PlayBgmAsync(clip, loop, cancellationToken);
        }

        /// <summary>
        /// Loads a BGM by asset reference and plays it on the next channel.
        /// </summary>
        public async UniTask PlayBgmAsync(
            AssetReferenceT<AudioClip> reference,
            bool loop = true,
            CancellationToken cancellationToken = default)
        {
            var clip = await _bgmRegistry.LoadAsync(
                reference,
                cancellationToken);
            await base.PlayBgmAsync(clip, loop, cancellationToken);
        }

        /// <summary>
        /// Loads a BGM by address and plays it using the configured transition.
        /// </summary>
        public async UniTask CrossFadeBgmAsync(
            string address,
            bool loop = true,
            CancellationToken cancellationToken = default)
        {
            var clip = await _bgmRegistry.LoadAsync<AudioClip>(
                address,
                cancellationToken);
            await base.CrossFadeBgmAsync(clip, loop, cancellationToken);
        }

        /// <summary>
        /// Loads a BGM by asset reference and plays it using the configured transition.
        /// </summary>
        public async UniTask CrossFadeBgmAsync(
            AssetReferenceT<AudioClip> reference,
            bool loop = true,
            CancellationToken cancellationToken = default)
        {
            var clip = await _bgmRegistry.LoadAsync(
                reference,
                cancellationToken);
            await base.CrossFadeBgmAsync(clip, loop, cancellationToken);
        }

        /// <inheritdoc />
        public override void StopAllBgm()
        {
            base.StopAllBgm();
            _bgmRegistry.Clear();
        }

        /// <summary>
        /// Loads and plays a sound effect by address.
        /// </summary>
        public UniTask PlaySeAsync(string address, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<AudioClip>(address);
            return PlayNonBgmAsync(handle, Sources.Se, cancellationToken);
        }

        /// <summary>
        /// Loads and plays a sound effect by asset reference.
        /// </summary>
        public UniTask PlaySeAsync(AssetReferenceT<AudioClip> reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = reference.LoadAssetAsync();
            return PlayNonBgmAsync(handle, Sources.Se, cancellationToken);
        }

        /// <summary>
        /// Loads and plays a voice clip by address.
        /// </summary>
        public UniTask PlayVoiceAsync(string address, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = Sources.Voice ?? throw new InvalidOperationException("Voice channel is not enabled.");
            var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<AudioClip>(address);
            return PlayNonBgmAsync(handle, channel, cancellationToken);
        }

        /// <summary>
        /// Loads and plays a voice clip by asset reference.
        /// </summary>
        public UniTask PlayVoiceAsync(
            AssetReferenceT<AudioClip> reference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = Sources.Voice ?? throw new InvalidOperationException("Voice channel is not enabled.");
            var handle = reference.LoadAssetAsync();
            return PlayNonBgmAsync(handle, channel, cancellationToken);
        }

        private static async UniTask PlayNonBgmAsync(
            AsyncOperationHandle<AudioClip> handle,
            AudioSource channel,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await handle.ToUniTask(
                    cancellationToken: cancellationToken,
                    autoReleaseWhenCanceled: true);
                if (result == null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError("Failed to load SE: " + handle.DebugName);
#endif
                    return;
                }

                await PlayNonBgmCoreAsync(result, channel, cancellationToken);
            }
            finally
            {
                if (handle.IsValid())
                {
                    UnityEngine.AddressableAssets.Addressables.Release(handle);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose() => _bgmRegistry.Dispose();
    }
}
