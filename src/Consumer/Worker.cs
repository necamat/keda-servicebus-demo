using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Consumer.Data;
using Consumer.Models;
using System.Text.Json;

namespace Consumer;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly QueueClient _queueClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public Worker(ILogger<Worker> logger, QueueClient queueClient, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _queueClient = queueClient;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            QueueMessage[] messages = await _queueClient.ReceiveMessagesAsync(maxMessages: 1, cancellationToken: stoppingToken);

            foreach (var message in messages)
            {
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(message.Body.ToString()));
                    var payload = JsonSerializer.Deserialize<MessagePayload>(json)
                        ?? throw new InvalidOperationException("Payload is null");

                    _logger.LogInformation("Received: {Id} - {Data}", payload.Id, payload.Data);

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    db.ProcessedMessages.Add(new ProcessedMessage
                    {
                        Id = Guid.NewGuid(),
                        ExternalId = payload.Id,
                        Data = payload.Data,
                        CreatedAt = payload.CreatedAt,
                        ProcessedAt = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                    await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}