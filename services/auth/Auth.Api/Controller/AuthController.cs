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
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IAssignmentRotationClient _rotationClient;

        public AuthController(
            ApplicationDbContext context,
            ILogger<AuthController> logger,
            IConfiguration configuration,
            IAssignmentRotationClient rotationClient)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _rotationClient = rotationClient;
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
                var (user, error) = await CreateUserAsync(request.Name, request.Email, request.Password, request.Role);
                if (error is not null)
                {
                    return error;
                }

                var response = new UserRegisterResponseDTO
                {
                    Name = user!.Name,
                    Email = user.Email,
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

        private async Task<(User? User, ActionResult? Error)> CreateUserAsync(string name, string email, string password, UserRole role, int? createdBy = null)
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

                var (user, error) = await CreateUserAsync(request.Name, request.Email, request.Password, request.Role, adminId);
                if (error is not null)
                {
                    return error;
                }

                var response = new AdminCreateUserResponseDTO
                {
                    Id = user!.Id,
                    Name = user.Name,
                    Email = user.Email,
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

                var jwtkey = _configuration["Jwt:Key"];
                var jwtIssuer = _configuration["Jwt:Issuer"];
                var jwtAudience = _configuration["Jwt:Audience"];
                var expiryMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"]!);

                var claims = new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.Email, user.Email),
                    new Claim(ClaimTypes.Role, user.Role.ToString()),
                };

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtkey!));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    issuer: jwtIssuer,
                    audience: jwtAudience,
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                    signingCredentials: creds);

                var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

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
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            CreatedBy = user.CreatedBy,
        };

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
                    return NotFound(new { message = "User not found." });
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
                    return NotFound(new { message = "User not found." });
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
}}