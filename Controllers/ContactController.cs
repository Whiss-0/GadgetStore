using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using api.ContactModule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers;

[ApiController]
[Route("api/contact")]
public sealed class ContactController : ControllerBase
{
    private static readonly HashSet<string> AllowedTopics = new(StringComparer.Ordinal)
    {
        "Product question",
        "Order issue",
        "Return or refund request",
        "Complaint",
        "Feedback",
        "Other",
    };

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        "New",
        "In Review",
        "Resolved",
    };

    private readonly IContactMessageRepository _messages;

    public ContactController(IContactMessageRepository messages)
    {
        _messages = messages;
    }

    /// <summary>Accepts a public customer message and saves it for the store's Support Inbox.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("contact-submit")]
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] ContactMessageRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Honeypot: silently accept likely bot submissions without storing them.
        if (!string.IsNullOrWhiteSpace(request.CompanyWebsite)) return NoContent();

        string name = request.Name.Trim();
        string email = request.Email.Trim();
        string topic = request.Topic.Trim();
        string message = request.Message.Trim();
        string? orderReference = string.IsNullOrWhiteSpace(request.OrderReference)
            ? null
            : request.OrderReference.Trim();

        if (name.Length == 0 || email.Length == 0 || message.Length < 10)
            return BadRequest(new { message = "Name, a reply email, and a message of at least 10 characters are required." });
        if (!AllowedTopics.Contains(topic))
            return BadRequest(new { message = "Choose one of the available contact topics." });

        int? userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int parsedUserId)
            ? parsedUserId
            : null;

        long ticketId = await _messages.CreateAsync(new ContactMessage
        {
            UserId = userId,
            Name = name,
            Email = email,
            Topic = topic,
            OrderReference = orderReference,
            Message = message,
            CreatedAt = DateTime.UtcNow,
        }, ct);

        return StatusCode(StatusCodes.Status201Created, new
        {
            ticketId,
            message = "Your message has been received by the store.",
        });
    }

    /// <summary>Lists recent customer messages for administrators.</summary>
    [Authorize(Policy = "AdminAccess")]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContactMessage>>> List([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        return Ok(await _messages.GetRecentAsync(limit, ct));
    }

    /// <summary>Updates a support message's status. Administrators only.</summary>
    [Authorize(Policy = "AdminAccess")]
    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateContactStatusRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        string status = request.Status.Trim();
        if (!AllowedStatuses.Contains(status))
            return BadRequest(new { message = "Status must be New, In Review, or Resolved." });

        bool updated = await _messages.UpdateStatusAsync(id, status, DateTime.UtcNow, ct);
        return updated ? NoContent() : NotFound(new { message = "Support message not found." });
    }
}

public sealed class ContactMessageRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string Topic { get; set; } = string.Empty;

    [StringLength(50)]
    public string? OrderReference { get; set; }

    [Required, StringLength(5000, MinimumLength = 10)]
    public string Message { get; set; } = string.Empty;

    [StringLength(120)]
    public string? CompanyWebsite { get; set; }
}

public sealed class UpdateContactStatusRequest
{
    [Required, StringLength(24)]
    public string Status { get; set; } = string.Empty;
}
