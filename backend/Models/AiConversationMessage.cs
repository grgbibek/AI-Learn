namespace TaskFlow.Api.Models;

public class AiConversationMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public AiConversation Conversation { get; set; } = null!;
    public required string Role { get; set; }
    public required string Content { get; set; }
    public bool WasSanitized { get; set; }
    public string DetectedTypesJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}