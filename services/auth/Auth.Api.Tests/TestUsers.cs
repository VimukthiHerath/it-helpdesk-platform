using Auth.Api.Data;
using Auth.Api.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Api.Tests;

// Since SCRUM-13 (password reset and token revocation) a bearer token is only accepted
// when it carries a SecurityStamp claim that matches an active user in the database.
// Tests that make their own tokens must therefore make sure that user exists first.
internal static class TestUsers
{
    public const string Stamp = "test-security-stamp";

    private static readonly string PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!");

    // Creates the user if it is missing, or makes sure it is active and carries the test
    // stamp, so a token made with the same id and stamp passes the revocation check.
    public static async Task EnsureAsync(IServiceProvider services, int userId, UserRole role)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            db.Users.Add(new User
            {
                Id = userId,
                Name = $"Test User {userId}",
                Email = $"test-user-{userId}@example.com",
                Password = PasswordHash,
                Role = role,
                IsActive = true,
                SecurityStamp = Stamp
            });
        }
        else
        {
            user.IsActive = true;
            user.SecurityStamp = Stamp;
        }

        await db.SaveChangesAsync();
    }
}
