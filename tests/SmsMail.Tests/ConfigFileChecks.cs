using Client.SmsMail.Configuration;
using Client.SmsMail.Services;

static class ConfigFileChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var settings = new SmsMailSettings { Cabinet = "Cabinet Fictif" };
        settings.Sms.ServiceName = "sms-ab123"; settings.Sms.ApplicationSecret = "SECRET-OVH"; settings.Sms.ConsumerKey = "CONSO";
        settings.Email.SmtpHost = "smtp.example.fr"; settings.Email.Password = "MOT-DE-PASSE";
        var file = SmsMailConfigFile.Export(settings, "un mot de passe long");
        var text = System.Text.Encoding.UTF8.GetString(file);
        Check(!text.Contains("SECRET-OVH") && !text.Contains("MOT-DE-PASSE") && !text.Contains("Cabinet Fictif") && text.Contains(SmsMailConfigFile.Format),
            "Config file is encrypted: no secret nor name in clear");
        var back = SmsMailConfigFile.Import(file, "un mot de passe long");
        Check(back.Cabinet == "Cabinet Fictif" && back.Sms.ApplicationSecret == "SECRET-OVH" && back.Email.Password == "MOT-DE-PASSE", "Config file round trip keeps every setting");
        try { SmsMailConfigFile.Import(file, "mauvais mot de passe"); Check(false, "Wrong password refused"); }
        catch (InvalidOperationException) { count++; }
        try { SmsMailConfigFile.Export(settings, "court"); Check(false, "Short password refused"); }
        catch (InvalidOperationException) { count++; }
        var diagnostic = DiagnosticPackage.Encrypt("{}", "un mot de passe long", PostItAnalysis.Format);
        try { SmsMailConfigFile.Import(diagnostic, "un mot de passe long"); Check(false, "Another EyeChat file refused"); }
        catch (InvalidDataException) { count++; }
        Check(SmsMailConfigFile.Describe(back).Contains("clés incluses") && SmsMailConfigFile.Describe(back).Contains("mot de passe inclus")
            && !SmsMailConfigFile.Describe(back).Contains("SECRET"), "Description without any secret");
        return count;
    }
}
