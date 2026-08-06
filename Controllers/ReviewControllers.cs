using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.ReviewModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReviewController : ControllerBase
    {
        private readonly IReviewRepository _reviewRepository;

        public ReviewController(IReviewRepository reviewRepository)
        {
            _reviewRepository = reviewRepository;
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

            var review = new Review
            {
                user_id = dto.User_ID > 0 ? dto.User_ID : userId,
                product_id = dto.Product_ID,
                rating = dto.Rating,
                comment = dto.Comment,
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
            existing.rating = dto.Rating;
            existing.comment = dto.Comment;
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
            return NoContent();
        }
    }

    public class ReviewRequest
    {
        public int User_ID { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int Product_ID { get; set; }
        [System.ComponentModel.DataAnnotations.Range(1, 5)]
        public int Rating { get; set; }
        public string? Comment { get; set; }
    }

    public class ReviewUpdateRequest
    {
        [System.ComponentModel.DataAnnotations.Range(1, 5)]
        public int Rating { get; set; }
        public string? Comment { get; set; }
    }
}
