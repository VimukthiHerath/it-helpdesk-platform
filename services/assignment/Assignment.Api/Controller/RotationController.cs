using Assignment.Api.Authorization;
using Assignment.Api.Data;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;

namespace Assignment.Api.Controller;

[ApiController]
[Route("api/Assignment/[controller]")]
[Authorize(Roles = Roles.Administrator)]
public class RotationController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RotationController> _logger;
    private readonly HttpClient _authClient;
    private readonly string _authApiBaseUrl;

    public RotationController(
        ApplicationDbContext context,
        ILogger<RotationController> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _authClient = httpClientFactory.CreateClient("AuthApi");
        _authApiBaseUrl = (configuration["AuthApi:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
    }

    [HttpGet("{agentId}")]
    public async Task<IActionResult> GetAgentRotationStatus(int agentId)
    {
        try
        {
            var agent = await _context.Agents.FirstOrDefaultAsync(a => a.UserId == agentId);
            if (agent == null)
            {
                return NotFound(new { message = $"Agent with user ID {agentId} not found." });
            }

            return Ok(new
            {
                userId = agent.UserId,
                isActive = agent.IsActive,
                displayOrder = agent.DisplayOrder
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent {AgentId} status", agentId);
            return Problem("Unable to load agent status. Please try again later.");
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddAgentToRotation([FromBody] AddAgentToRotationDTO request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            // Verify user exists in Auth.Api
            try
            {
                var authUrl = string.IsNullOrEmpty(_authClient.BaseAddress?.ToString())
                    ? $"{_authApiBaseUrl}/api/Auth/users/{request.UserId}"
                    : $"/api/Auth/users/{request.UserId}";

                var authResponse = await _authClient.GetAsync(authUrl);

                if (authResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return NotFound(new { message = $"User ID {request.UserId} does not exist in the Auth system." });
                }

                if (!authResponse.IsSuccessStatusCode)
                {
                    return StatusCode(500, new { message = "Failed to validate user with Auth service." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Auth service to validate user {UserId}", request.UserId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Unable to validate user with Auth service at this time." });
            }
            var existingAgent = await _context.Agents.FirstOrDefaultAsync(a => a.UserId == request.UserId);
            var adminId = GetAuthenticatedUserId();

            if (existingAgent != null)
            {
                if (existingAgent.IsActive)
                {
                    return Conflict(new { message = "Agent is already active in the rotation pool." });
                }
                else
                {
                    existingAgent.IsActive = true;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Admin {AdminId} added/reactivated Agent {AgentId} to the rotation pool.", adminId, request.UserId);
                    return Ok(new { message = "Agent reactivated successfully." });
                }
            }

            var maxDisplayOrder = await _context.Agents
                .Select(a => (int?)a.DisplayOrder)
                .MaxAsync();

            var agent = new Agent
            {
                UserId = request.UserId,
                DisplayOrder = (maxDisplayOrder ?? -1) + 1,
                IsActive = true
            };

            _context.Agents.Add(agent);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Admin {AdminId} added/reactivated Agent {AgentId} to the rotation pool.", adminId, request.UserId);

            var response = new AgentDTO
            {
                Id = agent.Id,
                UserId = agent.UserId,
                DisplayOrder = agent.DisplayOrder,
                IsActive = agent.IsActive
            };

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding agent to rotation");
            return Problem("Unable to add the agent to the rotation. Please try again later.");
        }
    }

    [HttpDelete("{agentId}")]
    public async Task<IActionResult> RemoveAgentFromRotation(int agentId)
    {
        try
        {
            var agent = await _context.Agents.FirstOrDefaultAsync(a => a.UserId == agentId);
            if (agent == null)
            {
                return NotFound(new { message = "Agent not found in the rotation pool." });
            }

            agent.IsActive = false;

            var activeAgentsCount = await _context.Agents.CountAsync(a => a.IsActive && a.UserId != agentId);
            if (activeAgentsCount == 0)
            {
                _logger.LogWarning("No active agents available for round-robin.");
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Agent removed from the rotation pool." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing agent from rotation");
            return Problem("Unable to remove the agent from the rotation. Please try again later.");
        }
    }

    private int GetAuthenticatedUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (int.TryParse(userIdString, out var userId))
        {
            return userId;
        }
        return 0; // Fallback or handle accordingly
    }
}
