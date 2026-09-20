using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using api.DTOs;
using api.Security;
using api.UserModule;
using api.Services;
using api.ActivityModule;
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
        private readonly IActivityRepository _activityRepository;

        public AuthController(
            IUserRepository userRepository,
            IJwtTokenService jwtTokenService,
            IOtpService otpService,
            IOtpEmailSender otpEmailSender,
            ILogger<AuthController> logger,
            IHostEnvironment environment,
            IActivityRepository activityRepository)
        {
            _userRepository = userRepository;
            _jwtTokenService = jwtTokenService;
            _otpService = otpService;
            _otpEmailSender = otpEmailSender;
            _logger = logger;
            _environment = environment;
            _activityRepository = activityRepository;
        }

        /// <summary>Login with username and password. Returns a JWT token.</summary>
        [EnableRateLimiting("auth")]
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

            // Record login (non-admin, non-MFA path)
            _ = TryLogActivityAsync(new ActivityLog
            {
                User_ID      = user.User_ID,
                Actor_User_ID = user.User_ID,
                Actor_Role   = RoleLabel(user.Role_ID),
                Activity_Type = "Login",
                Description  = "Signed in successfully.",
                Created_At   = DateTime.UtcNow,
            });

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

        [EnableRateLimiting("otp-verify")]
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

            // Record login after successful MFA verification
            _ = TryLogActivityAsync(new ActivityLog
            {
                User_ID       = user.User_ID,
                Actor_User_ID = user.User_ID,
                Actor_Role    = RoleLabel(user.Role_ID),
                Activity_Type = "Login",
                Description   = "Signed in successfully.",
                Created_At    = DateTime.UtcNow,
            });

            return Ok(new { 
                token, 
                tokenType = "Bearer",
                userId = user.User_ID,
                username = user.Name,
                roleId = user.Role_ID 
            });
        }

        /// <summary>Create a new user account. Anyone can register; role is always the default user role (never client-controlled).</summary>
        [EnableRateLimiting("auth")]
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

                // Record account creation
                _ = TryLogActivityAsync(new ActivityLog
                {
                    User_ID       = newId,
                    Actor_User_ID = newId,
                    Actor_Role    = "User",
                    Activity_Type = "AccountCreated",
                    Description   = "Created an account.",
                    Created_At    = DateTime.UtcNow,
                });

                return StatusCode(201, new { message = "User registered successfully.", userId = newId });
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return Conflict(new { message = "That username or email is already in use." });
            }
        }

        /// <summary>Request a password reset link. Always returns a generic response to prevent user enumeration.</summary>
        [EnableRateLimiting("auth")]
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
        [EnableRateLimiting("otp-verify")]
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
                email    = user.Email,
                address  = user.Address,
                roleId   = user.Role_ID
            });
        }

        /// <summary>Update the profile of the currently authenticated user.</summary>
        [Authorize(Policy = "UserAccess")]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var user = await _userRepository.GetByIdAsync(userId, ct);
            if (user == null) return NotFound();

            // Require current password verification before allowing a password change.
            bool passwordChanged = false;
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                    !PasswordHasher.Verify(user.Password, request.CurrentPassword))
                {
                    return BadRequest(new { message = "Current password is incorrect." });
                }
                await _userRepository.UpdatePasswordAsync(userId, PasswordHasher.Hash(request.Password), ct);
                passwordChanged = true;
            }

            user.Name = request.Name ?? user.Name;
            user.Email = request.Email ?? user.Email;
            user.Address = request.Address ?? user.Address;

            bool updated = await _userRepository.UpdateAsync(user, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update profile." });

            // Record the correct event — password change takes priority over generic profile update
            string actType = passwordChanged ? "PasswordChanged" : "ProfileUpdated";
            string actDesc = passwordChanged ? "Changed the account password." : "Updated account details.";
            _ = TryLogActivityAsync(new ActivityLog
            {
                User_ID       = userId,
                Actor_User_ID = userId,
                Actor_Role    = RoleLabel(user.Role_ID),
                Activity_Type = actType,
                Description   = actDesc,
                Created_At    = DateTime.UtcNow,
            });

            return Ok(new { message = "Profile updated successfully." });
        }

        // ---- Private helpers ----

        private static string RoleLabel(int? roleId) => roleId switch
        {
            1 => "Admin",
            2 => "Staff",
            _ => "User",
        };

        /// <summary>Best-effort activity log — never breaks the caller on failure.</summary>
        private async Task TryLogActivityAsync(ActivityLog log)
        {
            try
            {
                await _activityRepository.CreateAsync(log);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Activity log write failed for type {Type}", log.Activity_Type);
            }
        }
    }

    public class UpdateProfileRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Password { get; set; }
        public string? CurrentPassword { get; set; }
    }
}
