using System;
using System.Threading;

namespace Client.Helpers;

// The gate stays closed until the deferred notification finishes, including during a modal message loop.
public sealed class UnhandledUiErrorReporter
{
    private int _reporting;

    public void Report(Action log, Action markHandled, Func<Action, bool> enqueue, Action notify)
    {
        if (Interlocked.CompareExchange(ref _reporting, 1, 0) != 0) return;
        bool queued = false;
        try
        {
            // Preserve the original fault before doing anything that could raise another UI exception.
            try { log(); } catch { }
            markHandled();
            queued = enqueue(() =>
            {
                try { notify(); }
                catch { /* A failed error notification must never produce another error dialog. */ }
                finally { Interlocked.Exchange(ref _reporting, 0); }
            });
        }
        catch { /* The application can be shutting down; logging is best effort. */ }
        finally { if (!queued) Interlocked.Exchange(ref _reporting, 0); }
    }
}
