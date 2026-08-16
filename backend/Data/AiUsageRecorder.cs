using Microsoft.Extensions.AI;

namespace TaskFlow.Api.Data;

public sealed class AiUsageRecorder
{
    private int inputTokens;
    private int outputTokens;
    private int totalTokens;

    public string? ModelName { get; private set; }
    public bool HasProviderReportedUsage { get; private set; }
    public int? InputTokens => HasProviderReportedUsage ? inputTokens : null;
    public int? OutputTokens => HasProviderReportedUsage ? outputTokens : null;
    public int? TotalTokens => HasProviderReportedUsage ? totalTokens : null;

    public void Record(ChatResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.ModelId))
        {
            ModelName = response.ModelId;
        }

        if (response.Usage is null)
        {
            return;
        }

        var input = ToInt(response.Usage.InputTokenCount);
        var output = ToInt(response.Usage.OutputTokenCount);
        var total = ToInt(response.Usage.TotalTokenCount) ?? (input + output);

        inputTokens += input ?? 0;
        outputTokens += output ?? 0;
        totalTokens += total ?? 0;
        HasProviderReportedUsage = true;
    }

    private static int? ToInt(long? value) => value is null ? null : (int)Math.Min(value.Value, int.MaxValue);
}