using EyeChat.Fundus;

internal static class MedicalTextChecks
{
    public static void Run(Action<bool, string> check, DateTime day)
    {
        void Equal(string source, string expected, string name)
        {
            var actual = FoMedicalText.Read(source);
            check(actual == expected, $"{name}: expected [{expected}], actual [{actual}]");
        }
        void Invalid(object source, string name)
        {
            try { FoMedicalText.Read(source); check(false, name); }
            catch (FoDataException ex)
            {
                check(!ex.Message.Contains("PATIENT_CONFIDENTIEL") && ex.InnerException == null, name);
            }
        }
        FoAnalysis? Analyze(string observation, string refraction = "") => FundusAnalysis.Analyze(
            new[] { new FoConsultation("1", day.AddDays(-1), FoMedicalText.Read(observation), FoMedicalText.Read(refraction)) }, day);

        check(FoMedicalText.Read(null) == "" && FoMedicalText.Read(DBNull.Value) == "", "Empty database fields");
        Equal("FO à prévoir\r\nPIO < 20 mmHg ; AV > 8/10", "FO à prévoir\r\nPIO < 20 mmHg ; AV > 8/10", "Plain medical text unchanged");
        Equal("Patient TEST & famille", "Patient TEST & famille", "Plain ampersand unchanged");
        Equal("&lt;p&gt;FO&lt;/p&gt;", "&lt;p&gt;FO&lt;/p&gt;", "Literal encoded markup is not recursively interpreted");
        Equal("<div><b>FO</b>&nbsp;à pr&eacute;voir<br>Contr&ocirc;le : fond d&rsquo;&oelig;il</div>", "FO à prévoir\nContrôle : fond d’œil", "HTML accents, entities and breaks");
        Equal("<p>F<strong>O</strong> à prévoir</p>", "FO à prévoir", "Inline formatting cannot split a word");
        Equal("<p>Pas de <b>FO</b> prévu</p>", "Pas de FO prévu", "Inline formatting preserves negation");
        Equal("<p>FO à prévoir</p><p>FO non nécessaire</p>", "FO à prévoir\nFO non nécessaire", "Separate paragraphs stay separate");
        Equal("<ul><li>FO à prévoir</li><li>Contrôle annuel</li></ul>", "FO à prévoir\nContrôle annuel", "List items are readable");
        Equal("<table><tr><td>FO</td><td>à prévoir</td></tr><tr><td>PIO</td><td>18</td></tr></table>", "FO à prévoir\nPIO 18", "Table cells and rows do not concatenate");
        Equal("<pre>FO à prévoir\nFO non nécessaire</pre>", "FO à prévoir\nFO non nécessaire", "Preformatted lines preserved");
        Equal("<div>Pas de\n  <strong>FO</strong> prévu</div>", "Pas de FO prévu", "HTML source indentation is not a clinical paragraph");
        Equal("<HTML><BODY><FONT color='red'>FO À PRÉVOIR</FONT></BODY></HTML>", "FO À PRÉVOIR", "Access-style HTML and uppercase tags");
        Equal("<div>FO à prévoir", "FO à prévoir", "HTML fragment with omitted end tag");
        Equal("<p>&lt;script&gt;FO&lt;/script&gt;</p>", "<script>FO</script>", "Decoded literals remain plain text");
        Equal("<html><head><title>FO à prévoir</title><style>.x { color:red; }</style></head><body><!-- FO à prévoir --><script>FO à prévoir</script><p>RAS</p></body></html>", "RAS", "Scripts, styles, metadata and comments are not clinical text");
        Equal("<p title='FO à prévoir'>RAS</p><img src='https://example.invalid/patient' alt='FO à prévoir'><iframe src='file:///patient'>FO à prévoir</iframe>", "RAS", "External resources and attributes are never loaded or analyzed");
        Equal("<!-- FO à prévoir -->", "", "Comment-only field is HTML, not a clinical instruction");
        Equal("<template>FO à prévoir</template>", "", "Template-only fragment is not clinical text");
        Equal("<input value='FO à prévoir'><svg><text>FO à prévoir</text></svg>", "", "Non-text HTML fragment is not analyzed as raw markup");
        Equal("<p hidden>FO à prévoir</p><span style='display: none !important'>FO à prévoir</span><del>FO à prévoir</del><p>RAS</p>", "RAS", "Hidden and deleted instructions excluded");
        Equal("<span style='visibility:hidden'>FO à prévoir</span><span style='text-decoration: line-through'>FO à prévoir</span>RAS", "RAS", "Hidden and struck-through style excluded");

        Equal(@"{\rtf1\ansi FO prochaine fois\par Contr\'f4le annuel}", "FO prochaine fois\nContrôle annuel", "RTF paragraph and ANSI accents");
        Equal(@"{\rtf1\ansi\ansicpg1252 Pr\'e9voir un fond d'\'9cil}", "Prévoir un fond d'œil", "RTF Windows-1252 ligature");
        Equal(@"{\rtf1\ansi\uc1 FO \u224? pr\u233?voir}", "FO à prévoir", "RTF Unicode and fallback skipping");
        Equal(@"{\rtf1\ansi\uc0 Fond d'\u339il \u224 pr\u233voir}", "Fond d'œil à prévoir", "RTF Unicode with no fallback");
        Equal(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}{\colortbl;\red255\green0\blue0;}\f0\cf1 FO {\b prochaine} fois}", "FO prochaine fois", "RTF font/color tables and nested formatting");
        Equal(@"{\rtf1\ansi Pas de {\b FO} pr\'e9vu}", "Pas de FO prévu", "RTF formatting preserves negation");
        Equal(@"{\rtf1\ansi FO prochaine fois\line Contr\'f4le\tab annuel}", "FO prochaine fois\nContrôle annuel", "RTF line and tab");
        Equal(@"{\rtf1\ansi Texte \{test\} C:\\dossier}", @"Texte {test} C:\dossier", "Escaped RTF braces and backslash");
        Equal(@"{\rtf1\ansi{\*\info FO prochaine fois}RAS}", "RAS", "RTF non-text destination excluded");
        Equal(@"{\rtf1\ansi{\field{\*\fldinst HYPERLINK ""https://example.invalid/patient""}{\fldrslt FO prochaine fois}}}", "FO prochaine fois", "RTF hyperlink uses readable result only");
        Equal("\uFEFF  {\\rtf1\\ansi FO prochaine fois}", "FO prochaine fois", "RTF leading BOM and whitespace");
        Equal(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\par}", "", "Empty formatted clinical field");
        Equal(@"{\rtf1\ansi{\strike FO prochaine fois}RAS}", "RAS", "RTF struck-through instruction excluded");
        Equal(@"{\rtf1\ansi{\v FO prochaine fois}RAS}", "RAS", "RTF hidden instruction excluded");

