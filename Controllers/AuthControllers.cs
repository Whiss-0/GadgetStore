using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.DTOs;
using api.Security;
using api.UserModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IJwtTokenService _jwtTokenService;

        public AuthController(IUserRepository userRepository, IJwtTokenService jwtTokenService)
        {
            _userRepository = userRepository;
            _jwtTokenService = jwtTokenService;
        }

        /// <summary>Login with username and password. Returns a JWT token.</summary>
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _userRepository.GetByUsernameAsync(request.Username, ct);
            if (user == null || !PasswordHasher.Verify(user.Password, request.Password))
                return Unauthorized(new { message = "Invalid username or password." });

            string token = _jwtTokenService.GenerateToken(user);
            return Ok(new
            {
                token,
                tokenType = "Bearer",
                userId   = user.User_ID,
                username = user.Name,
                roleId   = user.Role_ID
            });
        }

        /// <summary>Register a new user account.</summary>
        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existing = await _userRepository.GetByUsernameAsync(request.Username, ct);
            if (existing != null)
                return Conflict(new { message = "A user with that username already exists." });

            var user = new User
            {
                Name     = request.Username,
                Email    = string.Empty,
                Password = PasswordHasher.Hash(request.Password),
                Role_ID  = request.RoleId
            };

            int newId = await _userRepository.CreateAsync(user, ct);
            return StatusCode(201, new { message = "User registered successfully.", userId = newId });
        }

        /// <summary>Request a password reset token (returns token for testing - in production send via email).</summary>
        [AllowAnonymous]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _userRepository.GetByUsernameAsync(request.Username, ct);
            // Always return 200 to prevent user enumeration
            if (user == null)
                return Ok(new { message = "If that account exists, a reset token has been issued." });

            string resetToken = _jwtTokenService.GeneratePasswordResetToken(user.User_ID.ToString());
            // In production, email this token; for dev return it directly
            return Ok(new { message = "Password reset token generated.", resetToken });
        }

        /// <summary>Reset password using a valid reset token.</summary>
        [AllowAnonymous]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (!_jwtTokenService.TryValidatePasswordResetToken(request.Token, out string userId))
                return BadRequest(new { message = "Invalid or expired reset token." });

            if (!int.TryParse(userId, out int id))
                return BadRequest(new { message = "Invalid token payload." });

            var user = await _userRepository.GetByIdAsync(id, ct);
            if (user == null)
                return NotFound(new { message = "User not found." });

            string newHash = PasswordHasher.Hash(request.NewPassword);
            bool updated = await _userRepository.UpdatePasswordAsync(id, newHash, ct);
            if (!updated)
                return StatusCode(500, new { message = "Failed to update password." });

            return Ok(new { message = "Password reset successfully." });
        }

        /// <summary>Get the profile of the currently authenticated user.</summary>
        [Authorize(Policy = "UserAccess")]
        [HttpGet("me")]
        public async Task<IActionResult> GetMe(CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var user = await _userRepository.GetByIdAsync(userId, ct);
            if (user == null) return NotFound();

            return Ok(new
            {
                userId   = user.User_ID,
                username = user.Name,
                roleId   = user.Role_ID
            });
        }
    }
}
