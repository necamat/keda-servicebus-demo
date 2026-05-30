using Azure.Storage.Queues;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<QueueClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["AzureStorage:ConnectionString"]
        ?? throw new InvalidOperationException("AzureStorage:ConnectionString is not set");
    var queueName = config["AzureStorage:QueueName"]
        ?? throw new InvalidOperationException("AzureStorage:QueueName is not set");
    var client = new QueueClient(connectionString, queueName);
    client.CreateIfNotExists();
    return client;
});

var app = builder.Build();

app.MapPost("/send", async (QueueClient queueClient, MessagePayload payload) =>
{
    var json = System.Text.Json.JsonSerializer.Serialize(payload);
    var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    await queueClient.SendMessageAsync(base64);
    return Results.Ok(new { message = "Sent", payload });
});

app.Run();

public record MessagePayload(string Id, string Data, DateTime CreatedAt);