        check(Analyze("<p>Prévoir <b>FO</b> au prochain contrôle</p>")?.Status == FoStatus.Planned, "HTML observation enters FO analysis");
        check(Analyze("", @"{\rtf1\ansi FO prochaine fois}")?.Status == FoStatus.Planned, "RTF REFRACTION enters FO analysis");
        check(Analyze("<p>Pas de <b>FO</b> prévu</p>") == null, "No false positive from formatted HTML negation");
        check(Analyze(@"{\rtf1\ansi Pas de {\b FO} pr\'e9vu}") == null, "No false positive from formatted RTF negation");
        check(Analyze("<p>FO à prévoir</p><p>FO non nécessaire</p>") == null, "HTML paragraph chronology preserved");
        check(Analyze(@"{\rtf1\ansi FO prochaine fois}", "<div>FO réalisé</div>")?.Status == FoStatus.Planned, "Mixed formats preserve same-visit future instruction");
        var plain = Analyze("FO prochaine fois");
        check(plain?.EvidenceHash == Analyze("<p>FO <strong>prochaine</strong> fois</p>")?.EvidenceHash, "Formatting alone does not invalidate identical clinical evidence");
        check(plain?.EvidenceHash != Analyze("<p>FO non nécessaire</p>")?.EvidenceHash, "Changed clinical content invalidates review");

        Invalid(123, "Non-text field rejected");
        Invalid(@"{\rtf1 PATIENT_CONFIDENTIEL", "Truncated RTF rejects without leaking its text");
        Invalid(@"{\rtf1 PATIENT_CONFIDENTIEL}trailing", "RTF trailing data cannot be silently ignored");
        Invalid(@"{\rtf1 \'zz PATIENT_CONFIDENTIEL}", "Malformed RTF hex rejected");
        Invalid(@"{\rtf1\bin999 PATIENT_CONFIDENTIEL}", "Truncated RTF binary rejected");
        Invalid(@"{\rtf1" + new string('{', 1000) + "PATIENT_CONFIDENTIEL" + new string('}', 1001), "RTF nesting bounded before parser");
        Invalid(string.Concat(Enumerable.Repeat("<div>", 1000)) + "PATIENT_CONFIDENTIEL" + string.Concat(Enumerable.Repeat("</div>", 1000)), "HTML nesting bounded before traversal");
        Invalid(new string('x', 1_000_001), "Raw field length bounded before parser");
        Invalid("<p>" + new string('x', 100_001) + "</p>", "Extracted text length bounded without truncation");
    }
}
