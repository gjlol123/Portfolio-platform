using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Abstractions;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Entities;

namespace Portfolio.Infrastructure.Persistence;

    public sealed class PortfolioDbContext(
        DbContextOptions<PortfolioDbContext> options)
        : DbContext(options), IUnitOfWork
    {    

    public DbSet<InboxMessage> InboxMessages
    => Set<InboxMessage>();



    public DbSet<TransactionReportEntry> TransactionReportEntries
    => Set<TransactionReportEntry>();

    public DbSet<PortfolioEntity> Portfolios =>
        Set<PortfolioEntity>();

    public DbSet<OutboxMessage> OutboxMessages
    => Set<OutboxMessage>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TransactionReportEntry>(entity =>
        {
            entity.ToTable("TransactionReportEntries");

            entity.HasKey(x => x.TransactionId);

            entity.Property(x => x.Symbol)
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(x => x.Quantity)
                .HasPrecision(18, 4);
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("InboxMessages");

            entity.HasKey(x => new
            {
                x.ConsumerName,
                x.MessageId
            });

            entity.Property(x => x.ConsumerName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.MessageId)
                .IsRequired();

            entity.Property(x => x.ReceivedAtUtc)
                .IsRequired();
        });


        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LockedBy)
            .IsRequired(false);

            entity.Property(x => x.LockToken)
                .IsRequired(false);

            entity.Property(x => x.LockedUntilUtc)
                .IsRequired(false);

            entity.Property(x => x.Payload)
                .HasColumnType("nvarchar(max)")
                .IsRequired();

            entity.Property(x => x.LastError)
                .HasMaxLength(2000);

            entity.HasIndex(x => new
            {
                x.NextAttemptAtUtc,
                x.OccurredAtUtc
            })
                .HasFilter(
                    "[ProcessedAtUtc] IS NULL AND " +
                    "[DeadLetteredAtUtc] IS NULL");
        });

        var entity = modelBuilder.Entity<PortfolioEntity>();

        entity.ToTable("Portfolios");

        entity.HasKey(p => p.Id);

        entity.Property(p => p.Id)
            .ValueGeneratedOnAdd();

        entity.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        entity.Property(p => p.BaseCurrency)
            .IsRequired()
            .HasMaxLength(3);

        entity.Property(p => p.MarketValue)
            .HasPrecision(18, 2);

        entity.HasData(
            new PortfolioEntity(            
                 1,
                "Global Equity",
                 "EUR",
                125000.50m
            ),
            new PortfolioEntity(
            
                 2,
                 "European Bonds",
                 "SEK",
                 50000m
            ));
    }
}