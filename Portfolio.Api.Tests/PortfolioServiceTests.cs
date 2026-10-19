using Moq;
using Portfolio.Application.Abstractions;
using Portfolio.Application.Services;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Outbox;
using Portfolio.Infrastructure.Repositories;
using Xunit;

namespace Portfolio.Api.Tests
{
    public class PortfolioServiceTests
    {


        //[Fact]
        //public async Task GetByIdAsync_WhenRepositoryIsCancelled_PropagatesCancellation()
        //{
        //    var repository = new Mock<IPortfolioRepository>();

        //    repository
        //        .Setup(r => r.GetByIdAsync(
        //            1, It.IsAny<CancellationToken>()))
        //        .ThrowsAsync(new OperationCanceledException());

        //    var service = new PortfolioService(repository.Object);

        //    using var cts = new CancellationTokenSource();

        //    await Assert.ThrowsAsync<OperationCanceledException>(
        //        () => service.GetByIdAsync(1, cts.Token));
        //}

        [Fact]
        public async Task GetByIdAsync_WhenRepositoryIsCancelled_PropagatesCancellation()
        {
            var repository = new Mock<IPortfolioRepository>();
            var outboxWriter = new Mock<IOutboxWriter>();
            var unitOfWork = new Mock<IUnitOfWork>();

            using var cts = new CancellationTokenSource();

            repository
                .Setup(r => r.GetByIdAsync(
                    1, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException(cts.Token));

            var service = new PortfolioService(
                repository.Object,
                outboxWriter.Object,
                unitOfWork.Object,
                TimeProvider.System);

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.GetByIdAsync(1, cts.Token));
        }


        [Fact]
        public async Task GetByIdAsync_PassesCancellationTokenToRepository()
        {
            // Arrange
            var repository = new Mock<IPortfolioRepository>();
            var outboxWriter = new Mock<IOutboxWriter>();
            var unitOfWork = new Mock<IUnitOfWork>();

            using var cts = new CancellationTokenSource();

            repository
                .Setup(r => r.GetByIdAsync(
                    1, It.IsAny<CancellationToken>()))
                .ReturnsAsync((PortfolioEntity?)null);


            var service = new PortfolioService(
                repository.Object,
                outboxWriter.Object,
                unitOfWork.Object,
                TimeProvider.System);


            // Act
            await service.GetByIdAsync(1, cts.Token);

            // Assert
            repository.Verify(
                r => r.GetByIdAsync(
                    1,
                    It.Is<CancellationToken>(
                        token => token == cts.Token)),
                Times.Once);
        }



        [Fact]
        public async Task GetByIdAsync_WhenRepositoryThrows_PropagatesException()
        {
            // Arrange
            var repository = new Mock<IPortfolioRepository>();
            var outboxWriter = new Mock<IOutboxWriter>();
            var unitOfWork = new Mock<IUnitOfWork>();


            repository
                .Setup(r => r.GetByIdAsync(
                    1, It.IsAny<CancellationToken>()))
                .ThrowsAsync(
                    new InvalidOperationException("Database unavailable"));


            var service = new PortfolioService(
                repository.Object,
                outboxWriter.Object,
                unitOfWork.Object,
                TimeProvider.System);

            using var cts = new CancellationTokenSource();

            // Act + Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetByIdAsync(1, cts.Token));

            Assert.Equal("Database unavailable", exception.Message);
        }



        [Fact]
        public async Task GetByIdAsync_WhenPortfolioExists_ReturnsPortfolio()
        {
            // Arrange
            var repository = new Mock<IPortfolioRepository>();
            var outboxWriter = new Mock<IOutboxWriter>();
            var unitOfWork = new Mock<IUnitOfWork>();

            var expected = new PortfolioEntity(
                1, "Global Equity", "EUR", 125000.50m);


            repository
                .Setup(r => r.GetByIdAsync(
                    1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            var service = new PortfolioService(
            repository.Object,
            outboxWriter.Object,
            unitOfWork.Object,
            TimeProvider.System);

            using var cts = new CancellationTokenSource();

            // Act
            var result = await service.GetByIdAsync(1, cts.Token);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Global Equity", result.Name);
            Assert.Equal("EUR", result.BaseCurrency);

            repository.Verify(
                r => r.GetByIdAsync(
                    1, It.IsAny<CancellationToken>()),
                Times.Once);
        }


        [Fact]
        public async Task GetByIdAsync_WhenPortfolioDoesNotExist_ReturnsNull()
        {
            // Arrange
            var repository = new Mock<IPortfolioRepository>();
            var outboxWriter = new Mock<IOutboxWriter>();
            var unitOfWork = new Mock<IUnitOfWork>();

            repository
                .Setup(r => r.GetByIdAsync(
                    999, It.IsAny<CancellationToken>()))
                .ReturnsAsync((PortfolioEntity?)null);

            var service = new PortfolioService(
           repository.Object,
           outboxWriter.Object,
           unitOfWork.Object,
           TimeProvider.System);

            using var cts = new CancellationTokenSource();

            // Act
            var result = await service.GetByIdAsync(999, cts.Token);

            // Assert
            Assert.Null(result);
        }



        //[Fact]
        //public async Task GetByIdAsync_WhenPortfolioDoesNotExist_ReturnsNull()
        //{
        //    // Arrange
        //    var service = new PortfolioService();
        //    using var cts = new CancellationTokenSource();

        //    // Act
        //    var result = await service.GetByIdAsync(999, cts.Token);

        //    // Assert
        //    Assert.Null(result);
        //}


        //[Fact]
        //public async Task CreateAsync_WhenRequestIsValid_CreatesPortfolio()
        //{
        //    // Arrange
        //    var service = new PortfolioService();
        //    using var cts = new CancellationTokenSource();

        //    var request = new CreatePortfolioRequest(
        //        "Test Portfolio",
        //        "SEK");

        //    // Act
        //    var created = await service.CreateAsync(request, cts.Token);
        //    var retrieved = await service.GetByIdAsync(created.Id, cts.Token);

        //    // Assert
        //    Assert.True(created.Id > 0);
        //    Assert.Equal("Test Portfolio", created.Name);
        //    Assert.Equal("SEK", created.BaseCurrency);

        //    Assert.NotNull(retrieved);
        //    Assert.Equal(created.Id, retrieved.Id);
        //}
    }
}
