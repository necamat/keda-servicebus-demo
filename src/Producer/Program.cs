using Azure.Storage.Queues;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration["AzureStorage:ConnectionString"];
    var queueName = builder.Configuration["AzureStorage:QueueName"];
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