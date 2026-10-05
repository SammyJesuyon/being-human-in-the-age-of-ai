using HumanLoopLab.Domain;

namespace HumanLoopLab.Application;

public sealed record DelegationScenario(string Name, DelegationCharacteristics Characteristics, string Context);

public static class DelegationScenarios
{
    public static IReadOnlyList<DelegationScenario> All { get; } =
    [
        new("Read application logs", new(RiskLevel.Low, true, false, false, true, false, true, false),
            "A bounded read of synthetic demo logs."),
        new("Summarize an incident", new(RiskLevel.Medium, true, true, true, true, false, true, false),
            "A human must interpret the summary and decide what follows."),
        new("Draft a code patch", new(RiskLevel.Medium, true, true, true, true, true, true, true),
            "The patch is a proposal, not an approved change."),
        new("Run a database migration", new(RiskLevel.High, false, true, true, true, true, false, true),
            "Potentially difficult to reverse; execution requires separate approval."),
        new("Deploy to production", new(RiskLevel.High, false, true, true, true, true, true, true),
            "Requires deployment scope and a distinct human approval."),
        new("Modify authorization policy", new(RiskLevel.Critical, false, true, true, true, true, false, true),
            "The agent cannot authorize its own authority expansion.")
    ];
}
