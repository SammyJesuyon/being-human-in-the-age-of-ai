using HumanLoopLab.Application;
using HumanLoopLab.Domain;
using Microsoft.Extensions.Configuration;

namespace HumanLoopLab.Infrastructure;

public sealed class DemoActorDirectory : IActorDirectory
{
    private readonly IReadOnlyDictionary<string, Actor> _actors;

    public DemoActorDirectory(IConfiguration configuration)
    {
        var definitions = new (string Id, Role Role, bool Human)[]
        {
            ("agent-1", Role.Agent, false),
            ("observer-1", Role.Observer, true),
            ("developer-1", Role.Developer, true),
            ("operator-1", Role.Operator, true),
            ("approver-1", Role.Approver, true),
            ("admin-1", Role.Administrator, true)
        };
        _actors = definitions.ToDictionary(x => x.Id, x => new Actor(x.Id, x.Role, x.Human,
            configuration.GetSection($"DemoRoles:{x.Role}:Scopes").GetChildren()
                .Select(section => section.Value ?? "").Where(scope => scope.Length > 0)
                .ToHashSet(StringComparer.Ordinal)));
    }

    public Actor? Find(string actorId) => _actors.GetValueOrDefault(actorId);
    public IReadOnlyCollection<Actor> All => _actors.Values.ToArray();
}

public sealed class DeterministicDemoAgent : IAgentModel
{
    public DemoSuggestion Suggest(string scenario) => (scenario ?? "").Trim().ToLowerInvariant() switch
    {
        "incident" => new(ActionKind.SummarizeIncident, "demo-incident-42", "Summarize available incident facts for a reviewer.", "Demo incident report supplied by caller."),
        "patch" => new(ActionKind.DraftCodePatch, "demo-service", "Draft a candidate patch for review.", "Demo failure description supplied by caller."),
        "deploy" => new(ActionKind.DeployToProduction, "demo-production", "Propose a simulated production deployment.", "Demo release candidate supplied by caller."),
        "logs" => new(ActionKind.ReadLogs, "demo-service", "Read simulated logs for diagnosis.", "Demo investigation request supplied by caller."),
        _ => throw new WorkflowException("unknown_scenario", "Use incident, patch, deploy, or logs.", 400)
    };
}

public sealed class SimulatedActionExecutor : ISimulatedActionExecutor
{
    public Task<string> ExecuteAsync(ActionProposal proposal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = proposal.Action switch
        {
            ActionKind.ReadLogs => $"SIMULATED: log sample for {proposal.Target} (no host logs read).",
            ActionKind.SummarizeIncident => $"SIMULATED: incident summary for {proposal.Target} (no external data queried).",
            ActionKind.DraftCodePatch => $"SIMULATED: patch proposal for {proposal.Target} (no files changed).",
            ActionKind.RestartDevelopmentService => $"SIMULATED: restart of {proposal.Target} (no service touched).",
            ActionKind.RunDatabaseMigration => $"SIMULATED: migration of {proposal.Target} (no target database changed).",
            ActionKind.DeployToProduction => $"SIMULATED: deployment to {proposal.Target} (no deployment occurred).",
            ActionKind.ModifyAccessPolicy => $"SIMULATED: policy update for {proposal.Target} (no permissions changed).",
            _ => throw new WorkflowException("unknown_action", "Action has no simulator.", 400)
        };
        return Task.FromResult(result);
    }
}
