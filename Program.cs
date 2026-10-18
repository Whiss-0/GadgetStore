using api.Main;
using api.Security;
using api.DTOs;
using api.UserModule;
using api.UserRoleModule;
using api.ProductsModule;
using api.CategoriesModule;
using api.CartModule;
using api.OrderModule;
using api.OrderDetailModule;
using api.ReviewModule;
using api.WishlistModule;
using api.Services;
using api.ActivityModule;
using api.NotificationModule;
using api.ContactModule;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(ConnEnvFile.LoadConfigurationValues());

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new RegisterRequestJsonConverter());
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("nominatim", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "GadgetStoreAPI/1.0 (+https://github.com/Whiss-0/GadgetStore)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "GadgetSystem API",
        Description = "API for GadgetSystem - connected to Gadgetdb.db"
    });

    // Add JWT Bearer authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = @"JWT Authorization header using the Bearer scheme.
                      Enter 'Bearer' [space] and then your token in the text input below.
                      Example: 'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Extensions =
    {
        ["x-example"] = new OpenApiString(
            "Bearer A1B2C3D4E5F6G7H8I9J0K1L2M3N4O5P6Q7R8S9T0U1V2W3X4Y5Z6A7B8C"
        )
    }
    });

    // Fix: IFormFile with [FromForm] requires an explicit OperationFilter in Swashbuckle 9.x
    c.OperationFilter<FormFileOperationFilter>();

    // Fix: Swashbuckle 9.x cannot introspect IFormFile directly — map it to a binary schema
    c.MapType<IFormFile>(() => new OpenApiSchema { Type = "string", Format = "binary" });
    c.MapType<IFormFileCollection>(() => new OpenApiSchema
    {
        Type = "array",
        Items = new OpenApiSchema { Type = "string", Format = "binary" }
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configure JWT Authentication
string jwtKey = JwtConfiguration.ResolveSigningKey(builder.Configuration, builder.Environment);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? JwtConfiguration.DefaultIssuer,
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? JwtConfiguration.DefaultAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

// Configure Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminAccess", policy => policy.RequireClaim("user_role_id", "1"));
    options.AddPolicy("ModAccess", policy => policy.RequireClaim("user_role_id", new[] { "1", "2" }));
    options.AddPolicy("UserAccess", policy => policy.RequireClaim("user_role_id", new[] { "1", "2", "3" }));
});

// Register MyCon (database connection) - connected to Gadgetdb.db
builder.Services.AddScoped<MyCon>();

// Register all repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.Services.AddScoped<IUserRoleRespository, UserRoleRespository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICategoriesRepository, CategoriesRepository>();
builder.Services.AddScoped<ICartRespository, CartRespository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderDetailRepository, OrderDetailRepository>();
builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
builder.Services.AddScoped<IWishlistRepository, WishlistRepository>();
builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IContactMessageRepository, ContactMessageRepository>();

// Register JWT Token Service
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// Register Email Service
builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddScoped<SmtpOtpEmailSender>();
builder.Services.AddScoped<IOtpEmailSender>(sp =>
    builder.Environment.IsDevelopment()
        ? sp.GetRequiredService<DevOtpEmailSender>()
        : sp.GetRequiredService<SmtpOtpEmailSender>());
builder.Services.AddScoped<DevOtpEmailSender>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IOrderEmailSender, SmtpOrderEmailSender>();

var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        if (configuredCorsOrigins.Length == 0)
        {
            // In development allow all origins; fail closed in production
            if (builder.Environment.IsDevelopment())
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            }
            else
            {
                policy.SetIsOriginAllowed(_ => false).AllowAnyMethod().AllowAnyHeader();
            }
            return;
        }

        policy.WithOrigins(configuredCorsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 5;                // 5 attempts
        opt.Window = TimeSpan.FromMinutes(1); // per minute
        opt.QueueLimit = 0;                  // no queueing — reject immediately over the limit
    });

    options.AddFixedWindowLimiter("otp-verify", opt =>
    {
        opt.PermitLimit = 3;               // codes are only 6 digits — keep this tight
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.AddPolicy("contact-submit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

var app = builder.Build();

// Ensure activity_logs table exists
using (var activityScope = app.Services.CreateScope())
{
    var activityRepository = activityScope.ServiceProvider
        .GetRequiredService<IActivityRepository>();
    await activityRepository.EnsureSchemaAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint($"/swagger/v1/swagger.json", "GadgetSystem API V1");
        options.RoutePrefix = string.Empty;
    });
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors("ConfiguredOrigins");
app.UseAuthentication();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'; script-src 'self'; object-src 'none';");
    await next();
});

app.UseStaticFiles(); // serves files from wwwroot/ — placed after the security-headers middleware so uploaded images get those headers too

app.UseAuthorization();
app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Database connection test endpoint
app.MapGet("/db-test", async (MyCon db) =>
{
    try
    {
        var canConnect = await db.CanConnectAsync();
        if (canConnect)
        {
            return Results.Ok(new
            {
                status = "Database connection successful",
                database = "Gadgetdb.db",
                timestamp = DateTime.UtcNow
            });
        }
        else
        {
            return Results.Problem("Database connection failed");
        }
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Database connection error"
        );
    }
});

// Development-only schema inspection endpoint
app.MapGet("/dev/schema", async (MyCon db) =>
{
    if (!app.Environment.IsDevelopment()) return Results.NotFound();
    try
    {
        await using var conn = db.GetConnection();
        await conn.OpenAsync();
        var result = new Dictionary<string, List<object>>();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        var reader = await cmd.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        reader.Close();

        foreach (var t in tables)
        {
            var cmd2 = conn.CreateCommand();
            cmd2.CommandText = $"PRAGMA table_info(\"{t}\");";
            var r2 = await cmd2.ExecuteReaderAsync();
            var cols = new List<object>();
            while (await r2.ReadAsync())
            {
                cols.Add(new { cid = r2["cid"], name = r2["name"]?.ToString(), type = r2["type"]?.ToString(), pk = r2["pk"] });
            }
            r2.Close();
            result[t] = cols;
        }
        conn.Close();
        return Results.Ok(result);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.Run();

public partial class Program { }

/// <summary>
/// Rewrites IFormFile / IFormFileCollection parameters into a proper
/// multipart/form-data request body so Swashbuckle 9.x can generate
/// swagger.json without throwing an InternalServerError.
/// </summary>
internal sealed class FormFileOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var formFileParams = context.ApiDescription.ActionDescriptor.Parameters
            .Where(p => p.ParameterType == typeof(IFormFile) || p.ParameterType == typeof(IFormFileCollection))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (formFileParams.Count == 0) return;

        // Remove any auto-detected parameters that Swashbuckle couldn't resolve
        operation.Parameters = operation.Parameters
            .Where(p => !formFileParams.Contains(p.Name))
            .ToList();

        var properties = new Dictionary<string, OpenApiSchema>();
        foreach (var name in formFileParams)
        {
            properties[name] = new OpenApiSchema
            {
                Type = "string",
                Format = "binary",
                Description = "File to upload"
            };
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content =
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = properties
                    }
                }
            }
        };
    }
}
