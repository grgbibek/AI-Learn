namespace TaskFlow.Api.Data;

public static class AuthPolicies
{
    public const string CanReadWorkItems = nameof(CanReadWorkItems);
    public const string CanWriteWorkItems = nameof(CanWriteWorkItems);
    public const string CanUseAi = nameof(CanUseAi);
    public const string CanUseRag = nameof(CanUseRag);
    public const string CanIngestKnowledge = nameof(CanIngestKnowledge);
    public const string CanUseAgents = nameof(CanUseAgents);
    public const string CanViewAnalytics = nameof(CanViewAnalytics);
    public const string CanManageUsers = nameof(CanManageUsers);
}