using System.Text;
using DotNetEnv;
using Fakebook.UploadServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;

// Load .env so JWT_* resolve when running standalone. AppHost forwards these too.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .SetIsOriginAllowed(_ => true)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Validate JWTs minted by the main API — same secret / issuer / audience.
var issuer   = builder.Configuration["JWT_ISSUER"]   ?? "fakebook";
var audience = builder.Configuration["JWT_AUDIENCE"] ?? "fakebook-clients";
var secret   = builder.Configuration["JWT_SECRET"]   ?? "dev-only-secret-change-me-32-chars-minimum-xxx";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = issuer,
        ValidAudience            = audience,
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ClockSkew                = TimeSpan.FromSeconds(30)
    });
builder.Services.AddAuthorization();

// Permit large multipart bodies (video). Per-kind limits enforced per upload.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = FileStore.MaxRequestBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = FileStore.MaxRequestBytes);

// Storage root: UPLOADS_ROOT env override, else <contentRoot>/uploads. Created on boot.
var configuredRoot = builder.Configuration["UPLOADS_ROOT"];
var root = string.IsNullOrWhiteSpace(configuredRoot)
    ? Path.Combine(builder.Environment.ContentRootPath, "uploads")
    : Path.GetFullPath(configuredRoot);
Directory.CreateDirectory(root);
builder.Services.AddSingleton(new FileStore(root));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapMedia();

app.Run();
