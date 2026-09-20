using System.Data.Common;
using System.Text;
using api.Main;

namespace api.ActivityModule;

public sealed class ActivityRepository : BaseRepository, IActivityRepository
{
    private const int MaxSafeLimit = 500;

    public ActivityRepository(MyCon db) : base(db) { }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS activity_logs (
                activity_id      INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id          INTEGER NULL,
                actor_user_id    INTEGER NULL,
                actor_role       TEXT    NOT NULL,
                activity_type    TEXT    NOT NULL,
                description      TEXT    NOT NULL,
                related_order_id   INTEGER NULL,
                related_product_id INTEGER NULL,
                created_at       TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_activity_created_at
                ON activity_logs(created_at DESC);

            CREATE INDEX IF NOT EXISTS idx_activity_user_id
                ON activity_logs(user_id);

            CREATE INDEX IF NOT EXISTS idx_activity_actor_user_id
                ON activity_logs(actor_user_id);
            """;

        await ExecuteNonQueryAsync(sql, ct: ct).ConfigureAwait(false);
    }

    public async Task<long> CreateAsync(ActivityLog activity, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO activity_logs
                (user_id, actor_user_id, actor_role, activity_type, description,
                 related_order_id, related_product_id, created_at)
            VALUES
                (@user_id, @actor_user_id, @actor_role, @activity_type, @description,
                 @related_order_id, @related_product_id, @created_at);
            SELECT last_insert_rowid();
            """;

        var parameters = new[]
        {
            CreateParameter("@user_id",            activity.User_ID),
            CreateParameter("@actor_user_id",      activity.Actor_User_ID),
            CreateParameter("@actor_role",         activity.Actor_Role),
            CreateParameter("@activity_type",      activity.Activity_Type),
            CreateParameter("@description",        activity.Description),
            CreateParameter("@related_order_id",   activity.Related_Order_ID),
            CreateParameter("@related_product_id", activity.Related_Product_ID),
            CreateParameter("@created_at",         activity.Created_At.ToString("o")),
        };

        var result = await ExecuteScalarAsync<long>(sql, parameters, ct: ct).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<ActivityLog>> GetForUserAsync(
        int userId,
        int? limit = null,
        CancellationToken ct = default)
    {
        int cap = Math.Min(limit ?? MaxSafeLimit, MaxSafeLimit);

        const string sql = """
            SELECT activity_id, user_id, actor_user_id, actor_role, activity_type,
                   description, related_order_id, related_product_id, created_at
            FROM activity_logs
            WHERE user_id = @user_id
            ORDER BY created_at DESC
            LIMIT @limit;
            """;

        var parameters = new[]
        {
            CreateParameter("@user_id", userId),
            CreateParameter("@limit",   cap),
        };

        var rows = await ExecuteReaderToListAsync(sql, MapRow, parameters, ct: ct).ConfigureAwait(false);
        return rows.AsReadOnly();
    }

    public async Task<IReadOnlyList<ActivityLog>> GetAllAsync(
        DateTime? from,
        DateTime? to,
        string? type,
        int? userId,
        string? actorRole = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        int cap = Math.Min(limit ?? MaxSafeLimit, MaxSafeLimit);

        var sb = new StringBuilder("""
            SELECT activity_id, user_id, actor_user_id, actor_role, activity_type,
                   description, related_order_id, related_product_id, created_at
            FROM activity_logs
            WHERE 1=1
            """);

        var parameters = new List<DbParameter>();

        if (from.HasValue)
        {
            sb.Append(" AND created_at >= @from");
            parameters.Add(CreateParameter("@from", from.Value.ToString("o")));
        }
        if (to.HasValue)
        {
            sb.Append(" AND created_at <= @to");
            parameters.Add(CreateParameter("@to", to.Value.ToString("o")));
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            sb.Append(" AND activity_type = @type");
            parameters.Add(CreateParameter("@type", type));
        }
        if (userId.HasValue)
        {
            sb.Append(" AND user_id = @uid");
            parameters.Add(CreateParameter("@uid", userId.Value));
        }
        if (!string.IsNullOrWhiteSpace(actorRole))
        {
            sb.Append(" AND actor_role = @actor_role");
            parameters.Add(CreateParameter("@actor_role", actorRole));
        }

        sb.Append(" ORDER BY created_at DESC LIMIT @limit;");
        parameters.Add(CreateParameter("@limit", cap));

        var rows = await ExecuteReaderToListAsync(sb.ToString(), MapRow, parameters, ct: ct).ConfigureAwait(false);
        return rows.AsReadOnly();
    }

    private static ActivityLog MapRow(DbDataReader r) => new()
    {
        Activity_ID        = ReadValue(r, "activity_id",        0L),
        User_ID            = r.IsDBNull(r.GetOrdinal("user_id"))            ? null : ReadValue(r, "user_id",            0),
        Actor_User_ID      = r.IsDBNull(r.GetOrdinal("actor_user_id"))      ? null : ReadValue(r, "actor_user_id",      0),
        Actor_Role         = ReadValue(r, "actor_role",         "System"),
        Activity_Type      = ReadValue(r, "activity_type",      string.Empty),
        Description        = ReadValue(r, "description",        string.Empty),
        Related_Order_ID   = r.IsDBNull(r.GetOrdinal("related_order_id"))   ? null : ReadValue(r, "related_order_id",   0),
        Related_Product_ID = r.IsDBNull(r.GetOrdinal("related_product_id")) ? null : ReadValue(r, "related_product_id", 0),
        Created_At         = DateTime.Parse(ReadValue(r, "created_at", DateTime.UtcNow.ToString("o")),
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 System.Globalization.DateTimeStyles.RoundtripKind),
    };
}
