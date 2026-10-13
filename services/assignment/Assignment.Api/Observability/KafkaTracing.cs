using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace ItHelpdesk.Observability;

// Carries the W3C trace context through Kafka message headers so Application Insights
// can show one ticket's path across services. With no telemetry configured every call is a no-op.
public static class KafkaTracing
{
    public const string SourceName = "ItHelpdesk.Kafka";
    private const string TraceParent = "traceparent";
    private static readonly ActivitySource Source = new(SourceName);

    // Headers for an outgoing message: the trace context of whatever is running now (for example the HTTP request).
    public static Headers CurrentHeaders()
    {
        var headers = new Headers();
        var id = Activity.Current?.Id;
        if (!string.IsNullOrEmpty(id))
        {
            headers.Add(TraceParent, Encoding.UTF8.GetBytes(id));
        }
        return headers;
    }

    // Starts a span for handling a consumed message, parented to the producer's trace when the header is present.
    public static Activity? StartConsume(ConsumeResult<string, string>? result)
    {
        string? parent = null;
        if (result?.Message?.Headers is { } headers && headers.TryGetLastBytes(TraceParent, out var bytes))
        {
            parent = Encoding.UTF8.GetString(bytes);
        }

        var topic = result?.Topic ?? "unknown";
        return ActivityContext.TryParse(parent, null, out var context)
            ? Source.StartActivity($"{topic} process", ActivityKind.Consumer, context)
            : Source.StartActivity($"{topic} process", ActivityKind.Consumer);
    }
}
