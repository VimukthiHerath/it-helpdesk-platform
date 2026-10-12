using System.Text.Json;
using Confluent.Kafka;

namespace Auth.Api.Services;

public interface IPasswordResetPublisher
{
    // Fire-and-forget: never throws and never blocks the request. Forgot-password
    // must answer the same way whether or not the email could be queued.
    void Publish(int userId, string email, string resetLink, DateTime expiresAtUtc);
}

// Publishes a password-reset-requested event. Notification.Api consumes it and sends
// the email, so Auth.Api needs no SMTP settings of its own.
//
// The event carries the reset link, which is a secret: it must never be logged.
public sealed class KafkaPasswordResetPublisher : IPasswordResetPublisher, IDisposable
{
    private readonly ILogger<KafkaPasswordResetPublisher> _logger;
    private readonly string _topic;
    private readonly Lazy<IProducer<string, string>> _producer;

    public KafkaPasswordResetPublisher(IConfiguration configuration, ILogger<KafkaPasswordResetPublisher> logger)
    {
        _logger = logger;
        _topic = configuration["Kafka:PasswordResetTopic"] ?? "password-reset-requested";

        var config = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            // Give up on an unreachable broker quickly instead of buffering for minutes.
            MessageTimeoutMs = 10000,
            SocketTimeoutMs = 5000
        };

        // Created on first use so the service starts even if Kafka is down.
        _producer = new Lazy<IProducer<string, string>>(() => new ProducerBuilder<string, string>(config).Build());
    }

    public void Publish(int userId, string email, string resetLink, DateTime expiresAtUtc)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                eventType = "PasswordResetRequested",
                userId,
                email,
                resetLink,
                expiresAtUtc,
                requestedAtUtc = DateTime.UtcNow
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            _producer.Value.Produce(
                _topic,
                new Message<string, string> { Key = userId.ToString(), Value = payload },
                report =>
                {
                    if (report.Error.IsError)
                    {
                        _logger.LogError(
                            "Could not queue the password reset email for user {UserId}: {Reason}",
                            userId, report.Error.Reason);
                    }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not queue the password reset email for user {UserId}.", userId);
        }
    }

    public void Dispose()
    {
        if (!_producer.IsValueCreated) return;
        try
        {
            _producer.Value.Flush(TimeSpan.FromSeconds(3));
            _producer.Value.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while closing the password reset Kafka producer.");
        }
    }
}
