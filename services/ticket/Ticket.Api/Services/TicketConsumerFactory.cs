using Confluent.Kafka;

namespace Ticket.Api.Services;

public interface ITicketConsumerFactory
{
    IConsumer<string, string> Create(ConsumerConfig configuration);
}

public sealed class TicketConsumerFactory : ITicketConsumerFactory
{
    public IConsumer<string, string> Create(ConsumerConfig configuration) =>
        new ConsumerBuilder<string, string>(configuration).Build();
}
