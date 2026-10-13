using System.ComponentModel.DataAnnotations;

namespace BankSystem.Shared.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the local MassTransit messaging system.
/// RabbitMQ is used for local development.
/// </summary>
public class MassTransitOptions
{
    public const string SectionName = "MassTransit";

    public OutboxConfiguration Outbox { get; set; } = new();
    public RabbitMqConfiguration RabbitMQ { get; set; } = new();
    public RetryConfiguration Retry { get; set; } = new();
    public CircuitBreakerConfiguration CircuitBreaker { get; set; } = new();
}

public class OutboxConfiguration
{
    [Range(1, 60)]
    public int QueryDelaySeconds { get; set; } = 1;

    [Range(5, 1440)]
    public int DuplicateDetectionWindowMinutes { get; set; } = 5;

    public bool DisableInboxCleanupService { get; set; } = false;
}

/// <summary>
/// RabbitMQ connection settings used by the local development environment.
/// </summary>
public class RabbitMqConfiguration
{
    [Required]
    public string Host { get; set; } = "rabbitmq://localhost:5672";

    public string VirtualHost { get; set; } = "/";

    [Required]
    public string Username { get; set; } = "bankuser";

    [Required]
    public string Password { get; set; } = "bankpassword";
}

public class RetryConfiguration
{
    [Range(1, 20)]
    public int RetryLimit { get; set; } = 5;

    [Range(1, 60)]
    public int InitialRetryIntervalSeconds { get; set; } = 1;

    [Range(1, 300)]
    public int MaxRetryIntervalSeconds { get; set; } = 30;

    [Range(1, 60)]
    public int RetryIntervalIncrementSeconds { get; set; } = 1;
}

public class CircuitBreakerConfiguration
{
    [Range(1, 100)]
    public int TripThreshold { get; set; } = 5;

    [Range(1, 100)]
    public int ActiveThreshold { get; set; } = 5;

    [Range(1, 60)]
    public int ResetIntervalMinutes { get; set; } = 5;

    [Range(1, 60)]
    public int TrackingPeriodMinutes { get; set; } = 1;
}
