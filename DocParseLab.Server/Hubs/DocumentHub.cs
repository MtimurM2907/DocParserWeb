using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using DocParseLab.Server.Extensions;

namespace DocParseLab.Server.Hubs;

[Authorize]
public sealed class DocumentHub : Hub
{
    public static string DocumentGroup(int documentId) => $"doc:{documentId}";
    public static string UserGroup(int userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.TryGetUserId(out var userId) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        await base.OnConnectedAsync();
    }

    public Task JoinDocument(int documentId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, DocumentGroup(documentId));

    public Task LeaveDocument(int documentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, DocumentGroup(documentId));
}

public interface IDocumentRealtimeService
{
    Task PushNotificationAsync(int userId, object payload, CancellationToken cancellationToken = default);

    Task PushParseProgressAsync(int userId, object payload, CancellationToken cancellationToken = default);
}

public sealed class DocumentRealtimeService : IDocumentRealtimeService
{
    private readonly IHubContext<DocumentHub> _hub;

    public DocumentRealtimeService(IHubContext<DocumentHub> hub) => _hub = hub;

    public Task PushNotificationAsync(int userId, object payload, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group(DocumentHub.UserGroup(userId)).SendAsync("notification", payload, cancellationToken);

    public Task PushParseProgressAsync(int userId, object payload, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group(DocumentHub.UserGroup(userId)).SendAsync("parseProgress", payload, cancellationToken);
}
