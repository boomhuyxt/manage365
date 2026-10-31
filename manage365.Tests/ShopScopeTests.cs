using System.Security.Claims;
using manage365.Repositories.Shifts;
using Xunit;

namespace manage365.Tests;

public sealed class ShopScopeTests
{
    private static ClaimsPrincipal Actor(string role, string id = "10") => new(new ClaimsIdentity(
        [new Claim("sub", id), new Claim(ClaimTypes.Role, role)], "test"));

    [Fact]
    public void Manager_NewAndLegacyAssignment_AutomaticallyUseOwnShop()
    {
        Assert.Equal(10, ShopScopeRepository.Resolve(Actor("Manager"), null, null));
        Assert.Equal(10, ShopScopeRepository.Resolve(Actor("Manager"), null, 10));
    }

    [Theory]
    [InlineData(20L, null)]
    [InlineData(null, 20L)]
    [InlineData(10L, 20L)]
    public void Manager_CannotForgeOrTakeAnotherShopsAssignment(long? requested, long? existing) =>
        Assert.Throws<InvalidOperationException>(() => ShopScopeRepository.Resolve(Actor("Manager"), requested, existing));

    [Fact]
    public void Admin_MustSelectShopForNewAssignment_ButPreservesExistingShop()
    {
        Assert.Throws<ArgumentException>(() => ShopScopeRepository.Resolve(Actor("Admin"), null, null));
        Assert.Equal(20, ShopScopeRepository.Resolve(Actor("Admin"), 20, null));
        Assert.Equal(20, ShopScopeRepository.Resolve(Actor("Admin"), null, 20));
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Employee")]
    public void OrdinaryAccount_CannotAssignShop(string role) =>
        Assert.Throws<InvalidOperationException>(() => ShopScopeRepository.Resolve(Actor(role), 10, null));

    [Fact]
    public void MissingActorId_IsRejected() =>
        Assert.Throws<ArgumentException>(() => ShopScopeRepository.Resolve(Actor("Manager", "invalid"), null, null));
}
