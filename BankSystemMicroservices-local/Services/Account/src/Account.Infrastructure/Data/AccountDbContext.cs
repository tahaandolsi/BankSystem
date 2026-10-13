using System.Diagnostics.CodeAnalysis;
using BankSystem.Account.Domain.Entities;
using BankSystem.Account.Domain.Enums;
using BankSystem.Shared.Domain.ValueObjects;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using AccountEntity = BankSystem.Account.Domain.Entities.Account;

namespace BankSystem.Account.Infrastructure.Data;

[ExcludeFromCodeCoverage]
public class AccountDbContext(DbContextOptions<AccountDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> Accounts { get; set; }
    public DbSet<AccountTransactionReceipt> AccountTransactionReceipts { get; set; }
    public DbSet<AccountTransferAuthorization> AccountTransferAuthorizations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountDbContext).Assembly);

        modelBuilder.Entity<AccountTransactionReceipt>(receipt =>
        {
            receipt.ToTable("AccountTransactionReceipts", table => table.HasCheckConstraint(
                "CK_AccountTransactionReceipts_OutcomeAndRejectionCode",
                "(\"Outcome\" = 'Applied' AND \"RejectionCode\" IS NULL) OR (\"Outcome\" = 'Rejected' AND \"RejectionCode\" IS NOT NULL)"));
            receipt.HasKey(x => x.TransactionId);
            receipt.Property(x => x.TransactionId).ValueGeneratedNever();
            receipt.Property(x => x.Outcome)
                .HasConversion<string>()
                .HasMaxLength(16)
                .HasDefaultValue(AccountTransactionReceiptOutcome.Applied)
                .IsRequired();
            receipt.Property(x => x.RejectionCode).HasMaxLength(64);
            receipt.Property(x => x.ProcessedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsRequired();
            receipt.Property(x => x.AppliedAt).IsRequired();
        });

        modelBuilder.Entity<AccountTransferAuthorization>(authorization =>
        {
            authorization.ToTable("AccountTransferAuthorizations", table =>
            {
                table.HasCheckConstraint(
                    "CK_AccountTransferAuthorizations_ValidOperation",
                    "\"AuthorizationId\" <> '00000000-0000-0000-0000-000000000000'::uuid " +
                    "AND \"RequestedByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid " +
                    "AND \"CustomerId\" <> '00000000-0000-0000-0000-000000000000'::uuid " +
                    "AND \"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid " +
                    "AND \"BusinessRequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid " +
                    "AND \"SourceAccountId\" <> '00000000-0000-0000-0000-000000000000'::uuid " +
                    "AND \"BeneficiaryAccountId\" <> '00000000-0000-0000-0000-000000000000'::uuid " +
                    "AND \"SourceAccountId\" <> \"BeneficiaryAccountId\" " +
                    "AND \"Amount\" > 0 " +
                    "AND btrim(\"Currency\") IN ('USD', 'EUR', 'GBP') " +
                    "AND \"ExpiresAt\" > \"CreatedAt\"");
                table.HasCheckConstraint(
                    "CK_AccountTransferAuthorizations_StatusAndTransaction",
                    "(\"Status\" = 'Consumed' AND \"ConsumedByTransactionId\" IS NOT NULL " +
                    "AND \"ConsumedByTransactionId\" <> '00000000-0000-0000-0000-000000000000'::uuid) " +
                    "OR (\"Status\" IN ('Active', 'Revoked', 'Expired') AND \"ConsumedByTransactionId\" IS NULL)");
            });

            authorization.HasKey(x => x.AuthorizationId);
            authorization.Property(x => x.AuthorizationId).ValueGeneratedNever();
            authorization.Property(x => x.RequestedByUserId).IsRequired();
            authorization.Property(x => x.CustomerId).IsRequired();
            authorization.Property(x => x.RequestId).IsRequired();
            authorization.Property(x => x.BusinessRequestId).IsRequired();
            authorization.Property(x => x.SourceAccountId).IsRequired();
            authorization.Property(x => x.BeneficiaryAccountId).IsRequired();
            authorization.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            authorization.Property(x => x.Currency)
                .HasColumnType("character(3)")
                .HasMaxLength(3)
                .IsFixedLength()
                .IsRequired();
            authorization.Property(x => x.CreatedAt).IsRequired();
            authorization.Property(x => x.ExpiresAt).IsRequired();
            authorization.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();
            authorization.Property(x => x.ConsumedByTransactionId);

            authorization.HasOne<AccountEntity>()
                .WithMany()
                .HasForeignKey(x => x.SourceAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            authorization.HasOne<AccountEntity>()
                .WithMany()
                .HasForeignKey(x => x.BeneficiaryAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            authorization.HasIndex(x => new { x.CustomerId, x.RequestId })
                .IsUnique()
                .HasDatabaseName("UX_AccountTransferAuthorizations_CustomerId_RequestId");
            authorization.HasIndex(x => new { x.CustomerId, x.BusinessRequestId })
                .HasDatabaseName("IX_AccountTransferAuthorizations_CustomerId_BusinessRequestId");
            authorization.HasIndex(x => new { x.CustomerId, x.BusinessRequestId })
                .IsUnique()
                .HasFilter("\"Status\" IN ('Active', 'Consumed')")
                .HasDatabaseName("UX_AccountTransferAuthorizations_ActiveOrConsumedBusinessRequest");
            authorization.HasIndex(x => new { x.CustomerId, x.BusinessRequestId })
                .IsUnique()
                .HasFilter("\"Status\" = 'Consumed'")
                .HasDatabaseName("UX_AccountTransferAuthorizations_ConsumedBusinessRequest");
            authorization.HasIndex(x => x.ConsumedByTransactionId)
                .IsUnique()
                .HasFilter("\"ConsumedByTransactionId\" IS NOT NULL")
                .HasDatabaseName("UX_AccountTransferAuthorizations_ConsumedByTransactionId");
        });

        // Configure Account entity
        ConfigureAccount(modelBuilder);

        // Configure MassTransit OutBox Entities
        modelBuilder.AddTransactionalOutboxEntities();
    }

    private static void ConfigureAccount(ModelBuilder modelBuilder)
    {
        var accountEntity = modelBuilder.Entity<AccountEntity>();

        // Primary key
        accountEntity.HasKey(a => a.Id);

        // Properties
        accountEntity.Property(a => a.Id).IsRequired().ValueGeneratedNever(); // We generate GUIDs in the domain

        accountEntity
            .Property(a => a.AccountNumber)
            .IsRequired()
            .HasMaxLength(34)
            .HasConversion(accountNumber => accountNumber.Value, value => new AccountNumber(value));

        accountEntity.Property(a => a.CustomerId).IsRequired();

        accountEntity.Property(a => a.Type).IsRequired().HasConversion<string>().HasMaxLength(15);

        accountEntity.Property(a => a.Status).IsRequired().HasConversion<string>().HasMaxLength(20);

        // Configure Money value object for Balance
        accountEntity.OwnsOne(
            a => a.Balance,
            balanceBuilder =>
            {
                balanceBuilder
                    .Property(m => m.Amount)
                    .HasColumnName("Balance")
                    .HasColumnType("decimal(18,2)")
                    .IsRequired();

                // Explicitly configure the nested Currency value object
                balanceBuilder.OwnsOne(
                    m => m.Currency,
                    currencyBuilder =>
                    {
                        currencyBuilder
                            .Property(c => c.Code)
                            .HasColumnName("CurrencyCode")
                            .HasMaxLength(3)
                            .IsFixedLength()
                            .IsRequired();

                        // Tell EF to not map other properties of Currency if they are not stored in the DB
                        currencyBuilder.Ignore(c => c.Symbol);
                    }
                );
            }
        );

        // Auditing fields
        accountEntity.Property(a => a.CreatedAt).IsRequired();

        accountEntity.Property(a => a.UpdatedAt).IsRequired(false);

        accountEntity.Property(a => a.CreatedBy).HasMaxLength(30);

        accountEntity.Property(a => a.UpdatedBy).HasMaxLength(30);

        // Indexes for performance
        accountEntity
            .HasIndex(a => a.AccountNumber)
            .IsUnique()
            .HasDatabaseName("IX_Accounts_AccountNumber");

        accountEntity.HasIndex(a => a.CustomerId).HasDatabaseName("IX_Accounts_CustomerId");

        accountEntity.HasIndex(a => a.Status).HasDatabaseName("IX_Accounts_Status");

        accountEntity.HasIndex(a => a.CreatedAt).HasDatabaseName("IX_Accounts_CreatedAt");

        // Table name
        accountEntity.ToTable("Accounts");
    }
}
