#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    /// <summary>
    /// Provides audio playback for keys of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type used to identify audio content.</typeparam>
    public interface IAudioHub<in T>
    {
        /// <summary>
        /// Gets the audio sources managed by this hub.
        /// </summary>
        ReadOnlySpan<AudioSource> AudioSources { get; }

        /// <summary>
        /// Starts playback for the specified key and completes when the implementation-defined playback lifetime ends.
        /// </summary>
        /// <param name="key">The key that identifies the audio content to play.</param>
        /// <param name="cancellationToken">A token that cancels the playback operation.</param>
        /// <returns>A task that represents the playback lifetime.</returns>
        /// <exception cref="OperationCanceledException">
        /// <paramref name="cancellationToken"/> is cancelled.
        /// </exception>
        UniTask PlayAsync(T key, CancellationToken cancellationToken);

        /// <summary>
        /// Stops playback on all audio sources managed by this hub.
        /// </summary>
        void StopAll();

        /// <summary>
        /// Applies an effective volume to this hub.
        /// </summary>
        /// <param name="value">The volume, greater than <c>0</c> and less than or equal to <c>1</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="value"/> is not greater than <c>0</c> and less than or equal to <c>1</c>.
        /// </exception>
        void ApplyVolume(float value);
    }

    /// <summary>
    /// Extends an audio hub with configurable looping.
    /// </summary>
    /// <typeparam name="T">The type used to identify audio content.</typeparam>
    public interface ILoopableAudioHub<in T> : IAudioHub<T>
    {
        /// <summary>
        /// Applies the looping state to all managed audio sources and subsequent playback.
        /// </summary>
        /// <param name="value"><see langword="true"/> to enable looping; otherwise, <see langword="false"/>.</param>
        void ApplyLoop(bool value);
    }
}
