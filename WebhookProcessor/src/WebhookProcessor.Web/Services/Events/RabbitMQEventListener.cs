using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;
using Serilog.Context;
using System.Text;
using System.Text.Json;

namespace WebhookProducer.Web.Services.Events;

public class RabbitMqConfiguration
{
    public string Host { get; set; }
    public int Port { get; set; } = 5672;
    public string Exchange { get; set; } = "events";
    public string Queue { get; set; } = "webhookmanager";
    public string RoutingKey { get; set; } = "#";
}

public class WebhookProcessor : IHostedService, IDisposable
{
    const string ALLEVENTS_ROUTINGKEY = "#"; //listen to all routing keys
    private readonly IOptions<RabbitMqConfiguration> _options;
    private IConnection? _connection;
    private IModel? _channel;
    private AsyncEventingBasicConsumer? _consumer;
    private bool _disposedValue;

    public WebhookProcessor(IOptions<RabbitMqConfiguration> options)
    {
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory()
        {
            HostName = _options.Value.Host,
            DispatchConsumersAsync = true,
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        var queueName = _options.Value.Queue;

        _channel.QueueDeclare(queueName, durable: true, autoDelete: false, exclusive: false);
        _channel.QueueBind(queueName, _options.Value.Exchange, _options.Value.RoutingKey);

        Log.Information("### Listening to events for {RoutingKey}", _options.Value.RoutingKey);

        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.Received += EventReceived;

        _channel.BasicConsume(queueName, autoAck: false, _consumer);

        return Task.CompletedTask;
    }

    private Task EventReceived(object sender, BasicDeliverEventArgs @event)
    {
        var json = Encoding.UTF8.GetString(@event.Body.Span);

        var obj = JsonSerializer.Deserialize<JsonDocument>(json);


        //using (LogContext.PushProperty("EventId", obj["Metadata"]["EventId"]?? "UKNOWN"))
        //{
            Log.ForContext("body", json)
                .ForContext("obj", obj, destructureObjects: true)
                .Information("[{Application}] event {EventName} Recieved while listening to {ListeningKey}", nameof(WebhookProcessor), @event.RoutingKey, _options.Value.RoutingKey);
        //}
        _channel?.BasicAck(@event.DeliveryTag, multiple: false);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _connection?.Close();
        _channel?.Close();
        return Task.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _connection?.Close();
                _channel?.Close();
                _channel?.Dispose();
                _connection?.Dispose();
            }
            _connection = null;
            _channel = null;
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
