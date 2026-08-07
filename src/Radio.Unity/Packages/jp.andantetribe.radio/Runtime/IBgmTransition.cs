#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Radio
{
    /// <summary>
    /// Defines a BGM transition without exposing the complete <see cref="AudioPlayer"/> API.
    /// </summary>
    public interface IBgmTransition
    {
        /// <summary>
        /// Transitions to <paramref name="clip"/> using only the operations exposed by
        /// <paramref name="context"/>.
        /// </summary>
        UniTask TransitionAsync(
            BgmTransitionContext context,
            AudioClip clip,
            bool loop,
            CancellationToken cancellationToken);
    }
}
