using System.Security.Claims;
using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class IdentityIntegrationTests
{
    [Fact]
    public async Task Identity_users_roles_claims_logins_and_tokens_persist()
    {
        var directory = Path.Combine(Path.GetTempPath(), "iotsharp-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var root = new LiteDBDatabaseRoot();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<IdentityContext>(options => options.UseLiteDB(Path.Combine(directory, "identity.db"), root));
            services.AddIdentityCore<IdentityUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<IdentityContext>();
            using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var user = new IdentityUser("alice") { Email = "alice@example.test" };
                Assert.True((await users.CreateAsync(user, "Valid-password-123!")).Succeeded);
                Assert.True((await roles.CreateAsync(new IdentityRole("Reader"))).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, "Reader")).Succeeded);
                Assert.True((await users.AddClaimAsync(user, new Claim("permission", "read"))).Succeeded);
                Assert.True((await users.AddLoginAsync(user, new UserLoginInfo("test", "alice-key", "Test"))).Succeeded);
                Assert.True((await users.SetAuthenticationTokenAsync(user, "test", "token", "value")).Succeeded);
            }
            using (var scope = provider.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var user = (await users.FindByNameAsync("ALICE"))!;
                Assert.NotNull(user);
                Assert.True(await users.CheckPasswordAsync(user, "Valid-password-123!"));
                Assert.True(await users.IsInRoleAsync(user, "Reader"));
                Assert.Equal("read", Assert.Single(await users.GetClaimsAsync(user)).Value);
                Assert.Equal("alice-key", Assert.Single(await users.GetLoginsAsync(user)).ProviderKey);
                Assert.Equal("value", await users.GetAuthenticationTokenAsync(user, "test", "token"));
                Assert.Single(await users.GetUsersInRoleAsync("Reader"));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class IdentityContext(DbContextOptions<IdentityContext> options) : IdentityDbContext<IdentityUser>(options);
}
