using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.Main;
using api.UserModule;
using api.DTOs;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserRepository _userRepository;

        public UserController(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        /// <summary>Get all users (paginated or full list).</summary>
        [Authorize(Policy = "AdminAccess")]
        [HttpGet]
        public async Task<ActionResult<object>> GetUsers(
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            CancellationToken ct)
        {
            if (pageNumber.HasValue && pageSize.HasValue)
            {
                var pagedResult = await _userRepository.GetPagedAsync(pageNumber.Value, pageSize.Value, ct);
                var dtoResult = new PaginationModel<UserResponse>
                {
                    Items = pagedResult.Items.Select(MapToDto).ToList(),
                    TotalCount = pagedResult.TotalCount,
                    PageSize = pagedResult.PageSize,
                    CurrentPage = pagedResult.CurrentPage
                };
                return Ok(dtoResult);
            }

            var users = await _userRepository.GetAllAsync(ct);
            return Ok(users.Select(MapToDto).ToList());
        }

        /// <summary>Get a user by ID.</summary>
        [Authorize(Policy = "ModAccess")]
        [HttpGet("{id:int}")]
        public async Task<ActionResult<UserResponse>> GetUserById(int id, CancellationToken ct)
        {
            var user = await _userRepository.GetByIdAsync(id, ct);
            if (user == null) return NotFound(new { message = $"User with ID {id} was not found." });
            return Ok(MapToDto(user));
        }

        /// <summary>Create a new user.</summary>
        [Authorize(Policy = "AdminAccess")]
        [HttpPost]
        public async Task<ActionResult<UserResponse>> CreateUser([FromBody] CreateUserRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existing = await _userRepository.GetByEmailAsync(dto.Email, ct);
            if (existing != null) return Conflict(new { message = $"A user with email '{dto.Email}' already exists." });

            var user = new User
            {
                Name = dto.Name,
                Email = dto.Email,
                Password = Security.PasswordHasher.Hash(dto.Password),
                Address = dto.Address,
                Role_ID = dto.Role_ID
            };

            int newId = await _userRepository.CreateAsync(user, ct);
            return CreatedAtAction(nameof(GetUserById), new { id = newId }, MapToDto(user));
        }

        /// <summary>Update a user.</summary>
        [Authorize(Policy = "ModAccess")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existingUser = await _userRepository.GetByIdAsync(id, ct);
            if (existingUser == null) return NotFound(new { message = $"User with ID {id} was not found." });

            existingUser.Name = dto.Name ?? existingUser.Name;
            existingUser.Email = dto.Email ?? existingUser.Email;
            existingUser.Address = dto.Address ?? existingUser.Address;
            existingUser.Role_ID = dto.Role_ID ?? existingUser.Role_ID;
            if (!string.IsNullOrWhiteSpace(dto.Password))
                existingUser.Password = Security.PasswordHasher.Hash(dto.Password);

            bool updated = await _userRepository.UpdateAsync(existingUser, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update user." });

            return NoContent();
        }

        /// <summary>Delete a user.</summary>
        [Authorize(Policy = "AdminAccess")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUser(int id, CancellationToken ct)
        {
            var existingUser = await _userRepository.GetByIdAsync(id, ct);
            if (existingUser == null) return NotFound(new { message = $"User with ID {id} was not found." });

            bool deleted = await _userRepository.DeleteAsync(id, ct);
            if (!deleted) return StatusCode(500, new { message = "Failed to delete user." });

            return NoContent();
        }

        private static UserResponse MapToDto(User user) => new UserResponse
        {
            User_ID = user.User_ID,
            Name = user.Name,
            Email = user.Email,
            Address = user.Address,
            Role_ID = user.Role_ID
        };
    }

    // ---- Request DTOs (only used in User controller) ----
    public class CreateUserRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Name { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.EmailAddress]
        public string Email { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MinLength(6)]
        public string Password { get; set; } = string.Empty;
        public string? Address { get; set; }
        public int? Role_ID { get; set; }
    }

    public class UpdateUserDto
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? Address { get; set; }
        public int? Role_ID { get; set; }
    }
}
