using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Adaminator.Api.Auth;
using Adaminator.Api.Infrastructure;
using Adaminator.Application;
using Adaminator.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfiguration) =>
    loggerConfiguration.ReadFrom.Configuration(context.Configuration));

// ---- Options ----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

// Outside development, refuse to start on the placeholders committed in appsettings.json. They are
// long enough for HMAC-SHA256 to accept, so a Fly secret that is missing, renamed or typo'd would
// otherwise boot a healthy-looking API signing admin tokens with a key that is in the public repo -
// and accepting a password that is in it too. The connection string already fails fast this way.
if (!builder.Environment.IsDevelopment())
{
    EnsureRealSecret("Jwt:Key", jwtOptions.Key);
    EnsureRealSecret("Admin:Password", builder.Configuration[$"{AdminOptions.SectionName}:Password"]);

    // HMAC-SHA256 needs a key at least as long as its output, and a short one is a weak one regardless.
    if (Encoding.UTF8.GetByteCount(jwtOptions.Key) < 32)
    {
        throw new InvalidOperationException("'Jwt:Key' must be at least 32 bytes.");
    }
}

static void EnsureRealSecret(string key, string? value)
{
    if (string.IsNullOrWhiteSpace(value) || value.StartsWith("REPLACE_ME", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"'{key}' is not configured - set it as a secret rather than leaving the placeholder.");
    }
}

// ---- Core services ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
builder.Services.AddInfrastructure(connectionString);

builder.Services.AddScoped<JwtTokenService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---- AuthN / AuthZ ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// ---- CORS ----
const string corsPolicy = "frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(options =>
    options.AddPolicy(corsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// ---- Health checks ----
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// ---- Forwarded headers ----
// Fly's edge proxy terminates the real client connection, so without this, every request would
// appear to come from the proxy's own address - defeating per-IP rate limiting below. Fly is the
// only hop in front of this app and its proxy address isn't a fixed one we can pin, so the
// X-Forwarded-For header is trusted as-is rather than restricted by KnownProxies/KnownNetworks.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---- Rate limiting ----
// The admin password is a single shared secret with no lockout, so /api/auth/login gets its own
// per-IP throttle to make brute-forcing it impractical. The limit is configurable because
// integration tests share one "connection" across dozens of logins run in seconds.
builder.Services.Configure<LoginRateLimitOptions>(builder.Configuration.GetSection(LoginRateLimitOptions.SectionName));
var loginRateLimit = builder.Configuration.GetSection(LoginRateLimitOptions.SectionName).Get<LoginRateLimitOptions>()
    ?? new LoginRateLimitOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Login, httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginRateLimit.PermitLimit,
            Window = TimeSpan.FromSeconds(loginRateLimit.WindowSeconds),
            QueueLimit = 0,
        }));

    // The Unmatched scoreboard is the one endpoint that writes to the database without a login, so it
    // is also the one an anonymous caller could hammer. Recording a result is a once-a-game action, so
    // a ceiling this far above real use costs the players nothing and keeps a loop off the database.
    options.AddPolicy(RateLimitPolicies.PublicWrite, httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// ---- Error handling ----
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ---- Swagger ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Adaminator API", Version = "v1" });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT returned by /api/auth/login.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

var app = builder.Build();

await DatabaseMigrator.MigrateAsync(app);

app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseSerilogRequestLogging();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.Append("X-Content-Type-Options", "nosniff");
    headers.Append("X-Frame-Options", "DENY");
    headers.Append("Referrer-Policy", "no-referrer");
    headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    await next();
});

// Swagger exposes the full API schema, which is free reconnaissance for an attacker; the app is
// small enough that Development-only is sufficient rather than gating it behind auth as well.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(corsPolicy);

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed so the integration test host (WebApplicationFactory<Program>) can reference the entry point.
public partial class Program;
