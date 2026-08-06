using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.DTOs;
using api.Main;
using api.UserRoleModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminAccess")]
    public class UserRoleController : ControllerBase
    {
        private readonly IUserRoleRespository _userRoleRepository;

        public UserRoleController(IUserRoleRespository userRoleRepository)
        {
            _userRoleRepository = userRoleRepository;
        }

        [HttpGet]
        public async Task<ActionResult<object>> GetAll([FromQuery] int? pageNumber, [FromQuery] int? pageSize, CancellationToken ct)
        {
            if (pageNumber.HasValue && pageSize.HasValue)
            {
                var paged = await _userRoleRepository.GetPagedAsync(pageNumber.Value, pageSize.Value, ct);
                return Ok(new PaginationModel<UserRoleResponse>
                {
                    Items = paged.Items.Select(MapToDto).ToList(),
                    TotalCount = paged.TotalCount,
                    PageSize = paged.PageSize,
                    CurrentPage = paged.CurrentPage
                });
            }
            var roles = await _userRoleRepository.GetAllAsync(ct);
            return Ok(roles.Select(MapToDto).ToList());
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<UserRoleResponse>> GetById(int id, CancellationToken ct)
        {
            var role = await _userRoleRepository.GetByIdAsync(id, ct);
            if (role == null) return NotFound(new { message = $"UserRole with ID {id} not found." });
            return Ok(MapToDto(role));
        }

        [HttpPost]
        public async Task<ActionResult<UserRoleResponse>> Create([FromBody] UpdateUserroleRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var role = new UserRole { Role_Name = dto.UserRole };
            int newId = await _userRoleRepository.CreateAsync(role, ct);
            return CreatedAtAction(nameof(GetById), new { id = newId }, MapToDto(role));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateUserroleRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _userRoleRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"UserRole with ID {id} not found." });
            existing.Role_Name = dto.UserRole;
            bool updated = await _userRoleRepository.UpdateAsync(existing, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update role." });
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var existing = await _userRoleRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"UserRole with ID {id} not found." });
            bool deleted = await _userRoleRepository.DeleteAsync(id, ct);
            if (!deleted) return StatusCode(500, new { message = "Failed to delete role." });
            return NoContent();
        }

        private static UserRoleResponse MapToDto(UserRole r) => new UserRoleResponse { Role_ID = r.Role_ID, Role_Name = r.Role_Name };
    }
}
