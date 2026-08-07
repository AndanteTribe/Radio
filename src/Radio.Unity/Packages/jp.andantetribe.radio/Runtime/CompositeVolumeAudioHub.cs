#nullable enable

using System;
using System.Collections.Generic;

namespace Radio
{
    public sealed class CompositeVolumeAudioHub<TClip, TId> : IDisposable where TId : notnull
    {
        private readonly Dictionary<TId, Entry> _entries;

        public float MasterVolume { get; private set; }

        public int Count => _entries.Count;

        internal CompositeVolumeAudioHub(Dictionary<TId, Entry> entries, float masterVolume)
        {
            _entries = entries;
            MasterVolume = masterVolume;
            ApplyAllVolumes();
        }

        public float GetVolume(TId id) => _entries[id].Volume;

        public IReadOnlyList<IAudioHub<TClip>> GetHubs(TId id) => _entries[id].Hubs;

        public void ApplyMasterVolume(float value)
        {
            ThrowHelper.ThrowIfVolumeOutOfRange(value);
            MasterVolume = value;
            ApplyAllVolumes();
        }

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

        public struct Builder
        {
            private Dictionary<TId, Entry>? _entries;
            private float _masterVolume;

            public Builder(IEqualityComparer<TId>? comparer = null, float masterVolume = 0.5f)
            {
                ThrowHelper.ThrowIfVolumeOutOfRange(masterVolume);
                _entries = new Dictionary<TId, Entry>(comparer);
                _masterVolume = masterVolume;
            }

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

            public void SetMasterVolume(float value)
            {
                ThrowHelper.ThrowIfVolumeOutOfRange(value);
                _masterVolume = value;
            }

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
