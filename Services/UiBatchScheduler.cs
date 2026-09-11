using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SqlXmlAnalyzer.Services;

public static class UiBatchScheduler
{
    public static async Task ApplyAsync<T>(IReadOnlyList<T> items, Action<T> apply, CancellationToken cancellationToken,
        Func<bool> isCurrent, int maximumBatchSize = 1)
    {
        ArgumentNullException.ThrowIfNull(items); ArgumentNullException.ThrowIfNull(apply); ArgumentNullException.ThrowIfNull(isCurrent);
        if (maximumBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBatchSize));
        int index = 0;
        while (index < items.Count)
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var watch = Stopwatch.StartNew();
            for (int count = 0; index < items.Count && count < maximumBatchSize; count++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!isCurrent()) throw new OperationCanceledException("视图请求已过期。", cancellationToken);
                apply(items[index++]);
                if (watch.ElapsedMilliseconds >= 4) break;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!isCurrent()) throw new OperationCanceledException("视图请求已过期。", cancellationToken);
        await Dispatcher.Yield(DispatcherPriority.Background);
        cancellationToken.ThrowIfCancellationRequested();
        if (!isCurrent()) throw new OperationCanceledException("视图请求已过期。", cancellationToken);
    }
}
