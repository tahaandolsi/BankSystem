//mkdir -p "$BASE/Transaction/src/Transaction.Domain"/{Entities,Enums,ValueObjects,Events,Exceptions} "$BASE/Transaction/src/Transaction.Infrastructure"/{Data,Repositories}
//mkdir -p "$BASE/Transaction/src/Transaction.Infrastructure"/{Data,Repositories}
namespace BankSystem.Transaction.Domain.Enums;

public enum TransactionType
{
    Deposit,
    Withdrawal,
    Transfer,
    Reversal
}