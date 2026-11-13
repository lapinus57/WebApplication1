using Microsoft.AspNetCore.SignalR;

namespace ChatServeur
{
    /// <summary>
    /// Le hub garde en mémoire, dans des dictionnaires partagés, qui est connecté, sur quel poste
    /// et dans quels groupes. SignalR traite les appels de plusieurs postes en parallèle, et ces
    /// dictionnaires ne le supportent pas : connexions simultanées, utilisateurs « fantômes »,
    /// exceptions aléatoires. Ce filtre fait passer un par un les appels qui touchent cet état.
    /// Les lectures qui ne vont qu'en base restent parallèles. Une méthode qui n'est pas listée
    /// ici est protégée par défaut.
    /// </summary>
    public sealed class ChatHubStateFilter : IHubFilter
    {
        internal static readonly SemaphoreSlim Gate = new(1, 1);

        private static readonly HashSet<string> DatabaseOnlyMethods = new(StringComparer.Ordinal)
        {
            nameof(ChatHub.GetIdleRoomTimeoutSeconds),
            nameof(ChatHub.GetTodayMessages),
            nameof(ChatHub.GetMessagesForDate),
            nameof(ChatHub.GetAvailableAvatars),
            nameof(ChatHub.GetUserSettings),
            nameof(ChatHub.GetMissingUserSettings),
            nameof(ChatHub.GetExamOptions),
            nameof(ChatHub.GetRooms),
            nameof(ChatHub.GetReminder),
            nameof(ChatHub.GetAppointmentSearchConfig),
            nameof(ChatHub.GetPatients),
            nameof(ChatHub.GetArchivedPatients),
            nameof(ChatHub.GetPatientLogs),
        };

        internal static bool IsDatabaseOnly(string methodName) => DatabaseOnlyMethods.Contains(methodName);

        public async ValueTask<object?> InvokeMethodAsync(
            HubInvocationContext invocationContext,
            Func<HubInvocationContext, ValueTask<object?>> next)
        {
            if (invocationContext.Hub is not ChatHub || IsDatabaseOnly(invocationContext.HubMethodName))
                return await next(invocationContext);

            await Gate.WaitAsync();
            try
            {
                return await next(invocationContext);
            }
            finally
            {
                Gate.Release();
            }
        }

        public async Task OnDisconnectedAsync(
            HubLifetimeContext context,
            Exception? exception,
            Func<HubLifetimeContext, Exception?, Task> next)
        {
            if (context.Hub is not ChatHub)
            {
                await next(context, exception);
                return;
            }

            await Gate.WaitAsync();
            try
            {
                await next(context, exception);
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
