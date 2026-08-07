#nullable enable

using System;
using UnityEngine;

namespace Radio
{
    /// <summary>
    /// Exposes only the BGM channel operations required by a transition implementation.
    /// </summary>
    public sealed class BgmTransitionContext
    {
        private readonly AudioPlayer _player;

        internal BgmTransitionContext(AudioPlayer player)
        {
            _player = player;
        }

        /// <summary>
        /// Gets the BGM channel selected by the most recent playback operation.
        /// </summary>
        public AudioSource? CurrentBgmChannel => _player.CurrentBgmChannel;

        /// <summary>
        /// Gets the current master-volume and BGM-volume product.
        /// </summary>
        public float ManagedBgmVolume => _player.ManagedBgmVolume;

        /// <summary>
        /// Selects and returns the next BGM channel in rotation.
        /// </summary>
        public AudioSource GetAvailableBgmChannel() => _player.GetAvailableBgmChannel();

        /// <summary>
        /// Temporarily prevents Radio's volume setters from overwriting transition-owned channels.
        /// </summary>
        public IDisposable AcquireVolumeControl(params AudioSource[] channels)
        {
            foreach (var channel in channels)
            {
                _player.ExcludeFromVolumeManagement(channel);
            }

            return new VolumeControlLease(_player, channels);
        }

        private sealed class VolumeControlLease : IDisposable
        {
            private readonly AudioPlayer _player;
            private readonly AudioSource[] _channels;
            private bool _disposed;

            public VolumeControlLease(AudioPlayer player, AudioSource[] channels)
            {
                _player = player;
                _channels = channels;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                foreach (var channel in _channels)
                {
                    _player.IncludeInVolumeManagement(channel);
                }
            }
        }
    }
}
