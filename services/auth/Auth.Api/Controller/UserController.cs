using Microsoft.AspNetCore.Mvc;

using Auth.Api.Repository;

namespace Auth.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController
    {
        private readonly UserRepository _userRepository;

        public UserController(UserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _userRepository.GetUsersAsync();
            return new OkObjectResult(users);
        }
    }
}