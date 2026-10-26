using System.Net;
using System.Text;
using System.Text.Json;
using EyeChat.Fundus;

internal static class LocalRulesChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var now = new DateTime(2035, 8, 12);
        var valid = new LocalRuleProposal { NeedsClarification = false, Question = "", Condition = "MYOPIA", Exam = "FO_OCT", SphereLessThan = -5, IntervalMonths = 60 };
        var json = JsonSerializer.Serialize(valid);
        void Reject(Action action, string name) { try { action(); check(false, name); } catch (FoDataException) { check(true, name); } }
        var proposal = LocalExamRules.ParseProposal(json);
        check(proposal.SphereLessThan == -5 && proposal.IntervalMonths == 60, "LLM proposal retains the signed threshold and converted interval");
        var question = LocalExamRules.ParseProposal("{\"NeedsClarification\":true,\"Question\":\"Quel délai ?\",\"Condition\":null,\"Exam\":null,\"SphereLessThan\":null,\"IntervalMonths\":null}");
        check(question.NeedsClarification && question.IntervalMonths == null, "Clarification never invents a missing interval");
        Reject(() => LocalExamRules.ParseProposal("{}"), "Required proposal fields cannot silently default");
        Reject(() => LocalExamRules.ParseProposal(json.Replace("60", "null")), "Incomplete ready proposal rejected");
        Reject(() => LocalExamRules.ParseProposal(json.Replace("MYOPIA", "SQL")), "Executable or unsupported criteria rejected");
        Reject(() => LocalExamRules.ParseProposal(json.Replace("-5", "5")), "Positive myopia threshold is not silently inverted");
        Reject(() => LocalExamRules.ParseProposal(json.Replace("60", "0")), "Zero interval rejected");
        Reject(() => LocalExamRules.ParseProposal(json.Replace("60", "121")), "Out-of-range interval rejected");
        Reject(() => LocalExamRules.ParseProposal(json[..^1] + ",\"Script\":\"delete\"}"), "Unknown generated properties cannot execute or enter a rule");
        var rule = new LocalExamRule(Guid.NewGuid(), "MYOPIA", "FO_OCT", -5, 60, DateTime.UtcNow, true);
        var package = new LocalRulePackage { Rules = new() { rule } };
        var exported = LocalExamRules.Export(package);
        var imported = LocalExamRules.Import(exported);
        check(imported.Rules.Single().Enabled == false && imported.Rules.Single().Id != rule.Id, "Imported rules require a new human activation");
        check(!exported.Contains("Instruction") && !exported.Contains("Question"), "Export omits free instructions and model conversations");
        check(LocalExamRules.Import(exported.Replace("false", "true")).Rules.All(r => !r.Enabled), "Even a file claiming activation is imported inactive");
        Reject(() => LocalExamRules.Import(new string('a', 64001)), "Bounded rule file");
        Reject(() => LocalExamRules.ValidatePackage(new() { Rules = new() { rule, rule } }), "Duplicate identifiers rejected");
        Reject(() => LocalExamRules.Validate(rule with { Condition = "HTO" }), "Unused myopia thresholds cannot sneak into another criterion");
        var oldFo = new FoConsultation("synthetic-old", now.AddYears(-5), "FO réalisé");
        var latest = new FoConsultation("synthetic-latest", now.AddDays(-1), "", "Myopie -5,50");
        ExamPrediction[] Unknown() => Enumerable.Range(0, 4).Select(_ => new ExamPrediction(null, null, 0, 0, "", Array.Empty<string>())).ToArray();
        var result = LocalExamRules.Apply(package, new[] { oldFo, latest }, now, now, Unknown());
        check(result[0].Decision == true && result[1].Decision == true && result[0].Explanation.Contains(rule.Id.ToString("N")[..8]), "Only active validated rules produce traceable FO/OCT preparation at the due date");
        check(LocalExamRules.Apply(new() { Rules = new() { rule with { Enabled = false } } }, new[] { oldFo, latest }, now, now, Unknown())[0].Decision == null, "Inactive proposals have no effect");
        check(LocalExamRules.Apply(package, new[] { latest }, now, now, Unknown())[0].Decision == null, "Missing examination date requires confirmation");
        check(LocalExamRules.Apply(package, new[] { oldFo, latest }, now, now.AddDays(-1), Unknown())[0].Decision == null, "No premature interval classification");
        var existingNo = Unknown(); existingNo[0] = existingNo[1] = new(false, false, 20, 1, "Autre analyse", Array.Empty<string>());
        check(LocalExamRules.Apply(package, new[] { oldFo, latest }, now, now.AddDays(-1), existingNo)[0].Decision == false, "A known not-yet-due rule does not erase an existing supported No");
        check(!LocalExamRules.ExplicitMyopia("Myopie -5,00", -5) && LocalExamRules.ExplicitMyopia("Myopie -5,25", -5), "Strict signed threshold and decimal comma");
        foreach (var text in new[] { "Cylindre -6", "Myopie -6 ?", "Antécédent familial myopie -6", "Pas de myopie -6" })
            check(!LocalExamRules.ExplicitMyopia(text, -5), "No ungrounded numeric criterion: " + text);
        var cancelled = latest with { Text = "Pas de FO prévu" };
        check(LocalExamRules.Apply(package, new[] { oldFo, cancelled }, now, now, Unknown())[0].Decision == null, "Explicit cancellation prevents rule-based automatic preparation");
        var v3 = package.Rules.Single() with { Exam = "V3F" };
        check(LocalExamRules.Apply(new() { Rules = new() { v3 } }, new[] { oldFo, latest }, now, now, Unknown())[2].Decision == null, "FO date never masquerades as the date of another examination");
        foreach (var pair in new[] { (Condition: "DIABETES", Text: "Diabète"), (Condition: "HTO", Text: "HTO") })
        {
            var chronic = new LocalRulePackage { Rules = new() { rule with { Condition = pair.Condition, SphereLessThan = null, IntervalMonths = 12 } } };
            check(LocalExamRules.Apply(chronic, new[] { oldFo, latest with { Text = pair.Text, RefractionText = "" } }, now, now, Unknown())[0].Decision == true, "Grounded chronic condition supports its validated rule");
            check(LocalExamRules.Apply(chronic, new[] { oldFo, latest with { Text = "HTA", RefractionText = "" } }, now, now, Unknown())[0].Decision == null, "Arterial hypertension is not inferred as HTO or diabetes");
        }

        string Envelope(string content, bool done = true) => JsonSerializer.Serialize(new { done, message = new { content } });
        var handler = new FakeHandler(async (request, token) =>
        {
            check(request.RequestUri!.OriginalString == "http://127.0.0.1:11434/api/chat", "Only the fixed loopback Ollama endpoint is called");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            check(body.RootElement.GetProperty("model").GetString() == "qwen2.5:7b" && !body.RootElement.GetProperty("stream").GetBoolean(), "Pinned local model and bounded non-streaming request");
            check(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "Consigne synthétique", "Only the user-entered instruction is sent, without a patient dossier");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Envelope(json), Encoding.UTF8, "application/json") };
        });
        using (var service = new OllamaRuleInterpreter(handler))
            check((await service.InterpretAsync("Consigne synthétique", default)).IntervalMonths == 60, "Real HTTP integration parses a mocked model response without activating anything");
        foreach (var response in new[]
        {
            new HttpResponseMessage(HttpStatusCode.NotFound),
            new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://example.com") } },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Envelope(json, false)) },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 64001)) }
        })
        {
            using var service = new OllamaRuleInterpreter(new FakeHandler((_, _) => Task.FromResult(response)));
            try { await service.InterpretAsync("Consigne synthétique", default); check(false, "Invalid transport response rejected"); }
            catch (FoDataException) { check(true, "Invalid transport response rejected without a generated rule"); }
        }
        using var cancel = new CancellationTokenSource();
        using var cancellable = new OllamaRuleInterpreter(new FakeHandler(async (_, token) => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(); }));
        try { await cancellable.InterpretAsync("Consigne synthétique", cancel.Token); check(false, "Cancellation"); }
        catch (OperationCanceledException) { check(true, "Cancellation reaches the HTTP inference call"); }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
