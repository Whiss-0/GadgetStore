using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.Main;
using api.ProductsModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductController : ControllerBase
    {
        private readonly IProductRepository _productRepository;

        public ProductController(IProductRepository productRepository)
        {
            _productRepository = productRepository;
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<ActionResult<object>> GetAll(
            [FromQuery] int? pageNumber, [FromQuery] int? pageSize,
            [FromQuery] int? categoryId, CancellationToken ct)
        {
            if (categoryId.HasValue)
            {
                var byCategory = await _productRepository.GetByCategoryAsync(categoryId.Value, ct);
                return Ok(byCategory);
            }
            if (pageNumber.HasValue && pageSize.HasValue)
            {
                var paged = await _productRepository.GetPagedAsync(pageNumber.Value, pageSize.Value, ct);
                return Ok(paged);
            }
            var products = await _productRepository.GetAllAsync(ct);
            return Ok(products);
        }

        [AllowAnonymous]
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Product>> GetById(int id, CancellationToken ct)
        {
            var product = await _productRepository.GetByIdAsync(id, ct);
            if (product == null) return NotFound(new { message = $"Product with ID {id} not found." });
            return Ok(product);
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpPost]
        public async Task<ActionResult<Product>> Create([FromBody] ProductRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var product = new Product
            {
                product_name = dto.ProductName,
                brand = dto.Brand,
                description = dto.Description,
                price = dto.Price,
                stock = dto.Stock,
                category_id = dto.CategoryId,
                image = dto.Image
            };
            int newId = await _productRepository.CreateAsync(product, ct);
            return CreatedAtAction(nameof(GetById), new { id = newId }, product);
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProductRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _productRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Product with ID {id} not found." });
            existing.product_name = dto.ProductName;
            existing.brand = dto.Brand;
            existing.description = dto.Description;
            existing.price = dto.Price;
            existing.stock = dto.Stock;
            existing.category_id = dto.CategoryId;
            existing.image = dto.Image;
            bool updated = await _productRepository.UpdateAsync(existing, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update product." });
            return NoContent();
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var existing = await _productRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Product with ID {id} not found." });
            bool deleted = await _productRepository.DeleteAsync(id, ct);
            if (!deleted) return StatusCode(500, new { message = "Failed to delete product." });
            return NoContent();
        }
    }

    public class ProductRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string ProductName { get; set; } = string.Empty;
        public string? Brand { get; set; }
        public string? Description { get; set; }
        [System.ComponentModel.DataAnnotations.Range(0, double.MaxValue)]
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public int? CategoryId { get; set; }
        public string? Image { get; set; }
    }
}
