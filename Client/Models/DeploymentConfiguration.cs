using System.Collections.Generic;

namespace Client.Models
{
    /// <summary>
    /// Portable configuration prepared on one computer and imported on a new EyeChat installation.
    /// </summary>
    public sealed class DeploymentConfiguration
    {
        public int FormatVersion { get; set; } = 1;
        public List<UserInfo> Users { get; set; } = new();
        public Dictionary<string, string> UserSettings { get; set; } = new();
        public List<DeploymentWorkstation> Workstations { get; set; } = new();
        public List<ExamOption> Exams { get; set; } = new();
        public List<string> Rooms { get; set; } = new();
    }

    public sealed class DeploymentWorkstation
    {
        public string Name { get; set; } = string.Empty;
        public string ShiftF9Exam { get; set; } = string.Empty;
        public string CtrlF9Exam { get; set; } = string.Empty;
        public string ShiftF10Exam { get; set; } = string.Empty;
        public string CtrlF10Exam { get; set; } = string.Empty;
        public string ShiftF11Exam { get; set; } = string.Empty;
        public string CtrlF11Exam { get; set; } = string.Empty;
        public string ShiftF12Exam { get; set; } = string.Empty;
        public string CtrlF12Exam { get; set; } = string.Empty;
    }
}
