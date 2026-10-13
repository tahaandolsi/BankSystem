namespace BankSystem.Account.Application.DTOs;

public sealed record CreateAccountTransferAuthorizationRequest(
    Guid RequestId,
    Guid BusinessRequestId,
    Guid SourceAccountId,
    Guid BeneficiaryAccountId,
    decimal Amount,
    string Currency);
