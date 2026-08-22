using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;
using Auth.Api.Data;
using Auth.Api.DTO;
using Auth.Api.Model;

namespace Auth.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuthController> _logger;

        public AuthController(ApplicationDbContext context, ILogger<AuthController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<ActionResult<User>> Register([FromBody] UserRegisterDTO request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                var userExists = await _context.Users
                    .AnyAsync(u => u.Email == request.Email);

                if (userExists)
                {
                    return Conflict(new { message = "Email already registered." });
                }

                var user = new User
                {
                    Name = request.Name,
                    Email = request.Email,
                    Password = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    Role = request.Role,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = null,
                    LastLoginAt = null
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                return StatusCode(StatusCodes.Status201Created, user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering user");
                return Problem("Unable to register user. Please try again later.");
            }
        }

        [HttpPost("login")]
        public async Task<ActionResult<User>> Login([FromBody] UserLoginDTO request)
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

                user.LastLoginAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging in user");
                return Problem("Unable to login. Please try again later.");
            }
        }
    }
}