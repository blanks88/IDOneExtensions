using FluentValidation;
using MediatR;
using Synchroteam.Domain;
using Synchroteam.Infrastructure;
using Synchroteam.Middleware.Behaviors;
using Synchroteam.Middleware.Handlers;

namespace Synchroteam.Middleware;

internal static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Ensure configuration sources are added (defaults already include these; adding explicitly for clarity)
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json",
                reloadOnChange: true,
                optional: true
            )
            .AddEnvironmentVariables();

        // Bind configuration from environment variables or appsettings
        // Required: SYNCHROTEAM_BASE_URL, SYNCHROTEAM_API_KEY
        builder.Services.AddSingleton<ISynchroteamClient>(sp =>
        {
            var cfg = builder.Configuration;
            var baseUrl = cfg["SYNCHROTEAM_BASE_URL"] ?? cfg["Synchroteam:BaseUrl"];
            var apiKey = cfg["SYNCHROTEAM_API_KEY"] ?? cfg["Synchroteam:ApiKey"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("SYNCHROTEAM_BASE_URL (or Synchroteam.Infrastructure:BaseUrl) is not configured");
            }

            return new SynchroteamClient(new SynchroteamClientOptions
            {
                BaseAddress = new Uri(baseUrl),
                ApiKey = apiKey
            });
        });

        // CQRS + Validation
        builder.Services.AddMediatR(typeof(DomainEntryPoint).Assembly);
        builder.Services.AddValidatorsFromAssemblyContaining<DomainEntryPoint>();
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        builder.Services.AddEndpointsApiExplorer();

        var app = builder.Build();

        // Global validation handling -> 400 Bad Request
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (ValidationException ex)
            {
                var errors = ex.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "Validation failed", details = errors });
            }
        });

        // Log configuration status at startup (non-sensitive)
        var configuredBaseUrl = builder.Configuration["SYNCHROTEAM_BASE_URL"] ?? builder.Configuration["Synchroteam.Infrastructure:BaseUrl"];
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            app.Logger.LogWarning(
                "Synchroteam.Infrastructure BaseUrl is not configured. Set SYNCHROTEAM_BASE_URL or Synchroteam.Infrastructure:BaseUrl in appsettings.json.");
        }
        else
        {
            app.Logger.LogInformation("Synchroteam.Infrastructure BaseUrl configured: {BaseUrl}", configuredBaseUrl);
        }

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // Customer endpoints:
        app.MapPost("/sync-to-st/customer", SyncCustomerToSynchroteamHandler.HandleAsync)
            .WithName("SyncCustomerToSynchroteam")
            .Produces<CustomerDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status500InternalServerError);

        app.Run();
    }
}