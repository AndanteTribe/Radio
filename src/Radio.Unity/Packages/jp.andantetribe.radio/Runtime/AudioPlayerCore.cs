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
    /// 汎用オーディオ再生実装.
    /// </summary>
    public partial class AudioPlayerCore : IDisposable
    {
        private const float DefaultVolume = 0.5f;

        private readonly AudioSource[] _allChannels;
        private readonly AssetsRegistry _bgmRegistry;
        private readonly bool _useVoice;

        private ReadOnlySpan<AudioSource> BgmChannels => _allChannels.AsSpan(_useVoice ? 2 : 1);
        private AudioSource SeChannel => _allChannels[0];
        private AudioSource VoiceChannel => _useVoice ? _allChannels[1] : throw new InvalidOperationException("Voice channel is not enabled.");

        private int _currentBgmChannelIndex = -1;
        private float _masterVolume = DefaultVolume;
        private float _bgmVolume = DefaultVolume;
        private float _seVolume = DefaultVolume;
        private float _voiceVolume = DefaultVolume;

        /// <summary>
        /// Initialize a new instance of <see cref="AudioPlayerCore"/>.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="bgmChannelCount"></param>
        /// <param name="useVoice"></param>
        /// <param name="bgmRegistry"></param>
        public AudioPlayerCore(GameObject root, int bgmChannelCount = 3, bool useVoice = false, AssetsRegistry? bgmRegistry = null)
        {
            _allChannels = root.GetComponents<AudioSource>();
            var existingChannels = _allChannels.AsSpan();

            var allChannelCount = bgmChannelCount + 1 + (useVoice ? 1 : 0); // BGM + SE + (Voice)
            if (existingChannels.Length < allChannelCount)
            {
                var channels = new AudioSource[allChannelCount];
                existingChannels.CopyTo(channels);
                for (var i = 0; i < channels.Length; i++)
                {
                    var channel = channels[i];
                    if (channel == null)
                    {
                        channel = channels[i] = root.AddComponent<AudioSource>();
                    }
                    channel.loop = false;
                    channel.playOnAwake = false;
                    channel.volume = DefaultVolume;
                }
                _allChannels = channels;
            }

            _bgmRegistry = bgmRegistry ?? new AssetsRegistry();
            _useVoice = useVoice;
        }

        /// <summary>
        /// BGMを非同期でロードして再生する.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="loop"></param>
        /// <param name="cancellationToken"></param>
        public async UniTaskVoid PlayBgmAsync(string address, bool loop = true, CancellationToken cancellationToken = default)
        {
            var clip = await _bgmRegistry.LoadAsync<AudioClip>(address, cancellationToken);
            PlayBgmCore(clip, loop);
        }

        public async UniTaskVoid PlayBgmAsync(AssetReferenceT<AudioClip> reference, bool loop = true, CancellationToken cancellationToken = default)
        {
            var clip = await _bgmRegistry.LoadAsync(reference, cancellationToken);
            PlayBgmCore(clip, loop);
        }

        private void PlayBgmCore(AudioClip clip, bool loop)
        {
            var channel = GetAvailableBgmChannel();
            channel.Stop();
            channel.clip = clip;
            channel.loop = loop;
            channel.volume = _bgmVolume * _masterVolume;
            channel.Play();
        }

        /// <summary>
        /// BGMを停止する.
        /// </summary>
        public void StopAllBgm()
        {
            foreach (var channel in BgmChannels)
            {
                if (channel.isPlaying)
                {
                    channel.Stop();
                    channel.clip = null;
                    channel.loop = false;
                }
            }
            _bgmRegistry.Clear();
            _currentBgmChannelIndex = -1;
        }

        /// <summary>
        /// SEを非同期でロードして再生する.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="cancellationToken"></param>
        public UniTask PlaySeAsync(string address, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = SeChannel;
            var handle = Addressables.LoadAssetAsync<AudioClip>(address);
            return PlayNonBgmCoreAsync(handle, channel, cancellationToken);
        }

        public UniTask PlaySeAsync(AssetReferenceT<AudioClip> reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = SeChannel;
            var handle = reference.LoadAssetAsync();
            return PlayNonBgmCoreAsync(handle, channel, cancellationToken);
        }

        public UniTask PlayVoiceAsync(string address, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = VoiceChannel;
            var handle = Addressables.LoadAssetAsync<AudioClip>(address);
            return PlayNonBgmCoreAsync(handle, channel, cancellationToken);
        }

        public UniTask PlayVoiceAsync(AssetReferenceT<AudioClip> reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = VoiceChannel;
            var handle = reference.LoadAssetAsync();
            return PlayNonBgmCoreAsync(handle, channel, cancellationToken);
        }

        private async UniTask PlayNonBgmCoreAsync(AsyncOperationHandle<AudioClip> handle, AudioSource channel, CancellationToken cancellationToken)
        {
            try
            {
                var result = await handle.ToUniTask(cancellationToken: cancellationToken, autoReleaseWhenCanceled: true);
                if (result == null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError("Failed to load SE: " + handle.DebugName);
#endif
                    return;
                }

                channel.PlayOneShot(result);
                await UniTask.Delay(TimeSpan.FromSeconds(result.length), cancellationToken: cancellationToken);
            }
            finally
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
        }

        public void SetMasterVolume(float volume)
        {
            _masterVolume = Mathf.Clamp01(volume);
            SeChannel.volume = _seVolume * _masterVolume;
            if (_useVoice)
            {
                VoiceChannel.volume = _voiceVolume * _masterVolume;
            }
        }

        public void SetBgmVolume(float volume)
        {
            _bgmVolume = Mathf.Clamp01(volume);
        }

        public void SetSeVolume(float volume)
        {
            var channel = SeChannel;
            _seVolume = Mathf.Clamp01(volume);
            channel.volume = _seVolume * _masterVolume;
        }

        public void SetVoiceVolume(float volume)
        {
            var channel = VoiceChannel;
            _voiceVolume = Mathf.Clamp01(volume);
            channel.volume = _voiceVolume * _masterVolume;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _bgmRegistry.Dispose();
        }

        private AudioSource GetAvailableBgmChannel() =>
            BgmChannels[_currentBgmChannelIndex = (_currentBgmChannelIndex + 1) % BgmChannels.Length];
    }
}