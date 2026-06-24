using System;
using System.Threading;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record AnalysisSession(long RequestId, CancellationToken Token);

    public sealed class AnalysisSessionCoordinator : IDisposable
    {
        private readonly object _sync = new();
        private CancellationTokenSource? _currentCancellation;
        private long _currentRequestId;

        public AnalysisSession Begin()
        {
            lock (_sync)
            {
                _currentCancellation?.Cancel();
                _currentCancellation?.Dispose();
                _currentCancellation = new CancellationTokenSource();
                long requestId = ++_currentRequestId;
                return new AnalysisSession(requestId, _currentCancellation.Token);
            }
        }

        public bool IsCurrent(long requestId)
        {
            lock (_sync)
            {
                return requestId == _currentRequestId
                    && _currentCancellation?.IsCancellationRequested == false;
            }
        }

        public void CancelCurrent()
        {
            lock (_sync)
            {
                _currentCancellation?.Cancel();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _currentCancellation?.Cancel();
                _currentCancellation?.Dispose();
                _currentCancellation = null;
            }
        }
    }
}
