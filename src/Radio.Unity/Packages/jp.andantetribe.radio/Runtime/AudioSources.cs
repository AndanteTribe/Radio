#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Radio
{
    /// <summary>
    /// Exposes the <see cref="AudioSource"/> components managed by an <see cref="AudioPlayer"/>.
    /// </summary>
    public sealed class AudioSources
    {
        private readonly IReadOnlyList<AudioSource> _all;
        private readonly IReadOnlyList<AudioSource> _bgm;

        internal AudioSources(AudioSource[] channels, bool useVoice)
        {
            var bgmStartIndex = useVoice ? 2 : 1;
            var bgmChannels = new AudioSource[channels.Length - bgmStartIndex];
            Array.Copy(channels, bgmStartIndex, bgmChannels, 0, bgmChannels.Length);

            Se = channels[0];
            Voice = useVoice ? channels[1] : null;
            _bgm = Array.AsReadOnly(bgmChannels);
            _all = Array.AsReadOnly(channels);
        }

        /// <summary>
        /// Gets the sound-effect channel.
        /// </summary>
        public AudioSource Se { get; }

        /// <summary>
        /// Gets the voice channel, or <see langword="null"/> when voice playback is disabled.
        /// </summary>
        public AudioSource? Voice { get; }

        /// <summary>
        /// Gets the BGM channels in rotation order.
        /// </summary>
        public IReadOnlyList<AudioSource> Bgm => _bgm;

        /// <summary>
        /// Gets all managed channels in SE, optional Voice, then BGM order.
        /// </summary>
        public IReadOnlyList<AudioSource> All => _all;
    }
}
