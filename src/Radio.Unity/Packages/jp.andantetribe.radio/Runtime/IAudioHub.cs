#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    public interface IAudioHub<in T>
    {
        ReadOnlySpan<AudioSource> AudioSources { get; }

        UniTask PlayAsync(T key, CancellationToken cancellationToken);

        void StopAll();

        void ApplyVolume(float value);
    }

    public interface ILoopableAudioHub<in T> : IAudioHub<T>
    {
        void ApplyLoop(bool value);
    }
}