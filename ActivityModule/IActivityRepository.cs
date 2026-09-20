namespace api.ActivityModule;

public interface IActivityRepository
{
    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task<long> CreateAsync(ActivityLog activity, CancellationToken ct = default);

    Task<IReadOnlyList<ActivityLog>> GetForUserAsync(
        int userId,
        int? limit = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ActivityLog>> GetAllAsync(
        DateTime? from,
        DateTime? to,
        string? type,
        int? userId,
        string? actorRole = null,
        int? limit = null,
        CancellationToken ct = default);
}
