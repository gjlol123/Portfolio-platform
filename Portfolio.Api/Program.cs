using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Api.Configuration;
using Portfolio.Api.Contracts;
using Portfolio.Application.Abstractions;
using Portfolio.Application.Services;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Background;
using Portfolio.Infrastructure.Messaging;
using Portfolio.Infrastructure.Outbox;
using Portfolio.Infrastructure.Persistence;
using Portfolio.Infrastructure.Repositories;
using Portfolio.Infrastructure.Inbox;


var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();

Console.WriteLine(
    $"Environment: {builder.Environment.EnvironmentName}");

Console.WriteLine(
    $"ContentRoot: {builder.Environment.ContentRootPath}");

Console.WriteLine(
    $"AllowedCurrencies from config: " +
    $"{builder.Configuration["Portfolio:AllowedCurrencies:0"]}");

builder.Services.AddScoped<PortfolioValuationConsumer>();
builder.Services.AddScoped<InboxProcessor>();

builder.Services.AddScoped<IOutboxWriter, EfOutboxWriter>();

builder.Services.AddScoped<OutboxClaimService>();
builder.Services.AddSingleton<
    IEventPublisher,
    LoggingEventPublisher>();


builder.Services.AddHostedService<OutboxWorker>();

builder.Services.AddSingleton<PortfolioValuationDeliveryHandler>();

builder.Services
    .AddOptions<PortfolioOptions>()
    .BindConfiguration("Portfolio")
    .Validate(
        options =>
            options.AllowedCurrencies is { Length: > 0 }
            && options.AllowedCurrencies.All(
                currency => !string.IsNullOrWhiteSpace(currency)),
        "At least one valid currency must be configured.")
    .Validate(
        options => options.MaxNameLength is > 0 and <= 200,
        "MaxNameLength must be between 1 and 200.")
    .ValidateOnStart();


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<PortfolioDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("PortfolioDb")));

builder.Services.AddScoped<IPortfolioRepository, EfPortfolioRepository>();


builder.Services.AddScoped<IUnitOfWork>(sp =>
sp.GetRequiredService<PortfolioDbContext>());

builder.Services.AddScoped<IPortfolioService, PortfolioService>();

builder.Services.AddSingleton(TimeProvider.System);


var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Portfolio API v1");

        options.RoutePrefix = "swagger";
    });
}

app.MapPut(
    "/api/v1/portfolios/{id:int}/name",
    async (
        int id,
        RenamePortfolioRequest request,
        IPortfolioService portfolioService,
        CancellationToken cancellationToken) =>
    {
        await portfolioService.RenameAsync(
            id,
            request.Name,
            cancellationToken);

        return Results.NoContent();
    });

app.MapGet("/api/v1/portfolios",
    async (
        int? page,
        int? pageSize,
        string? currency,
        IPortfolioService service,
        IOptions<PortfolioOptions> options,
        CancellationToken cancellationToken) =>
    {
        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 10;

        var errors = new Dictionary<string, string[]>();

        if (currentPage < 1)
        {
            errors["page"] =
                ["Page must be greater than or equal to 1."];
        }

        if (currentPageSize < 1 || currentPageSize > 100)
        {
            errors["pageSize"] =
                ["PageSize must be between 1 and 100."];
        }

        if (currency is not null)
        {
            var allowedCurrencies = options.Value.AllowedCurrencies;

            if (!allowedCurrencies.Contains(
                currency,
                StringComparer.OrdinalIgnoreCase))
            {
                errors["currency"] =
                    ["Currency is not supported."];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var portfolios = await service.GetAllAsync(
            cancellationToken);

        IEnumerable<PortfolioEntity> filtered = portfolios;

        if (!string.IsNullOrWhiteSpace(currency))
        {
            filtered = filtered.Where(p =>
                string.Equals(
                    p.BaseCurrency,
                    currency,
                    StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered
            .OrderBy(p => p.Id)
            .ToList();

        var totalCount = ordered.Count;

        var items = ordered
            .Skip((currentPage - 1) * currentPageSize)
            .Take(currentPageSize)
            .Select(p => new PortfolioResponse(
                p.Id,
                p.Name,
                p.BaseCurrency,
                p.MarketValue))
            .ToArray();

        var totalPages = (int)Math.Ceiling(
            totalCount / (double)currentPageSize);

        return Results.Ok(
       new PagedResponse<PortfolioResponse>(
           items,
           currentPage,
           currentPageSize,
           totalCount,
           totalPages));
    })
    .WithName("GetPortfoliosV1")
    .WithSummary("Get portfolios")
    .WithDescription(
        "Returns a paginated list of portfolios. " +
        "Optionally filters results by base currency.")
    .Produces<PagedResponse<PortfolioResponse>>(
        StatusCodes.Status200OK)
    .ProducesValidationProblem(
        StatusCodes.Status400BadRequest);

   


app.MapGet("/api/portfolios/{id:int}",
    async (
        int id,
          HttpContext httpContext,
        IPortfolioService service,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var traceId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "Retrieving portfolio {PortfolioId}. TraceId: {TraceId}",
            id,
            traceId);

        var portfolio = await service.GetByIdAsync(
            id,
            cancellationToken);

        if (portfolio is null)
        {
            logger.LogWarning(
             "Portfolio {PortfolioId} was not found",
             id);

            return Results.Problem(
                title: "Portfolio not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        logger.LogInformation(
       "Successfully retrieved portfolio {PortfolioId}",
       id);


        var response = new PortfolioResponse(
            portfolio.Id,
            portfolio.Name,
            portfolio.BaseCurrency,
            portfolio.MarketValue);

        return Results.Ok(response);
    });


app.MapPost("/api/portfolios", async (
    CreatePortfolioRequest request,
    IPortfolioService service,
     IOptions<PortfolioOptions> options,
    CancellationToken cancellationToken) =>
{
   

    var currency = request.BaseCurrency?.Trim()
         .ToUpperInvariant();

    var portfolioOptions = options.Value;
    var allowedCurrencies = portfolioOptions.AllowedCurrencies;
    var maxNameLength = portfolioOptions.MaxNameLength;

    var errors = new Dictionary<String, string[]>();

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        errors["Name"] = new[]
        {
            "Name is required."
        };
    }
    else if (request.Name.Trim().Length > maxNameLength)
    {
        errors["Name"] = new[]
      {
            "Name is 100."
        };
    }

    if (string.IsNullOrWhiteSpace(currency) ||
        !allowedCurrencies.Contains(currency))
    {
        errors["BaseCurrency"] = new[]
        {
                "Currency must be SEK, EUR, or USD."
            };
    }


    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    try
    {
        var portfolio = await service.CreateAsync(
            new CreatePortfolioRequest(
                request.Name.Trim(),
                currency!),
            cancellationToken);

        var response = new PortfolioResponse(
          portfolio.Id,
          portfolio.Name,
          portfolio.BaseCurrency,
          portfolio.MarketValue);

        return Results.Created(
            $"/api/portfolios/{response.Id}",
            response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});


app.MapGet("/health",() =>
{
    return Results.Ok(new
    {
        status ="Healthy",
        service = "Portoflio.Api"
    });
});


app.Run();

public partial class Program { }