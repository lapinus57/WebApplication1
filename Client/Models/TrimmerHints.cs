using System.Diagnostics.CodeAnalysis;

namespace Client.Models
{
    public static class TrimmerHints
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UserInfo))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatMessageModel))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Patient))]
        public static void PreserveForSignalR()
        {
        }
    }
}
