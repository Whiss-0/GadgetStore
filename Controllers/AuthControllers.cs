using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.DTOs;
using api.Security;
using api.UserModule;
using api.Services;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private const int DefaultUserRoleId = 3; // standard user role — never let clients set this

        private readonly IUserRepository _userRepository;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IEmailService _emailService;
        private readonly ILogger<AuthController> _logger;
        private readonly IHostEnvironment _environment;

        public AuthController(
            IUserRepository userRepository,
            IJwtTokenService jwtTokenService,
            IEmailService emailService,
            ILogger<AuthController> logger,
            IHostEnvironment environment)
        {
            _userRepository = userRepository;
            _jwtTokenService = jwtTokenService;
            _emailService = emailService;
            _logger = logger;
            _environment = environment;
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
                Role_ID  = DefaultUserRoleId
            };

            int newId = await _userRepository.CreateAsync(user, ct);
            return StatusCode(201, new { message = "User registered successfully.", userId = newId });
        }

        /// <summary>Request a password reset link. Always returns a generic response to prevent user enumeration.</summary>
        [AllowAnonymous]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(
            [FromBody] ForgotPasswordRequest request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _userRepository.GetByUsernameAsync(request.Username, ct);

            // Always return the same generic response whether or not the user exists,
            // and whether or not we are about to send an email — prevents enumeration.
            if (user != null)
            {
                string resetToken = _jwtTokenService.GeneratePasswordResetToken(user.User_ID.ToString());

                if (_environment.IsDevelopment())
                {
                    // OK to log locally for testing — never returned in the response body
                    _logger.LogInformation("DEV reset token for user {UserId}: {Token}", user.User_ID, resetToken);
                }
                else
                {
                    await _emailService.SendPasswordResetEmailAsync(user.Email, resetToken, ct);
                }
            }

            return Ok(new { message = "If that account exists, a reset link has been sent." });
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
