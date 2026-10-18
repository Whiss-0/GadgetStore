namespace api.ContactModule;

public interface IContactMessageRepository
{
    Task<long> CreateAsync(ContactMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ContactMessage>> GetRecentAsync(int limit = 100, CancellationToken ct = default);
    Task<bool> UpdateStatusAsync(long messageId, string status, DateTime updatedAt, CancellationToken ct = default);
}
