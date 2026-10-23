using manage365.Routes.API.Notifications;

namespace manage365.Repositories.Notifications;

public interface INotificationRepository
{
    Task EnsureTablesAsync(CancellationToken cancellationToken = default);
    Task<PagedNotificationsResponse> GetAsync(long employeeId, bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(long employeeId, CancellationToken cancellationToken = default);
    Task<bool> MarkReadAsync(long employeeId, long notificationId, DateTimeOffset readAtUtc, CancellationToken cancellationToken = default);
    Task<int> MarkAllReadAsync(long employeeId, DateTimeOffset readAtUtc, CancellationToken cancellationToken = default);
}
