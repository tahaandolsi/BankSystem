using MassTransit;

namespace BankSystem.Transaction.Infrastructure.Messaging;

/// <summary>MassTransit connection to the dedicated Payment financial vhost.</summary>
public interface IPaymentFinancialBus : IBus
{
}
