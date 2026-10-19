
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Portfolio.Infrastructure.Entities;
using Portfolio.Domain.Entities;

namespace Portfolio.Api.Tests;

public class PortfolioApiContractTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PortfolioApiContractTests(
        WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task OpenApiDocument_ContainsPortfoliosEndpoint()
    {
        var response = await _client.GetAsync(
            "/openapi/v1.json");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var paths = document.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty(
                "/api/v1/portfolios",
                out _));
    }

    [Fact]
    public async Task GetPortfolios_ReturnsExpectedResponseContract()
    {
        var response = await _client.GetAsync(
            "/api/v1/portfolios?page=1&pageSize=10");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<PagedResponse<PortfolioResponse>>();

        Assert.NotNull(result);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.True(result.TotalCount >= 0);
        Assert.NotNull(result.Items);
    }
}
