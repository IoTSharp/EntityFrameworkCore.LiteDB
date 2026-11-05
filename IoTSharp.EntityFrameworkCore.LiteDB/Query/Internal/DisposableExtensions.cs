using System;
using System.Collections;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore.Utilities;

internal static class DisposableExtensions
{
    public static ValueTask DisposeAsyncIfAvailable(this IEnumerator enumerator)
    {
        if (enumerator is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        (enumerator as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }
}
