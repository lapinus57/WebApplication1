using Client.Fundus;
using Client.Helpers;
using EyeChat.Fundus;

internal static class NativeChecks
{
    public static void Run(Action<bool, string> check, DateTime day)
    {
        // Simulate the reported WinUI error re-entering during logging and notification.
        var reporter = new UnhandledUiErrorReporter();
        int logs = 0, handled = 0, notices = 0, nested = 0;
        Action? pending = null;
        void Nested() => reporter.Report(() => nested++, () => nested++, _ => { nested++; return true; }, () => nested++);
        reporter.Report(() => { logs++; Nested(); }, () => handled++, callback => { pending = callback; return true; }, () => { notices++; Nested(); });
        check(logs == 1 && handled == 1 && notices == 0 && nested == 0, "Original error logged once; no synchronous UI notification or recursive handling");
        Nested();
        check(nested == 0, "A pending error notification suppresses duplicate reporting");
        pending!();
        check(notices == 1 && nested == 0, "Modal notification re-entry cannot recurse");
        reporter.Report(() => logs++, () => handled++, _ => false, () => notices++);
        check(logs == 2 && handled == 2 && notices == 1, "Unavailable dispatcher still records the original error");
        reporter.Report(() => throw new IOException(), () => handled++, _ => throw new InvalidOperationException(), () => notices++);
        reporter.Report(() => logs++, () => handled++, cb => { pending = cb; return true; }, () => throw new InvalidOperationException());
        pending!();
        reporter.Report(() => logs++, () => handled++, _ => false, () => notices++);
        check(logs == 4 && handled == 5, "Logging, dispatch and notification failures do not strand the guard");

        var lifetime = new FoPageLifetime();
        check(lifetime.TryStart() == null, "No query before the page is loaded");
        check(lifetime.Open() && !lifetime.Open(), "Repeated Loaded notifications initialize only once");
        var old = lifetime.TryStart()!;
        var oldToken = old.Token;
        check(old.IsCurrent && lifetime.TryStart() == null, "Double click or re-entry cannot start a duplicate load");
        lifetime.Close();
        check(oldToken.IsCancellationRequested && !old.IsCurrent, "Navigation away cancels the query and rejects its result");
        check(lifetime.Open(), "Page can be opened again");
        using var fresh = lifetime.TryStart()!;
        old.Dispose();
        check(fresh.IsCurrent && lifetime.TryStart() == null, "Late completion from an old page cannot release the current query");
        fresh.Dispose();
        using var retry = lifetime.TryStart();
        check(retry != null, "Explicit retry is available after completion or error");

        var data = new FoDataSet("sourceA", new[]
        {
            new FoPatientData("2", "Zoé TEST", day.AddHours(4), new[] { new FoConsultation("20", day.AddDays(-2), "FO à prévoir") }),
            new FoPatientData("1", "Émile TEST", day.AddHours(1), new[] { new FoConsultation("10", day.AddDays(-2), "", "FO prochaine fois ?") }),
            new FoPatientData("3", "Patient SANS CONSIGNE", day.AddHours(2), new[] { new FoConsultation("30", day.AddDays(-2), "RAS") }),
            new FoPatientData("4", "Patient REALISE", day.AddHours(3), new[] { new FoConsultation("40", day.AddDays(-5), "FO à prévoir"), new FoConsultation("41", day.AddDays(-1), "FO réalisé") })
        });
        var rows = FoPatientRow.Analyze(data, day, default);
        check(rows.Select(r => r.PatientId).SequenceEqual(new[] { "1", "4", "2" }), "Native list includes clinical candidates in appointment order");
        check(FoPatientRow.Filter(rows, 0, "").Select(r => r.PatientId).SequenceEqual(new[] { "1", "2" }), "Default native list separates completed FO");
        check(FoPatientRow.Filter(rows, 3, "").Single().PatientId == "4", "Completed FO remains available");
        check(FoPatientRow.Filter(rows, 4, "emile").Single().PatientId == "1", "Native patient search ignores accents");
        var row = rows[0];
        var review = new FoReview(row.Analysis.EvidenceHash, FoStatus.Planned, "test", day);
        check(row.WithReview(review).Status == FoStatus.Planned, "Matching review overrides automatic status");
        check(row.WithReview(review with { EvidenceHash = "outdated" }).Review == null, "Changed consultation invalidates local review");
        check(row.WithReview(review with { Status = (FoStatus)99 }).Review == null, "Invalid stored status is rejected");
        check(row.ReviewKey("sourceA") != row.ReviewKey("sourceB"), "Local reviews are isolated by Access source");
        check((row with { Appointment = day.AddDays(3) }).WithReview(review).Review == review, "Rescheduled appointment preserves unchanged clinical review");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { FoPatientRow.Analyze(data, day, cancelled.Token); check(false, "Cancelled analysis must stop"); }
        catch (OperationCanceledException) { check(true, "Cancelled analysis stops before displaying partial results"); }
    }
}
