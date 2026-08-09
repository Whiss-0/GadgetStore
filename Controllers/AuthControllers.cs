using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.DTOs;
using api.Security;
using api.UserModule;
using api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private const int DefaultUserRoleId = 3; // standard user role — never let clients set this

        private readonly IUserRepository _userRepository;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IOtpService _otpService;
        private readonly IOtpEmailSender _otpEmailSender;
        private readonly ILogger<AuthController> _logger;
        private readonly IHostEnvironment _environment;

        public AuthController(
            IUserRepository userRepository,
            IJwtTokenService jwtTokenService,
            IOtpService otpService,
            IOtpEmailSender otpEmailSender,
            ILogger<AuthController> logger,
            IHostEnvironment environment)
        {
            _userRepository = userRepository;
            _jwtTokenService = jwtTokenService;
            _otpService = otpService;
            _otpEmailSender = otpEmailSender;
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

            bool requiresMfa = user.Role_ID == 1; // Admin role id

            if (requiresMfa)
            {
                string code = await _otpService.GenerateAsync(user.User_ID, "login", ct);
                await _otpEmailSender.SendOtpAsync(user.Email, code, ct);
                return Ok(new { requiresMfa = true, message = "Enter the code sent to your email to finish logging in." });
            }

            string token = _jwtTokenService.GenerateToken(user);
            return Ok(new
            {
                requiresMfa = false,
                token,
                tokenType = "Bearer",
                userId   = user.User_ID,
                username = user.Name,
                roleId   = user.Role_ID
            });
        }

        [AllowAnonymous]
        [HttpPost("login/verify-mfa")]
        public async Task<IActionResult> VerifyLoginMfa([FromBody] VerifyMfaRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _userRepository.GetByUsernameAsync(request.Username, ct);
            if (user == null) return BadRequest(new { message = "Invalid code." });

            var result = await _otpService.VerifyAsync(user.User_ID, request.Code, "login", ct);
            if (result != OtpVerifyResult.Valid)
            {
                var message = result switch
                {
                    OtpVerifyResult.Expired => "This code has expired. Log in again to get a new one.",
                    OtpVerifyResult.MaxAttemptsReached => "Too many attempts. Try again in a few minutes.",
                    _ => "Invalid code."
                };
                return BadRequest(new { message });
            }

            var token = _jwtTokenService.GenerateToken(user);
            return Ok(new { 
                token, 
                tokenType = "Bearer",
                userId = user.User_ID,
                username = user.Name,
                roleId = user.Role_ID 
            });
        }

        /// <summary>Create a new user account. Anyone can register; role is always the default user role (never client-controlled).</summary>
        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existing = await _userRepository.GetByUsernameAsync(request.Username, ct);
            if (existing != null)
                return Conflict(new { message = "A user with that username already exists." });

            var existingEmail = await _userRepository.GetByEmailAsync(request.Email, ct);
            if (existingEmail != null)
                return Conflict(new { message = "That email is already registered." });

            var user = new User
            {
                Name     = request.Username,
                Email    = request.Email,
                Password = PasswordHasher.Hash(request.Password),
                Role_ID  = DefaultUserRoleId  // hardcoded — clients must never control their own role
            };

            try
            {
                int newId = await _userRepository.CreateAsync(user, ct);
                return StatusCode(201, new { message = "User registered successfully.", userId = newId });
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return Conflict(new { message = "That username or email is already in use." });
            }
        }

        /// <summary>Request a password reset link. Always returns a generic response to prevent user enumeration.</summary>
        [AllowAnonymous]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _userRepository.GetByUsernameAsync(request.Username, ct);
            _logger.LogInformation("Forgot-password called for username: {Username}. User found: {Found}", request.Username, user != null);

            if (user != null)
            {
                string code = await _otpService.GenerateAsync(user.User_ID, "reset", ct);
                await _otpEmailSender.SendOtpAsync(user.Email, code, ct);
            }

            // Same generic response either way — don't reveal which usernames exist.
            return Ok(new { message = "If that account exists, a reset code has been sent." });
        }

        /// <summary>Reset password using a valid reset token.</summary>
        [AllowAnonymous]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _userRepository.GetByUsernameAsync(request.Username, ct);
            if (user == null) return BadRequest(new { message = "Invalid code or username." });

            var result = await _otpService.VerifyAsync(user.User_ID, request.Code, "reset", ct);
            if (result != OtpVerifyResult.Valid)
            {
                var message = result switch
                {
                    OtpVerifyResult.Expired => "This code has expired. Request a new one.",
                    OtpVerifyResult.MaxAttemptsReached => "Too many attempts. Try again in a few minutes.",
                    _ => "Invalid code."
                };
                return BadRequest(new { message });
            }

            await _userRepository.UpdatePasswordAsync(user.User_ID, PasswordHasher.Hash(request.NewPassword), ct);
            return Ok(new { message = "Password updated." });
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
