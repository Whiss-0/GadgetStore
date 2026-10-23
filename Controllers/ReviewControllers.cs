using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.ReviewModule;
using api.ActivityModule;
using Ganss.Xss;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReviewController : ControllerBase
    {
        private readonly IReviewRepository _reviewRepository;
        private readonly IActivityRepository _activityRepository;
        private readonly ILogger<ReviewController> _logger;

        public ReviewController(
            IReviewRepository reviewRepository,
            IActivityRepository activityRepository,
            ILogger<ReviewController> logger)
        {
            _reviewRepository = reviewRepository;
            _activityRepository = activityRepository;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<ActionResult<List<Review>>> GetAll(CancellationToken ct)
        {
            return Ok(await _reviewRepository.GetAllAsync(ct));
        }

        [AllowAnonymous]
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Review>> GetById(int id, CancellationToken ct)
        {
            var review = await _reviewRepository.GetByIdAsync(id, ct);
            if (review == null) return NotFound(new { message = $"Review with ID {id} not found." });
            return Ok(review);
        }

        [AllowAnonymous]
        [HttpGet("product/{productId:int}")]
        public async Task<ActionResult<List<Review>>> GetByProduct(int productId, CancellationToken ct)
        {
            return Ok(await _reviewRepository.GetByProductAsync(productId, ct));
        }

        [Authorize(Policy = "UserAccess")]
        [HttpGet("user/{userId:int}")]
        public async Task<ActionResult<List<Review>>> GetByUser(int userId, CancellationToken ct)
        {
            return Ok(await _reviewRepository.GetByUserAsync(userId, ct));
        }

        [Authorize(Policy = "UserAccess")]
        [HttpPost]
        public async Task<ActionResult<Review>> Create([FromBody] ReviewRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);

            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedTags.Clear();
            sanitizer.AllowedAttributes.Clear();
            string? cleanComment = dto.Comment != null ? sanitizer.Sanitize(dto.Comment) : null;

            var review = new Review
            {
                user_id = userId, // never trust dto.User_ID
                product_id = dto.Product_ID,
                rating = dto.Rating,
                comment = cleanComment,
                review_date = DateTime.UtcNow
            };
            int newId = await _reviewRepository.CreateAsync(review, ct);
            return CreatedAtAction(nameof(GetById), new { id = newId }, review);
        }

        [Authorize(Policy = "UserAccess")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ReviewUpdateRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _reviewRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Review with ID {id} not found." });

            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedTags.Clear();
            sanitizer.AllowedAttributes.Clear();
            string? cleanComment = dto.Comment != null ? sanitizer.Sanitize(dto.Comment) : null;

            existing.rating = dto.Rating;
            existing.comment = cleanComment;
            bool updated = await _reviewRepository.UpdateAsync(existing, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update review." });
            return NoContent();
        }

        [Authorize(Policy = "ModAccess")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var existing = await _reviewRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Review with ID {id} not found." });
            bool deleted = await _reviewRepository.DeleteAsync(id, ct);
            if (!deleted) return StatusCode(500, new { message = "Failed to delete review." });
            string? actorRole = GetAuditedActorRole();
            if (actorRole != null)
            {
                await TryLogAuditAsync(new ActivityLog
                {
                    User_ID = existing.user_id,
                    Actor_User_ID = GetActorId(),
                    Actor_Role = actorRole,
                    Activity_Type = "ReviewDeleted",
                    Description = $"Deleted review #{id} for product #{existing.product_id}.",
                    Related_Product_ID = existing.product_id,
                    Created_At = DateTime.UtcNow,
                });
            }
            return NoContent();
        }

        private int? GetActorId()
        {
            var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out int id) ? id : null;
        }

        private string? GetAuditedActorRole() => User.HasClaim("user_role_id", "1") ? "Admin" :
            User.HasClaim("user_role_id", "2") ? "Staff" : null;

        private async Task TryLogAuditAsync(ActivityLog log)
        {
            try { await _activityRepository.CreateAsync(log); }
            catch (Exception ex) { _logger.LogWarning(ex, "Audit log write failed ({Type})", log.Activity_Type); }
        }
    }

    public class ReviewRequest
    {
        public int User_ID { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int Product_ID { get; set; }
        [System.ComponentModel.DataAnnotations.Range(1, 5)]
        public int Rating { get; set; }
        [System.ComponentModel.DataAnnotations.StringLength(1000)]
        public string? Comment { get; set; }
    }

    public class ReviewUpdateRequest
    {
        [System.ComponentModel.DataAnnotations.Range(1, 5)]
        public int Rating { get; set; }
        [System.ComponentModel.DataAnnotations.StringLength(1000)]
        public string? Comment { get; set; }
    }
}
