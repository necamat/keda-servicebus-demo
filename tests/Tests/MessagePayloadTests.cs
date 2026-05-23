using System.Text.Json;
using Consumer.Models;

namespace Tests;

public class MessagePayloadTests
{
    [Fact]
    public void MessagePayload_Serialization_RoundTrip()
    {
        var payload = new MessagePayload("001", "test data", new DateTime(2026, 5, 23, 0, 0, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<MessagePayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(payload.Id, deserialized.Id);
        Assert.Equal(payload.Data, deserialized.Data);
        Assert.Equal(payload.CreatedAt, deserialized.CreatedAt);
    }

    [Fact]
    public void MessagePayload_Base64_EncodeDecode()
    {
        var payload = new MessagePayload("002", "hello world", DateTime.UtcNow);
        var json = JsonSerializer.Serialize(payload);

        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var result = JsonSerializer.Deserialize<MessagePayload>(decoded);

        Assert.NotNull(result);
        Assert.Equal(payload.Id, result.Id);
        Assert.Equal(payload.Data, result.Data);
    }

    [Fact]
    public void MessagePayload_InvalidJson_ReturnsNull()
    {
        var json = "invalid json";
    
        var result = Record.Exception(() => JsonSerializer.Deserialize<MessagePayload>(json));
    
        Assert.NotNull(result);
        Assert.IsType<JsonException>(result);
    }
}