
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Infrastructure.Repositories;

namespace Portfolio.Api.Tests;

public class TestPortfolioApiFactory
    : WebApplicationFactory<Program>
{
    private readonly IPortfolioRepository _repository;

    public TestPortfolioApiFactory(
        IPortfolioRepository repository)
    {
        _repository = repository;
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPortfolioRepository>();
            services.AddSingleton(_repository);
        });
    }
}
