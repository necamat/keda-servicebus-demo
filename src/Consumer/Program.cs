using Azure.Storage.Queues;
using Consumer.Data;
using Consumer;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration["ConnectionStrings:PostgreSQL"]));

builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration["AzureStorage:ConnectionString"];
    var queueName = builder.Configuration["AzureStorage:QueueName"];
    var client = new QueueClient(connectionString, queueName);
    client.CreateIfNotExists();
    return client;
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();