using System.Collections.Concurrent;

namespace manage365.Routes.API.Auth;

public interface IUserStore
{
    User? FindByEmail(string normalizedEmail);
    bool TryAdd(User user);
}

public sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<string, User> _users =
        new(StringComparer.OrdinalIgnoreCase);

    public User? FindByEmail(string normalizedEmail) =>
        _users.GetValueOrDefault(normalizedEmail);

    public bool TryAdd(User user) =>
        _users.TryAdd(NormalizeEmail(user.Email), user);

    public static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
