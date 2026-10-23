using System.Security.Claims;
using manage365.Repositories.Notifications;
using manage365.Routes.API;
using manage365.Routes.API.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace manage365.Tests;

public sealed class NotificationsControllerTests
{
    [Fact]
    public async Task Get_UsesEmployeeIdFromAuthenticatedSubject()
    {
        var repository = new FakeNotificationRepository();
        var controller = CreateController(repository, "101");

        await controller.Get(false, 1, 500, TestContext.Current.CancellationToken);

        Assert.Equal(101, repository.LastEmployeeId);
        Assert.Equal(100, repository.LastPageSize);
    }

    [Fact]
    public async Task MarkRead_ReturnsNotFoundWhenNotificationDoesNotBelongToCurrentEmployee()
    {
        var repository = new FakeNotificationRepository { MarkReadResult = false };
        var controller = CreateController(repository, "101");

        var result = await controller.MarkRead(22, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(101, repository.LastEmployeeId);
        Assert.Equal(22, repository.LastNotificationId);
    }

    private static NotificationsController CreateController(FakeNotificationRepository repository, string subject)
    {
        var controller = new NotificationsController(repository);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "test"))
            }
        };
        return controller;
    }

    private sealed class FakeNotificationRepository : INotificationRepository
    {
        public long LastEmployeeId { get; private set; }
        public long LastNotificationId { get; private set; }
        public int LastPageSize { get; private set; }
        public bool MarkReadResult { get; set; } = true;

        public Task EnsureTablesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PagedNotificationsResponse> GetAsync(long employeeId, bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            LastEmployeeId = employeeId;
            LastPageSize = pageSize;
            return Task.FromResult(new PagedNotificationsResponse([], page, pageSize, 0, 0));
        }
        public Task<int> GetUnreadCountAsync(long employeeId, CancellationToken cancellationToken = default)
        {
            LastEmployeeId = employeeId;
            return Task.FromResult(0);
        }
        public Task<bool> MarkReadAsync(long employeeId, long notificationId, DateTimeOffset readAtUtc, CancellationToken cancellationToken = default)
        {
            LastEmployeeId = employeeId;
            LastNotificationId = notificationId;
            return Task.FromResult(MarkReadResult);
        }
        public Task<int> MarkAllReadAsync(long employeeId, DateTimeOffset readAtUtc, CancellationToken cancellationToken = default)
        {
            LastEmployeeId = employeeId;
            return Task.FromResult(0);
        }
    }
}
