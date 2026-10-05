using HumanLoopLab.Domain;

namespace HumanLoopLab.Application;

public interface IWorkflowStore
{
    Task<ActionProposal?> GetProposalAsync(Guid id, CancellationToken cancellationToken);
    Task<IdempotencyRecord?> GetIdempotencyAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> GetAuditAsync(Guid proposalId, CancellationToken cancellationToken);
    void AddProposal(ActionProposal proposal);
    void AddPolicyDecision(PolicyDecisionRecord decision);
    void AddApproval(ApprovalRequest approval);
    void AddExecution(ExecutionRecord execution);
    void AddAudit(AuditEvent auditEvent);
    void AddIdempotency(IdempotencyRecord record);
    Task SaveAsync(CancellationToken cancellationToken);
}

public interface IActorDirectory
{
    Actor? Find(string actorId);
    IReadOnlyCollection<Actor> All { get; }
}

public interface IAgentModel
{
    DemoSuggestion Suggest(string scenario);
}

public sealed record DemoSuggestion(ActionKind Action, string Target, string Reason, string Evidence);

public interface ISimulatedActionExecutor
{
    Task<string> ExecuteAsync(ActionProposal proposal, CancellationToken cancellationToken);
}

public sealed record PolicyResult(PolicyOutcome Outcome, string Reason, string PolicyName);
public sealed record ExecutionResult(Guid ProposalId, Guid ExecutionId, string Result, bool DuplicateSuppressed);
public sealed record ResponsibilityTrace(
    Guid ProposalId, string ProposerId, string RequiredScope, string? PolicyName,
    PolicyOutcome? PolicyOutcome, string ApprovalAuthority, ApprovalStatus? ApprovalDecision,
    string? ApprovalDeciderId, DateTime? ApprovalDecisionAtUtc,
    string? ExecutorId, string Resource, string? FinalResult,
    DateTime CreatedAtUtc, DateTime? ApprovedAtUtc, DateTime? ExecutedAtUtc);

public sealed class WorkflowException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

// Keeps two requests from both passing the idempotency check before either one records a key.
// It only covers this process.
public sealed class ExecutionGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
