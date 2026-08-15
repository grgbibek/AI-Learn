namespace TaskFlow.Api.Data;

public static class RateLimitPolicies
{
    public const string AiChat = nameof(AiChat);
    public const string KnowledgeIngest = nameof(KnowledgeIngest);
    public const string AgentPipeline = nameof(AgentPipeline);
}