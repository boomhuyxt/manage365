using System.Text.Json;

namespace manage365.Routes.API.Notifications;

public sealed record NotificationDto(
    long Id, string Type, string Title, string Message, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc, JsonElement? Data);

public sealed record PagedNotificationsResponse(
    IReadOnlyList<NotificationDto> Items, int Page, int PageSize, int TotalCount, int UnreadCount);

public sealed record UnreadNotificationCountResponse(int UnreadCount);
