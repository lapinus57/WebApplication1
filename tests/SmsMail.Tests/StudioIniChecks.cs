using Client.SmsMail.Services;

static class StudioIniChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var ini = "[Options]\r\nSystemDB=c:\\Studiov2000\\config\\system.mdw\r\n[StudioV]\r\nUserCurrent=sec\r\nNumPat=1\r\n[MAJ]\r\nPremierDémarrage=1\r\n[Echange]\r\nNumPat=39475\r\nNumConsult=411156\r\n";
        Check(StudioIni.OpenPatient(ini) == 39475, "NumPat read from the [Echange] section only");
        Check(StudioIni.OpenPatient("[Echange]\nNumConsult=1\n") == null && StudioIni.OpenPatient("[Echange]\nNumPat=\n") == null && StudioIni.OpenPatient("") == null,
            "No patient: nothing to open");
        Check(StudioIni.OpenConsultation(ini) == 411156, "NumConsult read");
        Check(StudioIni.NewConsultation(411157, 411156) == (true, 411157), "Higher consultation number: a consultation was created");
        Check(StudioIni.NewConsultation(400000, 411156) == (false, 411156), "Older dossier reopened: no new consultation");
        Check(StudioIni.NewConsultation(411156, 0) == (false, 411156), "First number seen on this post: remembered, not taken as new");
        var path = Path.Combine(Path.GetTempPath(), "studiov-" + Guid.NewGuid().ToString("N") + ".ini");
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes(ini));
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            Check(StudioIni.OpenPatient(StudioIni.Read(path)) == 39475, "Read while StudioV keeps the file open");
        File.Delete(path);
        return count;
    }
}
