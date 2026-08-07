#nullable enable

using System;
using System.Collections.Generic;

namespace Radio
{
    /// <summary>
    /// Applies a master volume and per-ID volumes to groups of audio hubs.
    /// </summary>
    /// <typeparam name="TClip">The clip key type accepted by the registered hubs.</typeparam>
    /// <typeparam name="TId">The type used to identify a volume group.</typeparam>
    public sealed class CompositeVolumeAudioHub<TClip, TId> : IDisposable where TId : notnull
    {
        private readonly Dictionary<TId, Entry> _entries;

        /// <summary>
        /// Gets the master volume multiplied into every group volume.
        /// </summary>
        public float MasterVolume { get; private set; }

        /// <summary>
        /// Gets the number of registered volume groups.
        /// </summary>
        public int Count => _entries.Count;

        internal CompositeVolumeAudioHub(Dictionary<TId, Entry> entries, float masterVolume)
        {
            _entries = entries;
            MasterVolume = masterVolume;
            ApplyAllVolumes();
        }

        /// <summary>
        /// Gets the volume configured for the specified group.
        /// </summary>
        /// <param name="id">The group identifier.</param>
        /// <returns>The group's volume before multiplication by <see cref="MasterVolume"/>.</returns>
        /// <exception cref="KeyNotFoundException"><paramref name="id"/> is not registered.</exception>
        public float GetVolume(TId id) => _entries[id].Volume;

        /// <summary>
        /// Gets the audio hubs registered in the specified group.
        /// </summary>
        /// <param name="id">The group identifier.</param>
        /// <returns>The hubs registered in the group, in registration order.</returns>
        /// <exception cref="KeyNotFoundException"><paramref name="id"/> is not registered.</exception>
        public IReadOnlyList<IAudioHub<TClip>> GetHubs(TId id) => _entries[id].Hubs;

        /// <summary>
        /// Applies a master volume and reapplies the effective volume of every group.
        /// </summary>
        /// <param name="value">The master volume, greater than <c>0</c> and less than or equal to <c>1</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="value"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
        /// </exception>
        public void ApplyMasterVolume(float value)
        {
            ThrowHelper.ThrowIfVolumeOutOfRange(value);
            MasterVolume = value;
            ApplyAllVolumes();
        }

        /// <summary>
        /// Applies a volume to one group and updates its registered hubs.
        /// </summary>
        /// <param name="id">The group identifier.</param>
        /// <param name="value">The group volume, greater than <c>0</c> and less than or equal to <c>1</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="value"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
        /// </exception>
        /// <exception cref="KeyNotFoundException"><paramref name="id"/> is not registered.</exception>
        public void ApplyVolume(TId id, float value)
        {
            ThrowHelper.ThrowIfVolumeOutOfRange(value);
            var entry = _entries[id];
            entry.Volume = value;
            _entries[id] = entry;
            entry.ApplyVolume(MasterVolume);
        }

        private void ApplyAllVolumes()
        {
            foreach (var (_, entry) in _entries)
            {
                entry.ApplyVolume(MasterVolume);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var (_, entry) in _entries)
            {
                foreach (var hub in entry.Hubs)
                {
                    if (hub is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }

        internal struct Entry
        {
            public readonly List<IAudioHub<TClip>> Hubs;

            public float Volume;

            public Entry(IAudioHub<TClip> hub, float volume)
            {
                Hubs = new List<IAudioHub<TClip>>(1) { hub };
                Volume = volume;
            }

            public void ApplyVolume(float masterVolume)
            {
                var volume = masterVolume * Volume;
                foreach (var hub in Hubs)
                {
                    hub.ApplyVolume(volume);
                }
            }
        }

        /// <summary>
        /// Builds a composite volume hub from mutable group registrations and volume settings.
        /// </summary>
        public struct Builder
        {
            private Dictionary<TId, Entry>? _entries;
            private float _masterVolume;

            /// <summary>
            /// Initializes a new builder.
            /// </summary>
            /// <param name="comparer">The comparer used for group identifiers.</param>
            /// <param name="masterVolume">The initial master volume.</param>
            /// <exception cref="ArgumentOutOfRangeException">
            /// <paramref name="masterVolume"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
            /// </exception>
            public Builder(IEqualityComparer<TId>? comparer = null, float masterVolume = 0.5f)
            {
                ThrowHelper.ThrowIfVolumeOutOfRange(masterVolume);
                _entries = new Dictionary<TId, Entry>(comparer);
                _masterVolume = masterVolume;
            }

            /// <summary>
            /// Registers an audio hub in a volume group.
            /// </summary>
            /// <param name="id">The group identifier.</param>
            /// <param name="hub">The audio hub to register.</param>
            public void AddHub(TId id, IAudioHub<TClip> hub)
            {
                _entries ??= new Dictionary<TId, Entry>();
                if (_entries.TryGetValue(id, out var entry))
                {
                    entry.Hubs.Add(hub);
                }
                else
                {
                    _entries.Add(id, new Entry(hub, 0.5f));
                }
            }

            /// <summary>
            /// Sets the volume of a registered group.
            /// </summary>
            /// <param name="id">The group identifier.</param>
            /// <param name="value">The group volume.</param>
            /// <exception cref="ArgumentOutOfRangeException">
            /// <paramref name="value"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
            /// </exception>
            /// <exception cref="KeyNotFoundException"><paramref name="id"/> is not registered.</exception>
            public void SetVolume(TId id, float value)
            {
                ThrowHelper.ThrowIfVolumeOutOfRange(value);
                if (_entries == null || !_entries.TryGetValue(id, out var entry))
                {
                    throw new KeyNotFoundException($"The specified ID '{id}' has not been registered.");
                }

                entry.Volume = value;
                _entries[id] = entry;
            }

            /// <summary>
            /// Sets the master volume used when the composite is built.
            /// </summary>
            /// <param name="value">The master volume.</param>
            /// <exception cref="ArgumentOutOfRangeException">
            /// <paramref name="value"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
            /// </exception>
            public void SetMasterVolume(float value)
            {
                ThrowHelper.ThrowIfVolumeOutOfRange(value);
                _masterVolume = value;
            }

            /// <summary>
            /// Builds a composite from the current registrations and resets this builder's registrations.
            /// </summary>
            /// <returns>The configured composite volume hub.</returns>
            public CompositeVolumeAudioHub<TClip, TId> Build()
            {
                var entries = _entries ?? new Dictionary<TId, Entry>();
                var masterVolume = _masterVolume == 0.0f ? 0.5f : _masterVolume;
                var result = new CompositeVolumeAudioHub<TClip, TId>(entries, masterVolume);
                _entries = null;
                return result;
            }
        }
    }
}
