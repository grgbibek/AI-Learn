using System.Diagnostics;

namespace TaskFlow.Api.Data;

// Shared ActivitySource for the multi-agent pipeline - OpenTelemetry picks up any Activity
// started from this source once it's registered via .AddSource() in Program.cs.
public static class AgentTelemetry
{
    public const string SourceName = "TaskFlow.Agents";
    public static readonly ActivitySource Source = new(SourceName);
}
