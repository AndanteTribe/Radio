using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    public interface IAudioClipLoad<T> : IDisposable
    {
        public UniTask<AudioClip> LoadAsync(T value, CancellationToken cancellationToken);

        /// <summary>
        /// <see cref="LoadAsync"/>で読み込んだものを1件解放する。同じvalueで同時に複数回ロードしていた場合は、そのうちの1件を解放する。
        /// </summary>
        public void Release(T value);
    }
}
