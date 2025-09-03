using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using Play929Backend.Data;
using Play929Backend.Services.Implementations;
using Play929Backend.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using HealthChecks.NpgSql; 
using Play929Backend.Models;
using Play929Backend.DTOs;
using Microsoft.AspNetCore.Mvc;
using Play929Backend.Background;
using HealthChecks.Redis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;




var builder = WebApplication.CreateBuilder(args);


var securitySettings = builder.Configuration.GetSection("Security");
var jwtSettings = builder.Configuration.GetSection("Jwt");



builder.Services.AddCors(options =>
{
    options.AddPolicy("RestrictedCors", policy =>
    {
        policy.WithOrigins("https://my.play929.com" , "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});


var redisConnection = builder.Configuration.GetConnectionString("Redis");
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
    options.InstanceName = "Play929_";
});


var redis = ConnectionMultiplexer.Connect(redisConnection);
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys")
    .SetApplicationName("Play929")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));


builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameTimerService>();
builder.Services.AddSingleton<GameWordService>();
builder.Services.AddScoped<IEmailService, EmailService>();
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
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["Secret"])),
        ClockSkew = TimeSpan.Zero 
    };

    
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});


builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("UserEmail", policy => 
        policy.RequireClaim("Email", "true"));
    
    options.AddPolicy("AdminOnly", policy => 
        policy.RequireRole("Admin"));
});


builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    
    options.AddPolicy("PerIp", context => 
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "global",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 100,
                TokensPerPeriod = 20,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                AutoReplenishment = true
            }));
    
    options.AddPolicy("Authentication", context => 
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "global",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6
            }));

            options.AddPolicy("5PerMinute", context => 
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "global",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true
            }));
});


builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-CSRF";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.FormFieldName = "CSRFField";
});


// Load connection string from appsettings
var dbConnection = builder.Configuration.GetConnectionString("DefaultConnection");


dbConnection = dbConnection
    .Replace("__DB_HOST__", Environment.GetEnvironmentVariable("DB_HOST")!)
    .Replace("__DB_PORT__", Environment.GetEnvironmentVariable("DB_PORT")!)
    .Replace("__DB_NAME__", Environment.GetEnvironmentVariable("DB_NAME")!)
    .Replace("__DB_USER__", Environment.GetEnvironmentVariable("DB_USER")!)
    .Replace("__DB_PASSWORD__", Environment.GetEnvironmentVariable("DB_PASSWORD")!);

    

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(dbConnection, sqlOptions =>
    {
        // sqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
    });
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});



builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<ISecurityLogService, SecurityLogService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddHostedService<BalanceBatchService>();



builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnection, name: "Database")
    .AddRedis(redisConnection, name: "Redis")
    .AddDbContextCheck<AppDbContext>();


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Play929 API", Version = "v1" });
    
   
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});


builder.Services.Configure<BackgroundTaskQueueOptions>(builder.Configuration.GetSection("BackgroundTasks"));
builder.Services.AddSingleton<BackgroundTaskQueueOptions>();

builder.Services.AddMemoryCache();



var app = builder.Build();

app.UseCors("RestrictedCors");

var env = app.Services.GetRequiredService<IWebHostEnvironment>();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    var csp = @"
    default-src 'self';
    script-src 'self' 'unsafe-inline' https://code.jquery.com https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.tailwindcss.com;
    style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdn.tailwindcss.com;
    connect-src 'self' https://api.play929.com;
    img-src 'self' data:;
    font-src 'self';
    ";

    context.Response.Headers.Add("Content-Security-Policy", csp.Replace("\r\n", ""));
    await next();
});


app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});



if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}


if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => 
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Play929 API v1");
        c.OAuthClientId("swagger-ui");
        c.OAuthAppName("Swagger UI");
    });
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(env.WebRootPath, "word-search")),
    RequestPath = "/game/word-search"
});


app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(env.WebRootPath, "CupGame")), 
    RequestPath = "/game/cup-game",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.PhysicalPath;
        if (path.EndsWith(".css"))
            ctx.Context.Response.ContentType = "text/css";
        else if (path.EndsWith(".js"))
            ctx.Context.Response.ContentType = "application/javascript";
    }
});



app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapHub<Play929Backend.Hubs.GameHub>("/gamehub");
    endpoints.MapHub<Play929Backend.Hubs.CupHub>("/cuphub");
});


app.UseStaticFiles();

/*

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    
   
    //await SeedAdminUser(db);
}

*/


app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

// ----------------------------------
// Helper Methods
// ----------------------------------
/*
async Task SeedAdminUser(AppDbContext context)
{
    if (await context.Users.AnyAsync(u => u.Email == "admin@play929.com")) 
        return;

    var adminUser = new User
    {
        FullNames = "System Admin",
        Surname = "Play929",
        Email = "admin@play929.com",
        PhoneNumber = "+27781045677",
        IdNumber = "0412095823081",
        AccountNumber = "ADMIN001",
        Role = "Admin",
        PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(
        Guid.NewGuid().ToString() + DateTime.UtcNow.Ticks.ToString()),
        IsEmailVerified = true,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        SecurityStamp = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    };

    context.Users.Add(adminUser);
    await context.SaveChangesAsync();
}
*/