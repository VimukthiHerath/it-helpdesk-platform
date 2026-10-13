using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Auth.Api.Data;
using Auth.Api.Model;
using Auth.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Auth.Api.Tests
{
    // AUTH-3: the forgot-password endpoint hands the reset link to Notification.Api
    // (through the password-reset publisher) instead of logging it.
    public class PasswordResetEmailTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private sealed record Published(int UserId, string Email, string ResetLink, DateTime ExpiresAtUtc);

        private sealed class CapturingPublisher : IPasswordResetPublisher
        {
            public ConcurrentQueue<Published> Calls { get; } = new();

            public void Publish(int userId, string email, string resetLink, DateTime expiresAtUtc) =>
                Calls.Enqueue(new Published(userId, email, resetLink, expiresAtUtc));
        }

        private readonly WebApplicationFactory<Program> _factory;

        public PasswordResetEmailTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private (System.Net.Http.HttpClient Client, CapturingPublisher Publisher, IServiceProvider Services) CreateClient()
        {
            var publisher = new CapturingPublisher();
            var factory = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IPasswordResetPublisher>();
                    services.AddSingleton<IPasswordResetPublisher>(publisher);
                }));
            return (factory.CreateClient(), publisher, factory.Services);
        }

        private static async Task<User> AddUserAsync(IServiceProvider services, string email, bool isActive = true)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new User
            {
                Name = "Reset Test",
                Email = email,
                Password = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                Role = UserRole.Employee,
                IsActive = isActive,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        [Fact]
        public async Task ForgotPassword_ExistingUser_PublishesLinkWhoseTokenMatchesTheStoredHash()
        {
            var (client, publisher, services) = CreateClient();
            var email = $"reset-{Guid.NewGuid():N}@example.com";
            var user = await AddUserAsync(services, email);

            var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var call = Assert.Single(publisher.Calls);
            Assert.Equal(user.Id, call.UserId);
            Assert.Equal(email, call.Email);

            var token = HttpUtility.ParseQueryString(new Uri(call.ResetLink).Query)["token"];
            Assert.False(string.IsNullOrEmpty(token));

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id);
            var expectedHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token!)));
            Assert.Equal(expectedHash, stored.ResetTokenHash);
            // MySQL rounds the timestamp, so compare within a second rather than exactly.
            Assert.True(Math.Abs((stored.ResetTokenExpiresAtUtc!.Value - call.ExpiresAtUtc).TotalSeconds) < 1);
        }

        [Fact]
        public async Task ForgotPassword_UnknownEmail_PublishesNothing()
        {
            var (client, publisher, _) = CreateClient();

            var response = await client.PostAsJsonAsync("/api/auth/forgot-password",
                new { email = $"nobody-{Guid.NewGuid():N}@example.com" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(publisher.Calls);
        }

        [Fact]
        public async Task ForgotPassword_DeactivatedUser_PublishesNothing()
        {
            var (client, publisher, services) = CreateClient();
            var email = $"inactive-{Guid.NewGuid():N}@example.com";
            await AddUserAsync(services, email, isActive: false);

            var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(publisher.Calls);
        }
    }
}
