using System;
using System.Threading;

namespace Client.Fundus;

// Accessed only on the UI thread. Async continuations check IsCurrent before touching the page.
public sealed class FoPageLifetime
{
    private bool _active;
    private Operation? _current;

    public bool Open()
    {
        if (_active) return false;
        _active = true;
        return true;
    }

    public void Close()
    {
        _active = false;
        _current?.Cancel();
        _current = null;
    }

    public Operation? TryStart()
    {
        if (!_active || _current != null) return null;
        return _current = new Operation(this);
    }

    public sealed class Operation : IDisposable
    {
        private readonly FoPageLifetime _owner;
        private readonly CancellationTokenSource _source = new();
        private bool _disposed;
        internal Operation(FoPageLifetime owner) => _owner = owner;
        public CancellationToken Token => _source.Token;
        public bool IsCurrent => !_disposed && _owner._active && ReferenceEquals(_owner._current, this) && !_source.IsCancellationRequested;
        internal void Cancel() => _source.Cancel();
        public void Dispose()
        {
            if (_disposed) return;
            if (ReferenceEquals(_owner._current, this)) _owner._current = null;
            _disposed = true;
            _source.Dispose();
        }
    }
}
