namespace Consumer.Models;

public class ProcessedMessage
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ProcessedAt { get; set; }
}