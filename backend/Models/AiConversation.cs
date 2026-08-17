namespace TaskFlow.Api.Models;

public class AiConversation
{
    public int Id { get; set; }
    public required string UserName { get; set; }
    public required string Title { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<AiConversationMessage> Messages { get; set; } = [];
}