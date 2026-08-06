using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.CategoriesModule;
using api.Main;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CategoriesController : ControllerBase
    {
        private readonly ICategoriesRepository _categoriesRepository;

        public CategoriesController(ICategoriesRepository categoriesRepository)
        {
            _categoriesRepository = categoriesRepository;
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<ActionResult<object>> GetAll([FromQuery] int? pageNumber, [FromQuery] int? pageSize, CancellationToken ct)
        {
            if (pageNumber.HasValue && pageSize.HasValue)
            {
                var paged = await _categoriesRepository.GetPagedAsync(pageNumber.Value, pageSize.Value, ct);
                return Ok(paged);
            }
            var categories = await _categoriesRepository.GetAllAsync(ct);
            return Ok(categories);
        }

        [AllowAnonymous]
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Category>> GetById(int id, CancellationToken ct)
        {
            var category = await _categoriesRepository.GetByIdAsync(id, ct);
            if (category == null) return NotFound(new { message = $"Category with ID {id} not found." });
            return Ok(category);
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpPost]
        public async Task<ActionResult<Category>> Create([FromBody] CategoryRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var category = new Category { category_name = dto.CategoryName };
            int newId = await _categoriesRepository.CreateAsync(category, ct);
            return CreatedAtAction(nameof(GetById), new { id = newId }, category);
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CategoryRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _categoriesRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Category with ID {id} not found." });
            existing.category_name = dto.CategoryName;
            bool updated = await _categoriesRepository.UpdateAsync(existing, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update category." });
            return NoContent();
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var existing = await _categoriesRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Category with ID {id} not found." });
            bool deleted = await _categoriesRepository.DeleteAsync(id, ct);
            if (!deleted) return StatusCode(500, new { message = "Failed to delete category." });
            return NoContent();
        }
    }

    public class CategoryRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string CategoryName { get; set; } = string.Empty;
    }
}
