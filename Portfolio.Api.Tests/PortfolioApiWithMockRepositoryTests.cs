
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Repositories;

namespace Portfolio.Api.Tests;

public class PortfolioApiWithMockRepositoryTests
{
    [Fact]
    public async Task GetPortfolio_WhenRepositoryReturnsNull_Returns404()
    {
        // Arrange
        var repository = new Mock<IPortfolioRepository>();

        repository
            .Setup(r => r.GetByIdAsync(
                999,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PortfolioEntity?)null);

        using var factory =
            new TestPortfolioApiFactory(repository.Object);

        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/api/portfolios/999");

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        repository.Verify(
            r => r.GetByIdAsync(
                999,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
