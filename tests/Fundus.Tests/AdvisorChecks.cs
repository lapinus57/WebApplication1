using System.Net;
using System.Text;
using System.Text.Json;
using EyeChat.Fundus;

// Local AI opinion: synthetic consultations, mocked Ollama responses, no model call in CI.
internal static class AdvisorChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var day = new DateTime(2031, 5, 12, 9, 0, 0);
        var visits = Enumerable.Range(0, 9).Select(i => new FoConsultation("c" + i, day.AddDays(-900 + i * 100), "RAS " + i)).ToList();
        visits[0] = visits[0] with { Text = "Prévoir FO" };
        visits[8] = visits[8] with { Text = "=> REVOIR DANS 1 AN\nFAIRE FO DE CTRL", RefractionText = "OD= -1,00= 10 /10\nOG= -1,25= 9 /10" };
        var detection = FundusAnalysis.Analyze(new[] { visits[0] }, day);
        var selected = OllamaFundusAdvisor.Select(visits, detection);
        check(selected.Count == OllamaFundusAdvisor.MaxConsultations + 1 && selected[0].Id == "c0" && selected[^1].Id == "c8", "Latest consultations plus the older detection source");
        check(OllamaFundusAdvisor.Select(visits, null).Count == OllamaFundusAdvisor.MaxConsultations, "At most the latest consultations without a source");

        var prompt = OllamaFundusAdvisor.Prompt(selected, day.AddDays(3), detection, new[] { new FoAdviceAnswer("Le FO a-t-il été fait ailleurs ?", "Non") });
        check(prompt.Contains("15/05/2031") && prompt.Contains("FAIRE FO DE CTRL") && prompt.Contains("FO à prévoir"), "Prompt carries appointment, texts and automatic detection");
        check(prompt.Contains("Le FO a-t-il été fait ailleurs ? → Non"), "Answers of the cabinet are sent back to the model");
        var longText = OllamaFundusAdvisor.Prompt(new[] { visits[1] with { Text = new string('a', 5000) } }, day, null, Array.Empty<FoAdviceAnswer>());
        check(longText.Contains("[…]") && longText.Length < 3000, "Long fields are clipped");

        string Answer(string decision, string date, string quote, string reasons = "[\"CONSIGNE_FO\"]") =>
            $$"""{"Decision":"{{decision}}","Explanation":"Consigne de contrôle à un an.","Evidence":[{"Date":"{{date}}","Quote":"{{quote}}"}],"Reasons":{{reasons}},"PreviousFoReason":"","Questions":["Le FO a-t-il été fait ailleurs ?"]}""";
        var lastDate = visits[8].Date.ToString("dd/MM/yyyy");
        var advice = OllamaFundusAdvisor.Parse(Answer("OUI", lastDate, "faire fo de ctrl"), selected);
        check(advice.Decision == FoAdviceDecision.Yes && advice.Evidence.Single().Found && advice.Reasons.Single() == "Consigne de FO dans le dossier" && advice.Questions.Count == 1, "Grounded quote, reason label and question");
        check(!OllamaFundusAdvisor.Parse(Answer("OUI", lastDate, "FO réalisé le 3 mars"), selected).Evidence.Single().Found, "Invented quote is flagged");
        check(!OllamaFundusAdvisor.Parse(Answer("OUI", "01/01/2020", "FAIRE FO DE CTRL"), selected).Evidence.Single().Found, "Quote from another date is flagged");
        foreach (var invalid in new[] { Answer("PEUT-ETRE", lastDate, "x"), Answer("OUI", lastDate, "x", "[\"PRESCRIRE\"]"), "{}", "pas du json" })
        {
            try { OllamaFundusAdvisor.Parse(invalid, selected); check(false, "Invalid AI answer rejected"); }
            catch (FoDataException) { check(true, "Invalid AI answer rejected"); }
        }

        string Envelope(string content, bool done = true) => JsonSerializer.Serialize(new { done, message = new { content } });
        var handler = new FakeHandler(async (request, token) =>
        {
            check(request.RequestUri!.OriginalString == "http://127.0.0.1:11434/api/chat", "Advisor only calls the loopback Ollama endpoint");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            check(body.RootElement.GetProperty("model").GetString() == "qwen2.5:7b" && !body.RootElement.GetProperty("stream").GetBoolean(), "Advisor uses the pinned local model");
            var user = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            check(!user.Contains("Patient TEST") && !user.Contains("P-0001"), "No patient name or identifier is sent");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Envelope(Answer("INCERTAIN", lastDate, "REVOIR DANS 1 AN")), Encoding.UTF8, "application/json") };
        });
        using (var advisor = new OllamaFundusAdvisor(handler))
        {
            var result = await advisor.AskAsync(visits, day.AddDays(3), detection, Array.Empty<FoAdviceAnswer>(), default);
            check(result.Decision == FoAdviceDecision.Uncertain && result.Evidence.Single().Found, "HTTP round trip parses the opinion");
        }
        foreach (var response in new[] { new HttpResponseMessage(HttpStatusCode.NotFound), new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Envelope("{}", false)) } })
        {
            using var advisor = new OllamaFundusAdvisor(new FakeHandler((_, _) => Task.FromResult(response)));
            try { await advisor.AskAsync(visits, day, detection, Array.Empty<FoAdviceAnswer>(), default); check(false, "Transport error"); }
            catch (FoDataException) { check(true, "Transport error gives a readable message"); }
        }
        using (var advisor = new OllamaFundusAdvisor(new FakeHandler((_, _) => throw new HttpRequestException())))
        {
            try { await advisor.AskAsync(visits, day, detection, Array.Empty<FoAdviceAnswer>(), default); check(false, "Ollama stopped"); }
            catch (FoDataException ex) { check(ex.Message.Contains("Lancez l’application Ollama"), "Stopped Ollama is explained"); }
        }
        using (var advisor = new OllamaFundusAdvisor(new FakeHandler((_, _) => throw new InvalidOperationException())))
        {
            try { await advisor.AskAsync(Array.Empty<FoConsultation>(), day, null, Array.Empty<FoAdviceAnswer>(), default); check(false, "Empty timeline"); }
            catch (FoDataException) { check(true, "Empty timeline is not sent"); }
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
