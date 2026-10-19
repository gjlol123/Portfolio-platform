
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Repositories;
using System.Net;
using System.Net.Http.Json;

namespace Portfolio.Api.Tests;

public class PortfolioApiIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PortfolioApiIntegrationTests(
        WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }


    [Fact]
    public async Task GetPortfolios_WithPageSizeOne_ReturnsOneItem()
    {
        using var factory =
            new WebApplicationFactory<Program>();

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/portfolios?page=1&pageSize=1");

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<PagedResponse<PortfolioResponse>>();

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public async Task GetPortfolios_WithEurFilter_ReturnsOnlyEur()
    {
        using var factory =
            new WebApplicationFactory<Program>();

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/portfolios?currency=EUR");

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<PagedResponse<PortfolioResponse>>();

        Assert.NotNull(result);
        Assert.All(
            result.Items,
            item => Assert.Equal("EUR", item.BaseCurrency));

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetPortfolios_WithInvalidPage_Returns400()
    {
        using var factory =
            new WebApplicationFactory<Program>();

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/portfolios?page=0");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }



    [Fact]
    public async Task GetPortfolio_WhenRepositoryFails_Returns500()
    {
        var repository = new Mock<IPortfolioRepository>();

        repository
            .Setup(r => r.GetByIdAsync(
                1,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new InvalidOperationException("Database unavailable"));

        using var factory =
            new TestPortfolioApiFactory(repository.Object)
                .WithWebHostBuilder(builder =>
                    builder.UseEnvironment("Production"));

        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/problem+json");

        var response = await client.GetAsync(
            "/api/portfolios/1");

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Database unavailable", body);
    }

    [Fact]
    public async Task GetPortfolio_WhenMissing_ReturnsProblemDetails()
    {
        using var factory =
            new WebApplicationFactory<Program>();

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/portfolios/999");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content
            .ReadFromJsonAsync<
                Microsoft.AspNetCore.Mvc.ProblemDetails>();

        Assert.NotNull(body);
        Assert.Equal(404, body.Status);
        Assert.Equal("Portfolio not found.", body.Title);
    }


    [Fact]
    public async Task GetPortfolio_WhenIdExists_Returns200()
    {
        // Arrange
        const int id = 1;

        // Act
        var response = await _client.GetAsync(
            $"/api/portfolios/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var portfolio =
            await response.Content.ReadFromJsonAsync<PortfolioResponse>();

        Assert.NotNull(portfolio);
        Assert.Equal(1, portfolio.Id);
        Assert.Equal("Global Equity", portfolio.Name);
        Assert.Equal("EUR", portfolio.BaseCurrency);
    }

    [Fact]
    public async Task GetPortfolio_WhenIdDoesNotExist_Returns404()
    {
        // Arrange
        const int id = 999;

        // Act
        var response = await _client.GetAsync(
            $"/api/portfolios/{id}");

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task CreatePortfolio_WhenCurrencyIsInvalid_Returns400()
    {
        // Arrange
        var request = new CreatePortfolioRequest(
            "Test Portfolio",
            "NOK");

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/portfolios",
            request);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }
}
