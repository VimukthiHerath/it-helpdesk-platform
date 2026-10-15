using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Auth.Api.Authorization;
using Auth.Api.Data;
using Auth.Api.DTO;
using Auth.Api.Model;
using Auth.Api.Services;

namespace Auth.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private const string UserNotFoundMessage = "User not found.";

        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IAssignmentRotationClient _rotationClient;
        private readonly ITokenService _tokenService;
        private readonly IPasswordResetPublisher _passwordResetPublisher;

        public AuthController(
            ApplicationDbContext context,
            ILogger<AuthController> logger,
            IConfiguration configuration,
            IAssignmentRotationClient rotationClient,
            ITokenService tokenService,
            IPasswordResetPublisher passwordResetPublisher)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _rotationClient = rotationClient;
            _tokenService = tokenService;
            _passwordResetPublisher = passwordResetPublisher;
        }

        [HttpPost("register")]
        public async Task<ActionResult<UserRegisterResponseDTO>> Register([FromBody] UserRegisterDTO request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                var (user, error) = await CreateUserAsync(request.Name, request.Email, request.Phone, request.Password, request.Role);
                if (error is not null)
                {
                    return error;
                }

                var response = new UserRegisterResponseDTO
                {
                    Name = user!.Name,
                    Email = user.Email,
                    Phone = user.Phone ?? string.Empty,
                    Role = user.Role,
                    CreatedAt = user.CreatedAt
                };

                return StatusCode(StatusCodes.Status201Created, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering user");
                return Problem("Unable to register user. Please try again later.");
            }
        }

        private async Task<(User? User, ActionResult? Error)> CreateUserAsync(string name, string email, string phone, string password, UserRole role, int? createdBy = null)
        {
            var userExists = await _context.Users
                .AnyAsync(u => u.Email == email);

            if (userExists)
            {
                return (null, Conflict(new { message = "Email already registered." }));
            }

            var user = new User
            {
                Name = name,
                Email = email,
                Phone = phone,
                Password = BCrypt.Net.BCrypt.HashPassword(password),
                Role = role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = null,
                LastLoginAt = null,
                CreatedBy = createdBy
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return (user, null);
        }

        [Authorize(Roles = Roles.Administrator)]
        [HttpPost("users")]
        public async Task<ActionResult<AdminCreateUserResponseDTO>> CreateUser([FromBody] UserRegisterDTO request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                var adminIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
                var adminId = int.Parse(adminIdClaim!);

                var (user, error) = await CreateUserAsync(request.Name, request.Email, request.Phone, request.Password, request.Role, adminId);
                if (error is not null)
                {
                    return error;
                }

                var response = new AdminCreateUserResponseDTO
                {
                    Id = user!.Id,
                    Name = user.Name,
                    Email = user.Email,
                    Phone = user.Phone,
                    Role = user.Role,
                    CreatedAt = user.CreatedAt,
                    CreatedBy = user.CreatedBy
                };

                return StatusCode(StatusCodes.Status201Created, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return Problem("Unable to create user. Please try again later.");
            }
        }

        [HttpPost("login")]
        public async Task<ActionResult<UserLoginResponseDTO>> Login([FromBody] UserLoginDTO request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == request.Email);

                if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.Password))
                {
                    return Unauthorized(new { message = "Invalid email or password." });
                }

                if (!user.IsActive)
                {
                    return Unauthorized(new { message = "Your account has been deactivated. Please contact your administrator." });
                }

                var expiryMinutes = int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var parsed) ? parsed : 60;
                var roleName = user.Role.ToString();

                // Older accounts predate the password-reset migration and may have no stamp yet.
                user.SecurityStamp ??= Guid.NewGuid().ToString();
                var tokenString = _tokenService.CreateToken(user.Id, user.Email, roleName, user.SecurityStamp, expiryMinutes);

                user.LastLoginAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new 
                { 
                    token = tokenString,
                    message = "Login successful"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging in user");
                return Problem("Unable to login. Please try again later.");
            }
        }

        [Authorize(Roles = Roles.Administrator)]
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                var users = await _context.Users
                    .OrderBy(u => u.Id)
                    .ToListAsync();

                return Ok(users.Select(ToUserListItemDto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing users");
                return Problem("Unable to load users. Please try again later.");
            }
        }

        private static AdminUserListItemDTO ToUserListItemDto(User user) => new()
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            CreatedBy = user.CreatedBy,
        };

        // Agents need a requester's email and phone to follow up on a ticket
        // assigned to them, so reading one user is open to Agent as well as
        // Administrator. Listing every user stays Administrator-only.
        [Authorize(Roles = $"{Roles.Agent},{Roles.Administrator}")]
        [HttpGet("users/{id}")]
        public async Task<IActionResult> GetUser(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user is null)
                {
                    return NotFound(new { message = UserNotFoundMessage });
                }

                return Ok(ToUserListItemDto(user));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user {UserId}", id);
                return Problem("Unable to load the user. Please try again later.");
            }
        }

        [Authorize(Roles = Roles.Administrator)]
        [HttpPut("users/{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDTO request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user is null)
                {
                    return NotFound(new { message = UserNotFoundMessage });
                }

                var callerId = GetAuthenticatedUserId();
                if (callerId == id && user.Role == UserRole.Administrator && request.Role != UserRole.Administrator)
                {
                    return Conflict(new { message = "You cannot change your own role away from Administrator." });
                }

                var emailTaken = await _context.Users
                    .AnyAsync(u => u.Id != id && u.Email == request.Email);
                if (emailTaken)
                {
                    return Conflict(new { message = "That email is already registered." });
                }

                // Changing an agent's role away from Agent must be blocked while
                // they're still in Assignment's round-robin rotation - see
                // AssignmentRotationClient for why this cross-service check
                // exists, and agent-rotation-management.md for the full chain
                // (ASSIGN-5's future "remove from rotation" is what actually
                // clears this, and that removal must itself refuse to run
                // while the agent's queue is non-empty).
                if (user.Role == UserRole.Agent && request.Role != UserRole.Agent)
                {
                    var blocked = await BlockIfAgentStillInRotationAsync(user.Id);
                    if (blocked is not null)
                    {
                        return blocked;
                    }
                }

                user.Name = request.Name;
                user.Email = request.Email;
                user.Phone = request.Phone;
                user.Role = request.Role;
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(ToUserListItemDto(user));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user {UserId}", id);
                return Problem("Unable to update user. Please try again later.");
            }
        }

        [Authorize(Roles = Roles.Administrator)]
        [HttpPatch("users/{id}/deactivate")]
        public async Task<IActionResult> DeactivateUser(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user is null)
                {
                    return NotFound(new { message = UserNotFoundMessage });
                }

                var callerId = GetAuthenticatedUserId();
                if (callerId == id)
                {
                    return Conflict(new { message = "You cannot deactivate your own account." });
                }

                // Idempotent: deactivating an already-inactive account is a
                // no-op success rather than an error.
                if (!user.IsActive)
                {
                    return Ok(ToUserListItemDto(user));
                }

                // Same rotation guard as UpdateUser - see there and
                // AssignmentRotationClient for the full reasoning.
                if (user.Role == UserRole.Agent)
                {
                    var blocked = await BlockIfAgentStillInRotationAsync(user.Id);
                    if (blocked is not null)
                    {
                        return blocked;
                    }
                }

                user.IsActive = false;
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(ToUserListItemDto(user));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating user {UserId}", id);
                return Problem("Unable to deactivate user. Please try again later.");
            }
        }

        private int GetAuthenticatedUserId()
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            return int.Parse(userId!);
        }

        private async Task<IActionResult?> BlockIfAgentStillInRotationAsync(int userId)
        {
            var authorizationHeader = Request.Headers.Authorization.ToString();
            var result = await _rotationClient.IsUserInRotationAsync(userId, authorizationHeader, HttpContext.RequestAborted);

            return result switch
            {
                RotationCheckResult.InRotation => Conflict(new
                {
                    message = "This agent is still in the ticket rotation. Remove them from the rotation before changing their role or deactivating this account."
                }),
                RotationCheckResult.Unreachable => StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Unable to verify this agent's rotation status right now. Please try again later."
                }),
                _ => null,
            };
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult GetMe()
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = User.FindFirstValue(ClaimTypes.Email);
            var role = User.FindFirstValue(ClaimTypes.Role);

            return Ok(new
            {
                userId,
                email,
                role
            });
        }

        [AllowAnonymous]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDTO request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var genericResponse = Ok(new { message = "If an account exists for this email, a password reset link has been sent." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null || !user.IsActive) return genericResponse;

            var windowMinutes = _configuration.GetValue<int>("PasswordReset:WindowMinutes", 15);
            var maxRequests = _configuration.GetValue<int>("PasswordReset:MaxRequestsPerWindow", 3);

            if (user.LastPasswordResetRequestedAtUtc.HasValue && 
                DateTime.UtcNow <= user.LastPasswordResetRequestedAtUtc.Value.AddMinutes(windowMinutes))
            {
                if (user.ResetRequestCountInWindow >= maxRequests) return genericResponse;
                user.ResetRequestCountInWindow++;
            }
            else
            {
                user.ResetRequestCountInWindow = 1;
            }

            user.LastPasswordResetRequestedAtUtc = DateTime.UtcNow;

            var rawTokenBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var rawToken = Convert.ToBase64String(rawTokenBytes);

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var tokenHashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawToken));
            var tokenHash = Convert.ToBase64String(tokenHashBytes);

            user.ResetTokenHash = tokenHash;
            user.ResetTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(_configuration.GetValue<int>("PasswordReset:TokenTtlMinutes", 15));
            user.ResetTokenUsed = false;

            await _context.SaveChangesAsync();

            var resetBaseUrl = _configuration.GetValue<string>("PasswordReset:ResetBaseUrl", "http://localhost:3000/reset-password");
            var resetLink = $"{resetBaseUrl}?token={Uri.EscapeDataString(rawToken)}";

            // The email is sent by Notification.Api. The link is a secret, so it is never logged.
            _passwordResetPublisher.Publish(user.Id, user.Email, resetLink, user.ResetTokenExpiresAtUtc!.Value);
            _logger.LogInformation("Password reset requested for user {UserId}.", user.Id);

            return genericResponse;
        }

        [AllowAnonymous]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDTO request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var incomingTokenHashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(request.Token));
            var incomingTokenHash = Convert.ToBase64String(incomingTokenHashBytes);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.ResetTokenHash == incomingTokenHash);

            if (user == null || user.ResetTokenUsed || !user.ResetTokenExpiresAtUtc.HasValue || user.ResetTokenExpiresAtUtc <= DateTime.UtcNow)
            {
                return BadRequest(new { message = "This reset link has expired or is invalid. Please request a new one." });
            }

            if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.Password))
            {
                return BadRequest(new { message = "New password cannot be the same as the current password." });
            }

            user.Password = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.ResetTokenUsed = true;
            user.ResetTokenExpiresAtUtc = null;
            user.SecurityStamp = Guid.NewGuid().ToString();

            await _context.SaveChangesAsync();

            return Ok(new { message = "Password has been successfully reset. Please log in with your new password." });
        }

        // Internal service-to-service endpoint.
        // Used by Notification.Api Kafka consumers (which have no JWT token) to resolve
        // a user's email address by their ID for email dispatch.
        [AllowAnonymous]
        [HttpGet("internal/users/{id}/email")]
        public async Task<IActionResult> GetUserEmail(int id)
        {
            if (!IsAuthorizedInternalCall())
            {
                return Unauthorized(new { message = "Unauthorized: Authentication or valid internal service key required." });
            }

            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user is null)
                {
                    return NotFound(new { message = UserNotFoundMessage });
                }

                return Ok(new { email = user.Email, name = user.Name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching email for user {UserId}", id);
                return Problem("Unable to fetch user email.");
            }
        }

        // Internal service-to-service endpoint.
        // Used by Notification.Api to fetch all active administrator emails for SLA breach alerts.
        [AllowAnonymous]
        [HttpGet("internal/admins/emails")]
        public async Task<IActionResult> GetAdminEmails()
        {
            if (!IsAuthorizedInternalCall())
            {
                return Unauthorized(new { message = "Unauthorized: Authentication or valid internal service key required." });
            }

            try
            {
                var emails = await _context.Users
                    .Where(u => u.Role == UserRole.Administrator && u.IsActive)
                    .Select(u => u.Email)
                    .ToListAsync();

                return Ok(new { emails });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching administrator emails");
                return Problem("Unable to fetch administrator emails.");
            }
        }

        private bool IsAuthorizedInternalCall()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                return true;
            }

            var expectedKey = _configuration["InternalService:ApiKey"];
            if (string.IsNullOrEmpty(expectedKey))
            {
                return false;
            }

            if (Request.Headers.TryGetValue("X-Internal-Key", out var providedKey))
            {
                return string.Equals(expectedKey, providedKey.ToString(), StringComparison.Ordinal);
            }

            return false;
        }
    }
}
