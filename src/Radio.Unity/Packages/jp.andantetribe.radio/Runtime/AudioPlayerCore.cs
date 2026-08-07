using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    /// <summary>
    /// Audio playback implementation that is not tied to a specific asset loading mechanism (e.g. Addressables).
    /// The actual clip loading strategy is injected via <see cref="IAudioClipLoad{T}"/> instead of being implemented
    /// through subclassing, so switching loaders (Addressables, Resources, etc.) never touches this class.
    /// </summary>
    public class AudioPlayerCore<T> : IDisposable
    {
        private const float DefaultVolume = 0.5f;

        private readonly IAudioClipLoad<T> _audioClipLoad;

        private readonly AudioSource[] _allBGMChannels;
        private readonly T[] _bgmChannelKeys;
        private AudioSource[] BgmChannels => _allBGMChannels;

        private readonly AudioSource _seChannel;
        private AudioSource SeChannel => _seChannel;

        private readonly AudioSource _voiceChannel;
        private AudioSource VoiceChannel => _voiceChannel != null
            ? _voiceChannel
            : throw new InvalidOperationException("Voice channel is not enabled.");

        private float _masterVolume = DefaultVolume;
        private float _bgmVolume = DefaultVolume;
        private float _seVolume = DefaultVolume;
        private float _voiceVolume = DefaultVolume;

        private readonly AsyncReactiveProperty<int> _currentBgmChannelIndex = new(-1);

        public AudioPlayerCore(GameObject root, IAudioClipLoad<T> audioClipLoad, int bgmChannels = 3, bool useVoice = false)
        {
            _audioClipLoad = audioClipLoad;

            // BGMチャンネルを用意する
            _allBGMChannels = new AudioSource[bgmChannels];
            _bgmChannelKeys = new T[bgmChannels];
            for (var i = 0; i < bgmChannels; i++)
            {
                var source = root.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.volume = DefaultVolume;
                _allBGMChannels[i] = source;
            }

            // SE
            _seChannel = root.AddComponent<AudioSource>();
            _seChannel.playOnAwake = false;
            _seChannel.loop = false;
            _seChannel.volume = DefaultVolume;

            // Voice
            if (useVoice)
            {
                _voiceChannel = root.AddComponent<AudioSource>();
                _voiceChannel.playOnAwake = false;
                _voiceChannel.loop = false;
                _voiceChannel.volume = DefaultVolume;
            }
        }

        /// <summary>
        /// addressに用意されているBGMを再生する。再生中のBGMがあれば停止する。
        /// </summary>
        /// <param name="address"></param>
        /// <param name="loop"></param>
        /// <param name="cancellationToken"></param>
        public async UniTask PlayBgmAsync(T address, bool loop = true, CancellationToken cancellationToken = default)
        {
            var clip = await _audioClipLoad.LoadAsync(address, cancellationToken);
            await PlayBgmCoreAsync(address, clip, loop, cancellationToken);
        }

        /// <summary>
        /// 再生本編
        /// </summary>
        /// <param name="address"></param>
        /// <param name="clip"></param>
        /// <param name="loop"></param>
        /// <param name="cancellationToken"></param>
        private async UniTask PlayBgmCoreAsync(T address, AudioClip clip, bool loop, CancellationToken cancellationToken)
        {
            var channelIndex = GetAvailableBgmChannelIndex();
            var channel = BgmChannels[channelIndex];
            channel.Stop();
            ReleaseBgmChannel(channelIndex); // 使い回すチャンネルに前のクリップが残っていれば先に解放する

            channel.clip = clip;
            channel.loop = loop;
            channel.volume = _bgmVolume * _masterVolume;
            channel.Play();
            _bgmChannelKeys[channelIndex] = address;

            try
            {
                if (loop)
                {
                    await WaitUntilBgmChannelCyclesAsync(cancellationToken);
                }
                else
                {
                    using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    try
                    {
                        await UniTask.WhenAny(
                            UniTask.Delay(TimeSpan.FromSeconds(clip.length), cancellationToken: linkedCancellationTokenSource.Token).AsAsyncUnitUniTask(),
                            WaitUntilBgmChannelCyclesAsync(linkedCancellationTokenSource.Token));
                    }
                    finally
                    {
                        linkedCancellationTokenSource.Cancel();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                channel.Stop();
                ReleaseBgmChannel(channelIndex);
                channel.clip = null;
                channel.loop = false;
                throw;
            }


            // 複数回BGMが実行された時のために、BGMのチャンネルを回し続け、全て回しきったら最後に再生したチャンネルを停止.
            async UniTask<AsyncUnit> WaitUntilBgmChannelCyclesAsync(CancellationToken token)
            {
                for (var i = 0; i < BgmChannels.Length; i++)
                {
                    var chIndex = await _currentBgmChannelIndex.WaitAsync(token);
                    if (chIndex < 0)
                    {
                        break;
                    }
                }
                return AsyncUnit.Default;
            }
        }

        /// <summary>
        /// BGMを止める。
        /// </summary>
        public void StopAllBgm()
        {
            for (var i = 0; i < BgmChannels.Length; i++)
            {
                var channel = BgmChannels[i];
                channel.Stop();
                ReleaseBgmChannel(i);
                channel.clip = null;
                channel.loop = false;
            }

            _currentBgmChannelIndex.Value = -1;
        }

        /// <summary>
        /// SEを流す
        /// </summary>
        /// <param name="address"></param>
        /// <param name="cancellationToken"></param>
        public UniTask PlaySeAsync(T address, CancellationToken cancellationToken = default) =>
            PlayNonBgmCoreAsync(SeChannel, address, cancellationToken);

        /// <summary>
        /// 音声を流す. 音声チャンネルが有効化されていない場合は例外を投げる
        /// </summary>
        /// <param name="address"></param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public UniTask PlayVoiceAsync(T address, CancellationToken cancellationToken = default)
        {
            if (_voiceChannel == null)
            {
                throw new InvalidOperationException("Voice channel is not enabled.");
            }

            return PlayNonBgmCoreAsync(_voiceChannel, address, cancellationToken);
        }

        /// <summary>
        /// SE・Voiceの再生本編。指定channelでaddressのクリップをワンショット再生する。
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="address"></param>
        /// <param name="cancellationToken"></param>
        private async UniTask PlayNonBgmCoreAsync(AudioSource channel, T address, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clip = await _audioClipLoad.LoadAsync(address, cancellationToken);
            try
            {
                if (clip == null)
                {
                    return;
                }

                channel.PlayOneShot(clip);
                await UniTask.Delay(TimeSpan.FromSeconds(clip.length), cancellationToken: cancellationToken);
            }
            finally
            {
                _audioClipLoad.Release(address);
            }
        }

        /// <summary>
        /// Sets the master volume affecting all audio channels.
        /// </summary>
        /// <param name="volume"></param>
        public void SetMasterVolume(float volume)
        {
            _masterVolume = Mathf.Clamp01(volume);
            SeChannel.volume = _seVolume * _masterVolume;
            if (_voiceChannel != null)
            {
                _voiceChannel.volume = _voiceVolume * _masterVolume;
            }
            foreach (var channel in BgmChannels)
            {
                channel.volume = _bgmVolume * _masterVolume;
            }
        }

        /// <summary>
        /// Sets the BGM volume.
        /// </summary>
        /// <param name="volume"></param>
        public void SetBgmVolume(float volume)
        {
            _bgmVolume = Mathf.Clamp01(volume);
            foreach (var channel in BgmChannels)
            {
                channel.volume = _bgmVolume * _masterVolume;
            }
        }

        /// <summary>
        /// Sets the sound effect volume.
        /// </summary>
        /// <param name="volume"></param>
        public void SetSeVolume(float volume)
        {
            _seVolume = Mathf.Clamp01(volume);
            SeChannel.volume = _seVolume * _masterVolume;
        }

        /// <summary>
        /// Sets the voice volume.
        /// </summary>
        /// <param name="volume"></param>
        public void SetVoiceVolume(float volume)
        {
            _voiceVolume = Mathf.Clamp01(volume);
            VoiceChannel.volume = _voiceVolume * _masterVolume;
        }

        /// <summary>
        /// 全チャンネルを停止してclip参照を外す。BGM/SE/Voiceいずれも再生の区切り(再生完了・チャンネル差し替え・停止)ごとに
        /// <see cref="IAudioClipLoad{T}.Release"/>を呼んでいるため、ここでの<see cref="IAudioClipLoad{T}.Dispose"/>は
        /// 再生タスクが完了しないまま破棄された場合などに備えたセーフティネットの位置づけ。
        /// </summary>
        public virtual void BgmClear()
        {
            StopAllBgm();

            SeChannel.Stop();
            SeChannel.clip = null;

            if (_voiceChannel != null)
            {
                _voiceChannel.Stop();
                _voiceChannel.clip = null;
            }

            _audioClipLoad?.Dispose();
        }

        /// <inheritdoc />
        public void Dispose() => BgmClear();

        private int GetAvailableBgmChannelIndex() =>
            _currentBgmChannelIndex.Value = (_currentBgmChannelIndex.Value + 1) % BgmChannels.Length;

        /// <summary>
        /// 指定したBGMチャンネルに現在ロードされているクリップがあれば、ローダーに解放を依頼する。
        /// </summary>
        /// <param name="channelIndex"></param>
        private void ReleaseBgmChannel(int channelIndex)
        {
            if (BgmChannels[channelIndex].clip != null)
            {
                _audioClipLoad.Release(_bgmChannelKeys[channelIndex]);
            }
        }
    }
}
