namespace BankSystem.Transaction.Application.DTOs;
public record TransactionDto(Guid Id, Guid AccountId, Guid? ToAccountId, decimal Amount, string Currency, string Type, string Status, string Description, string Reference, DateTime? ProcessedAt);
