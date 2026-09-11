using System;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record AnalysisSession(long RequestId, CancellationToken Token);

    public sealed class AnalysisSessionCoordinator : IDisposable
    {
        private readonly object _sync = new();
        private CancellationTokenSource? _currentCancellation;
        private long _currentRequestId;
        private AnalysisDocumentKind _currentKind;
        private bool _disposed;
        private long _revision;
        private long _stageStarted;
        private readonly List<AnalysisStageTiming> _timings = [];
        private readonly IUnexpectedErrorReporter _reporter;
        private AnalysisProgress _progress = new(0, 0, AnalysisOperationState.Idle, "", []);
        public AnalysisSessionCoordinator(IUnexpectedErrorReporter? reporter = null) => _reporter = reporter ?? UnexpectedErrorReporter.Shared;
        public event EventHandler<AnalysisProgress>? ProgressChanged;
        public AnalysisProgress Progress { get { lock (_sync) return _progress; } }
        public AnalysisSession? Current { get { lock (_sync) return _currentCancellation == null ? null : new(_currentRequestId, _currentCancellation.Token); } }

        public AnalysisSession Begin(AnalysisDocumentKind kind = AnalysisDocumentKind.Unknown)
        {
            AnalysisSession session;
            AnalysisProgress progress;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                CancelSource(_currentCancellation, dispose: true);
                _currentCancellation = new CancellationTokenSource();
                _currentKind = kind;
                long requestId = ++_currentRequestId;
                session = new AnalysisSession(requestId, _currentCancellation.Token);
                _timings.Clear(); _stageStarted = Stopwatch.GetTimestamp();
                progress = _progress = new(requestId, ++_revision, AnalysisOperationState.Loading, "", []);
            }
            Publish(progress);
            return session;
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
            var current = Current;
            if (current != null) Cancel(current.RequestId);
        }

        public bool TrySetKind(long requestId, AnalysisDocumentKind kind)
        {
            lock (_sync)
            {
                if (!IsCurrent(requestId)) return false;
                _currentKind = kind;
                return true;
            }
        }

        public bool CancelCurrent(AnalysisDocumentKind kind)
        {
            long id;
            lock (_sync)
            {
                if (_currentKind != kind || _currentCancellation?.IsCancellationRequested != false) return false;
                id = _currentRequestId;
            }
            return Cancel(id);
        }

        public bool Cancel(long requestId)
        {
            AnalysisProgress progress;
            lock (_sync)
            {
                if (!IsCurrent(requestId)) return false;
                progress = ChangeState(AnalysisOperationState.Cancelled, "操作已取消；未完成结果不会提交。");
                CancelSource(_currentCancellation, dispose: false);
            }
            Publish(progress);
            return true;
        }

        public bool Report(long requestId, AnalysisOperationState state, string detail = "")
        {
            AnalysisProgress progress;
            lock (_sync)
            {
                if (!IsCurrent(requestId) || !_progress.IsBusy || state == AnalysisOperationState.Idle) return false;
                if (state is AnalysisOperationState.Loading or AnalysisOperationState.Parsing or AnalysisOperationState.Analyzing
                    && state < _progress.State) return false;
                if (state == _progress.State && detail == _progress.Detail) return true;
                progress = ChangeState(state, detail);
            }
            Publish(progress);
            return true;
        }
        public void Reset()
        {
            CancelCurrent();
            AnalysisProgress progress;
            lock (_sync) progress = ChangeState(AnalysisOperationState.Idle, "");
            Publish(progress);
        }
        public string Fail(long requestId, Exception exception)
        {
            string detail = ExceptionPolicy.Describe(exception, "IMP26.Analysis", _reporter);
            if (exception is OperationCanceledException) Cancel(requestId);
            else Report(requestId, AnalysisOperationState.Failed, detail);
            return detail;
        }
        private AnalysisProgress ChangeState(AnalysisOperationState state, string detail)
        {
            if (_progress.IsBusy && state != _progress.State)
                _timings.Add(new(_progress.State, Stopwatch.GetElapsedTime(_stageStarted).TotalMilliseconds));
            if (state != _progress.State) _stageStarted = Stopwatch.GetTimestamp();
            return _progress = new(_currentRequestId, ++_revision, state, detail, _timings.ToArray());
        }
        private void Publish(AnalysisProgress progress)
        {
            Logger.Debug($"IMP26 request={progress.RequestId}, state={progress.State}.");
            if (progress.State == AnalysisOperationState.Partial) Logger.Warning("IMP26 analysis completed with partial input or failed checks.");
            ProgressChanged?.Invoke(this, progress);
        }
        private async void CancelSource(CancellationTokenSource? source, bool dispose)
        {
            if (source == null) return;
            // .NET 8 marks the token immediately and runs user callbacks asynchronously.
            try { await source.CancelAsync().ConfigureAwait(false); }
            catch (Exception exception) { ExceptionPolicy.Describe(exception, "IMP26.CancelCallbacks", _reporter); }
            finally { if (dispose) source.Dispose(); }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                CancelSource(_currentCancellation, dispose: true);
                _currentCancellation = null;
            }
        }
    }
}
