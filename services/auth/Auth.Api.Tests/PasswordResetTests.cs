using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Auth.Api.Data;
using Auth.Api.Model;
using Auth.Api.DTO;
using System.Linq;

namespace Auth.Api.Tests
{
    public class PasswordResetTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public PasswordResetTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private async Task<User> SetupUserAsync(ApplicationDbContext dbContext, string email, string password, bool isActive = true)
        {
            var user = new User
            {
                Name = "Test User",
                Email = email,
                Password = BCrypt.Net.BCrypt.HashPassword(password),
                Role = UserRole.Employee,
                IsActive = isActive,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
            return user;
        }

        private async Task<User> GetFreshUserAsync(int userId)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        }

        [Fact]
        public async Task ForgotPassword_AlwaysReturns200_WithGenericMessage_ForExistingAndNonExistingEmails()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var client = _factory.CreateClient();

            var req1 = new { email = "unknown@example.com" };
            var res1 = await client.PostAsJsonAsync("/api/auth/forgot-password", req1);
            Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
            var body1 = await res1.Content.ReadFromJsonAsync<dynamic>();
            Assert.Equal("If an account exists for this email, a password reset link has been sent.", (string)body1.GetProperty("message").GetString());

            await SetupUserAsync(db, "exists@example.com", "Password123!");
            var req2 = new { email = "exists@example.com" };
            var res2 = await client.PostAsJsonAsync("/api/auth/forgot-password", req2);
            Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
            var body2 = await res2.Content.ReadFromJsonAsync<dynamic>();
            Assert.Equal("If an account exists for this email, a password reset link has been sent.", (string)body2.GetProperty("message").GetString());
        }

        [Fact]
        public async Task ResetToken_IsStoredAsHash_With15MinuteLifespan()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var client = _factory.CreateClient();

            var user = await SetupUserAsync(db, "token@example.com", "Password123!");
            
            var req = new { email = "token@example.com" };
            await client.PostAsJsonAsync("/api/auth/forgot-password", req);

            var updatedUser = await GetFreshUserAsync(user.Id);
            Assert.NotNull(updatedUser.ResetTokenHash);
            Assert.True(updatedUser.ResetTokenExpiresAtUtc.HasValue);
            var diff = updatedUser.ResetTokenExpiresAtUtc.Value - DateTime.UtcNow;
            Assert.True(diff.TotalMinutes <= 15 && diff.TotalMinutes > 14);
        }

        [Fact]
        public async Task ForgotPassword_RateLimits_MoreThan3RequestsPerWindow()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var client = _factory.CreateClient();

            var user = await SetupUserAsync(db, "ratelimit@example.com", "Password123!");
            
            var req = new { email = "ratelimit@example.com" };
            for(int i = 0; i < 4; i++) {
                await client.PostAsJsonAsync("/api/auth/forgot-password", req);
            }

            var updatedUser = await GetFreshUserAsync(user.Id);
            Assert.Equal(3, updatedUser.ResetRequestCountInWindow);
        }

        [Fact]
        public async Task ResetPassword_WithInvalidToken_Returns400BadRequest()
        {
            var client = _factory.CreateClient();
            var req = new { Token = "invalid_token", NewPassword = "NewPassword123!", ConfirmPassword = "NewPassword123!" };
            var res = await client.PostAsJsonAsync("/api/auth/reset-password", req);
            
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        [Fact]
        public async Task ResetPassword_WithSamePasswordAsCurrent_Returns400BadRequest()
        {
            // Note: Since we don't return the raw token in the response (stubbed out), we have to simulate token generation to test end-to-end
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var client = _factory.CreateClient();
            
            var user = await SetupUserAsync(db, "samepass@example.com", "SamePass123!");

            var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            user.ResetTokenHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawToken)));
            user.ResetTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(15);
            await db.SaveChangesAsync();

            var req = new { Token = rawToken, NewPassword = "SamePass123!", ConfirmPassword = "SamePass123!" };
            var res = await client.PostAsJsonAsync("/api/auth/reset-password", req);
            
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<dynamic>();
            Assert.Equal("New password cannot be the same as the current password.", (string)body.GetProperty("message").GetString());
        }

        [Fact]
        public async Task ResetPassword_WithValidToken_UpdatesPasswordAndInvalidatesToken()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var client = _factory.CreateClient();
            
            var user = await SetupUserAsync(db, "validreset@example.com", "OldPass123!");
            var oldStamp = user.SecurityStamp;

            var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            user.ResetTokenHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawToken)));
            user.ResetTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(15);
            await db.SaveChangesAsync();

            var req = new { Token = rawToken, NewPassword = "NewPassword123!", ConfirmPassword = "NewPassword123!" };
            var res = await client.PostAsJsonAsync("/api/auth/reset-password", req);
            
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var updatedUser = await GetFreshUserAsync(user.Id);
            Assert.True(updatedUser.ResetTokenUsed);
            Assert.Null(updatedUser.ResetTokenExpiresAtUtc);
            Assert.NotEqual(oldStamp, updatedUser.SecurityStamp);
            Assert.True(BCrypt.Net.BCrypt.Verify("NewPassword123!", updatedUser.Password));
        }

        [Fact]
        public async Task ResetPassword_WithAlreadyUsedToken_Returns400BadRequest()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var client = _factory.CreateClient();
            
            var user = await SetupUserAsync(db, "used@example.com", "OldPass123!");

            var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            user.ResetTokenHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawToken)));
            user.ResetTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(15);
            user.ResetTokenUsed = true;
            await db.SaveChangesAsync();

            var req = new { Token = rawToken, NewPassword = "NewPassword123!", ConfirmPassword = "NewPassword123!" };
            var res = await client.PostAsJsonAsync("/api/auth/reset-password", req);
            
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }
}
