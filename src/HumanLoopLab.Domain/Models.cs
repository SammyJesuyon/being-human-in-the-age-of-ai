namespace HumanLoopLab.Domain;

public enum ActionKind
{
    ReadLogs,
    SummarizeIncident,
    DraftCodePatch,
    RestartDevelopmentService,
    RunDatabaseMigration,
    DeployToProduction,
    ModifyAccessPolicy
}

public enum Role { Observer, Developer, Operator, Approver, Administrator, Agent }
public enum RiskLevel { Low, Medium, High, Critical }
public enum PolicyOutcome { Allow, Deny, RequireHumanApproval }
public enum ProposalStatus
{
    Draft, Evaluated, Denied, PendingApproval, Approved, Rejected,
    Executing, Executed, Failed
}
public enum ApprovalStatus { Pending, Approved, Rejected }
public enum ExecutionStatus { Started, Succeeded, Failed }
public enum DelegationMode { Retain, Augment, Delegate, PeriodicallyReclaim }

public sealed record Actor(string Id, Role Role, bool IsHuman, IReadOnlySet<string> Scopes);
public sealed record ActionDefinition(
    ActionKind Kind, string RequiredScope, RiskLevel Risk, bool Reversible,
    bool RequiresHumanApproval, string Description);

public static class ActionCatalog
{
    private static readonly IReadOnlyDictionary<ActionKind, ActionDefinition> Definitions =
        new Dictionary<ActionKind, ActionDefinition>
        {
            [ActionKind.ReadLogs] = new(ActionKind.ReadLogs, "logs:read", RiskLevel.Low, true, false, "Read demo application logs"),
            [ActionKind.SummarizeIncident] = new(ActionKind.SummarizeIncident, "incident:summarize", RiskLevel.Medium, true, false, "Summarize a demo incident"),
            [ActionKind.DraftCodePatch] = new(ActionKind.DraftCodePatch, "code:draft", RiskLevel.Medium, true, false, "Draft a patch for human review"),
            [ActionKind.RestartDevelopmentService] = new(ActionKind.RestartDevelopmentService, "service:restart", RiskLevel.High, true, true, "Simulate a development restart"),
            [ActionKind.RunDatabaseMigration] = new(ActionKind.RunDatabaseMigration, "database:migrate", RiskLevel.High, false, true, "Simulate a database migration"),
            [ActionKind.DeployToProduction] = new(ActionKind.DeployToProduction, "deployment:production", RiskLevel.High, false, true, "Simulate a production deployment"),
            [ActionKind.ModifyAccessPolicy] = new(ActionKind.ModifyAccessPolicy, "policy:modify", RiskLevel.Critical, false, true, "Simulate a policy change")
        };

    public static IReadOnlyCollection<ActionDefinition> All => Definitions.Values.ToArray();
    public static bool TryGet(ActionKind kind, out ActionDefinition definition) =>
        Definitions.TryGetValue(kind, out definition!);
}

public sealed class ActionProposal
{
    public Guid Id { get; set; }
    public Guid CorrelationId { get; set; }
    public string ProposerId { get; set; } = "";
    public Role ProposerRole { get; set; }
    public ActionKind Action { get; set; }
    public string Target { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string RequiredScope { get; set; } = "";
    public RiskLevel Risk { get; set; }
    public bool Reversible { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Draft;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovedById { get; set; }
    public PolicyDecisionRecord? PolicyDecision { get; set; }
    public ApprovalRequest? Approval { get; set; }
    public ExecutionRecord? Execution { get; set; }
}

public sealed class PolicyDecisionRecord
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public PolicyOutcome Outcome { get; set; }
    public string Reason { get; set; } = "";
    public string PolicyName { get; set; } = "";
    public DateTime DecidedAtUtc { get; set; }
}

public sealed class ApprovalRequest
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public ActionKind BoundAction { get; set; }
    public string BoundTarget { get; set; } = "";
    public string BoundScope { get; set; } = "";
    public ApprovalStatus Status { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecidedById { get; set; }
}

public sealed class ExecutionRecord
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public string ExecutorId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public ExecutionStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Result { get; set; } = "";
}

public sealed class IdempotencyRecord
{
    public string Key { get; set; } = "";
    public Guid ProposalId { get; set; }
    public Guid ExecutionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid ProposalId { get; set; }
    public string ActorId { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string MetadataJson { get; set; } = "{}";
}
