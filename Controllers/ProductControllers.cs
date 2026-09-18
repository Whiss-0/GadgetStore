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
            [FromQuery] int? categoryId, [FromQuery] string? search, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                var results = await _productRepository.SearchAsync(search, ct);
                return Ok(results);
            }
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
                image = dto.Image,
                ram_gb = dto.RamGb,
                processor = dto.Processor,
                storage_gb = dto.StorageGb
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
            existing.ram_gb = dto.RamGb;
            existing.processor = dto.Processor;
            existing.storage_gb = dto.StorageGb;
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

        /// <summary>Update only the description. Accessible by Moderator (staff) and Admin.</summary>
        [Authorize(Policy = "ModAccess")]
        [HttpPatch("{id:int}/description")]
        public async Task<IActionResult> UpdateDescription(int id, [FromBody] DescriptionUpdateRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _productRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Product with ID {id} not found." });
            existing.description = dto.Description?.Trim();
            bool updated = await _productRepository.UpdateAsync(existing, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update description." });
            return Ok(new { message = "Description updated.", description = existing.description });
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpPost("upload-image")]
        [RequestSizeLimit(5 * 1024 * 1024)] // 5 MB cap
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file was uploaded." });

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType))
                return BadRequest(new { message = "Only JPG, PNG, or WebP images are allowed." });

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowedExtensions.Contains(extension))
                return BadRequest(new { message = "Only JPG, PNG, or WebP images are allowed." });

            if (!await IsGenuineImageAsync(file, extension, ct))
                return BadRequest(new { message = "That file doesn't look like a genuine image." });

            // Random filename — never trust or reuse the client's original filename directly on disk.
            var fileName = $"{Guid.NewGuid()}{extension}";
            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "products");
            Directory.CreateDirectory(uploadsPath); // safety net if it somehow doesn't exist yet
            var filePath = Path.Combine(uploadsPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream, ct);
            }

            var publicUrl = $"{Request.Scheme}://{Request.Host}/uploads/products/{fileName}";
            return Ok(new { url = publicUrl });
        }

        private static readonly Dictionary<string, byte[][]> ImageSignatures = new()
        {
            [".jpg"]  = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
            [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
            [".png"]  = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } },
            [".webp"] = new[] { new byte[] { 0x52, 0x49, 0x46, 0x46 } }, // "RIFF" — WebP's container format
        };

        private static async Task<bool> IsGenuineImageAsync(IFormFile file, string extension, CancellationToken ct)
        {
            if (!ImageSignatures.TryGetValue(extension, out var signatures)) return false;
            var header = new byte[8];
            await using var stream = file.OpenReadStream();
            int read = await stream.ReadAsync(header, 0, header.Length, ct);
            return signatures.Any(sig => read >= sig.Length && header.Take(sig.Length).SequenceEqual(sig));
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
        public int? RamGb { get; set; }
        public string? Processor { get; set; }
        public int? StorageGb { get; set; }
    }

    public class DescriptionUpdateRequest
    {
        public string? Description { get; set; }
    }
}
