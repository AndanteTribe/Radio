#if ENABLE_ADDRESSABLES
#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Radio
{
    [ExcludeFromCodeCoverage]
    internal sealed class AsyncOperationHandleEqualityComparer : IEqualityComparer<AsyncOperationHandle>
    {
        public static readonly IEqualityComparer<AsyncOperationHandle> Default = new AsyncOperationHandleEqualityComparer();

        private AsyncOperationHandleEqualityComparer()
        {
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IEqualityComparer<AsyncOperationHandle>.Equals(AsyncOperationHandle x, AsyncOperationHandle y) => x.Equals(y);

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int IEqualityComparer<AsyncOperationHandle>.GetHashCode(AsyncOperationHandle obj) => obj.GetHashCode();
    }
}

#endif
