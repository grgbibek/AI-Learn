using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using TaskFlow.Api.Endpoints;

namespace TaskFlow.Api.Data;

/// <summary>
/// A lightweight development IChatClient supporting Structured Outputs & Tool Calling
/// when no external LLM API key (OpenAI/Azure/Ollama) is configured.
/// </summary>
public class DevMockChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new("DevMockChatClient");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages, 
        ChatOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        var lastMessage = chatMessages.LastOrDefault()?.Text ?? "No prompt provided";
        string responseText;

        // Pattern A: Check if Structured JSON Schema output was requested
        if (options?.ResponseFormat is ChatResponseFormatJson jsonFormat)
        {
            var mockStructuredData = new SubtaskAnalysisResponse(
                WorkItemId: 2,
                OriginalTitle: ExtractTopic(lastMessage),
                Subtasks: new List<string>
                {
                    "1. Refactor component state to Angular 19 signal()",
                    "2. Replace BehaviorSubject with rxResource or httpResource",
                    "3. Add unit test suite using Vitest / Angular Testing Library"
                },
                EstimatedTotalHours: 12,
                ComplexityLevel: "High"
            );

            responseText = JsonSerializer.Serialize(mockStructuredData, new JsonSerializerOptions { WriteIndented = true });
        }
        // Pattern B: Check if Tools / Function Calling was provided
        else if (options?.Tools is { Count: > 0 } tools)
        {
            var toolNames = string.Join(", ", tools.Select(t => t.Name));
            responseText = $"""
                [AI Function Calling Active]
                Identified prompt requirement. Executing registered C# AI Tools: [{toolNames}].
                Result: Found 3 high-priority work items in database matching your query.
                """;
        }
        else
        {
            // Default Unstructured Text Response
            responseText = $"""
                [AI Assistant Response - Microsoft.Extensions.AI]
                Suggested subtasks generated for your prompt:
                • 1. Analyze initial requirements & edge cases for: '{ExtractTopic(lastMessage)}'
                • 2. Implement backend service endpoints & DTO validation rules.
                • 3. Create Angular 19 Standalone Component with Signal state binding.
                """;
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages, 
        ChatOptions? options = null, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var fullResponse = await GetResponseAsync(chatMessages, options, cancellationToken);
        var words = fullResponse.Text.Split(' ');

        foreach (var word in words)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(word + " ")]
            };
            await Task.Delay(50, cancellationToken);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    private static string ExtractTopic(string prompt)
    {
        if (prompt.Contains("Title:"))
        {
            var parts = prompt.Split("Title:");
            if (parts.Length > 1) return parts[1].Split('\n')[0].Trim();
        }
        return "Work Item";
    }
}
