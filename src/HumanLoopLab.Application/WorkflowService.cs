using System.Text.Json;
using HumanLoopLab.Domain;

namespace HumanLoopLab.Application;

public sealed class WorkflowService(
    IWorkflowStore store,
    PolicyEngine policy,
    ISimulatedActionExecutor executor,
    ExecutionGate gate,
    TimeProvider clock)
{
    public async Task<ActionProposal> CreateAsync(
        Actor proposer, ActionKind action, string target, string reason, string evidence,
        CancellationToken cancellationToken)
    {
        if (!ActionCatalog.TryGet(action, out var definition))
            throw new WorkflowException("unknown_action", "The action is not registered.", 400);
        ValidateText(target, nameof(target), 1, 200);
        ValidateText(reason, nameof(reason), 1, 500);
        ValidateText(evidence, nameof(evidence), 0, 2_000);

        var proposal = new ActionProposal
        {
            Id = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ProposerId = proposer.Id,
            ProposerRole = proposer.Role,
            Action = action,
            Target = target.Trim(),
            Reason = reason.Trim(),
            Evidence = evidence?.Trim() ?? "",
            RequiredScope = definition.RequiredScope,
            Risk = definition.Risk,
            Reversible = definition.Reversible,
            CreatedAtUtc = UtcNow()
        };
        store.AddProposal(proposal);
        Audit(proposal, proposer.Id, "ProposalCreated", new { action, proposal.Target });
        await store.SaveAsync(cancellationToken);
        return proposal;
    }

    public async Task<ActionProposal> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await store.GetProposalAsync(id, cancellationToken)
        ?? throw new WorkflowException("proposal_not_found", "Proposal not found.", 404);

    public async Task<ActionProposal> EvaluateAsync(Guid id, Actor caller, CancellationToken cancellationToken)
    {
        var proposal = await GetAsync(id, cancellationToken);
        if (proposal.Status != ProposalStatus.Draft)
            throw InvalidState(proposal);
        if (caller.Id != proposal.ProposerId)
            throw new WorkflowException("permission_denied", "Only the proposer can request evaluation.", 403);
        if (!ActionCatalog.TryGet(proposal.Action, out var definition))
            throw new WorkflowException("unknown_action", "The action is not registered.", 400);
        var decision = policy.Evaluate(caller, definition);
        proposal.PolicyDecision = new PolicyDecisionRecord
        {
            Id = Guid.NewGuid(), ProposalId = proposal.Id, Outcome = decision.Outcome,
            Reason = decision.Reason, PolicyName = decision.PolicyName, DecidedAtUtc = UtcNow()
        };
        store.AddPolicyDecision(proposal.PolicyDecision);
        Audit(proposal, caller.Id, "PolicyEvaluated", new { decision.Outcome, decision.PolicyName, proposal.RequiredScope });
        switch (decision.Outcome)
        {
            case PolicyOutcome.Allow:
                ProposalStateMachine.Move(proposal, ProposalStatus.Evaluated);
                break;
            case PolicyOutcome.Deny:
                ProposalStateMachine.Move(proposal, ProposalStatus.Denied);
                Audit(proposal, caller.Id, "PermissionDenied", new { decision.Reason });
                break;
            case PolicyOutcome.RequireHumanApproval:
                ProposalStateMachine.Move(proposal, ProposalStatus.PendingApproval);
                proposal.Approval = new ApprovalRequest
                {
                    Id = Guid.NewGuid(), ProposalId = proposal.Id, BoundAction = proposal.Action,
                    BoundTarget = proposal.Target, BoundScope = proposal.RequiredScope,
                    Status = ApprovalStatus.Pending, RequestedAtUtc = UtcNow(),
                    ExpiresAtUtc = UtcNow().AddMinutes(30)
                };
                store.AddApproval(proposal.Approval);
                Audit(proposal, caller.Id, "ApprovalRequested", new
                {
                    proposal.Action, proposal.Target, proposal.RequiredScope,
                    proposal.Approval.ExpiresAtUtc
                });
                break;
        }
        await store.SaveAsync(cancellationToken);
        return proposal;
    }

    public async Task<ActionProposal> DecideAsync(
        Guid id, Actor approver, bool approve, CancellationToken cancellationToken)
    {
        var proposal = await GetAsync(id, cancellationToken);
        if (proposal.Status != ProposalStatus.PendingApproval || proposal.Approval is null)
            throw InvalidState(proposal);
        if (!approver.IsHuman || approver.Role is not (Role.Approver or Role.Administrator)
            || !approver.Scopes.Contains("approval:grant") || approver.Id == proposal.ProposerId)
        {
            Audit(proposal, approver.Id, "ApprovalPermissionDenied", new { reason = "separation-of-duties" });
            await store.SaveAsync(cancellationToken);
            throw new WorkflowException("permission_denied", "A distinct authorized human approver is required.", 403);
        }
        if (UtcNow() > proposal.Approval.ExpiresAtUtc)
            throw new WorkflowException("stale_approval", "The approval window has expired; create a new proposal.", 409);
        proposal.Approval.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        proposal.Approval.DecidedById = approver.Id;
        proposal.Approval.DecidedAtUtc = UtcNow();
        if (approve)
        {
            proposal.ApprovedById = approver.Id;
            proposal.ApprovedAtUtc = UtcNow();
            ProposalStateMachine.Move(proposal, ProposalStatus.Approved);
            Audit(proposal, approver.Id, "ProposalApproved", new { proposal.Action, proposal.Target });
        }
        else
        {
            ProposalStateMachine.Move(proposal, ProposalStatus.Rejected);
            Audit(proposal, approver.Id, "ProposalRejected", new { proposal.Action, proposal.Target });
        }
        await store.SaveAsync(cancellationToken);
        return proposal;
    }

    public async Task<ExecutionResult> ExecuteAsync(
        Guid id, Actor caller, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateText(idempotencyKey, nameof(idempotencyKey), 1, 100);
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var proposal = await GetAsync(id, cancellationToken);
            if (!caller.Scopes.Contains(proposal.RequiredScope))
                throw new WorkflowException("permission_denied", "Executor lacks the required scope.", 403);
            var prior = await store.GetIdempotencyAsync(idempotencyKey, cancellationToken);
            if (prior is not null)
            {
                if (prior.ProposalId != id)
                    throw new WorkflowException("idempotency_conflict", "This key belongs to another proposal.", 409);
                if (proposal.Status != ProposalStatus.Executed || proposal.Execution is null)
                    throw new WorkflowException("execution_in_progress", "The previous attempt did not complete successfully.", 409);
                Audit(proposal, caller.Id, "DuplicateExecutionSuppressed", new { idempotencyKey });
                await store.SaveAsync(cancellationToken);
                return new(id, proposal.Execution.Id, proposal.Execution.Result, true);
            }
            if (proposal.Status == ProposalStatus.PendingApproval)
                throw new WorkflowException("approval_required", "A human approval is required before execution.", 409);
            if (proposal.Status == ProposalStatus.Denied)
                throw new WorkflowException("permission_denied", "A denied proposal cannot execute.", 403);
            if (proposal.Status == ProposalStatus.Rejected)
                throw new WorkflowException("proposal_rejected", "A rejected proposal cannot execute.", 409);
            if (proposal.Status == ProposalStatus.Executed)
                throw new WorkflowException("already_executed", "Use the original idempotency key to retrieve the result.", 409);
            if (proposal.Status is not (ProposalStatus.Evaluated or ProposalStatus.Approved))
                throw InvalidState(proposal);
            if (proposal.PolicyDecision?.Outcome == PolicyOutcome.RequireHumanApproval)
            {
                var approval = proposal.Approval;
                if (approval is null || approval.Status != ApprovalStatus.Approved
                    || approval.BoundAction != proposal.Action || approval.BoundTarget != proposal.Target
                    || approval.BoundScope != proposal.RequiredScope || UtcNow() > approval.ExpiresAtUtc)
                    throw new WorkflowException("approval_required", "A current approval bound to this action is required.", 409);
            }
            if (proposal.PolicyDecision?.Outcome is null or PolicyOutcome.Deny)
                throw new WorkflowException("permission_denied", "No allowing policy decision exists.", 403);

            ProposalStateMachine.Move(proposal, ProposalStatus.Executing);
            var record = new ExecutionRecord
            {
                Id = Guid.NewGuid(), ProposalId = proposal.Id, ExecutorId = caller.Id,
                IdempotencyKey = idempotencyKey, Status = ExecutionStatus.Started,
                StartedAtUtc = UtcNow()
            };
            proposal.Execution = record;
            store.AddExecution(record);
            store.AddIdempotency(new IdempotencyRecord
            {
                Key = idempotencyKey, ProposalId = proposal.Id, ExecutionId = record.Id,
                CreatedAtUtc = UtcNow()
            });
            Audit(proposal, caller.Id, "ExecutionStarted", new { proposal.Action, proposal.Target, idempotencyKey });
            await store.SaveAsync(cancellationToken);

            try
            {
                record.Result = await executor.ExecuteAsync(proposal, cancellationToken);
                record.Status = ExecutionStatus.Succeeded;
                record.CompletedAtUtc = UtcNow();
                ProposalStateMachine.Move(proposal, ProposalStatus.Executed);
                Audit(proposal, caller.Id, "ExecutionSucceeded", new { record.Result });
                await store.SaveAsync(cancellationToken);
                return new(id, record.Id, record.Result, false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                record.Result = "Simulated execution failed.";
                record.Status = ExecutionStatus.Failed;
                record.CompletedAtUtc = UtcNow();
                ProposalStateMachine.Move(proposal, ProposalStatus.Failed);
                Audit(proposal, caller.Id, "ExecutionFailed", new { errorType = exception.GetType().Name });
                await store.SaveAsync(cancellationToken);
                throw new WorkflowException("execution_failed", "The simulated action failed.", 500);
            }
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEvent>> AuditAsync(Guid id, CancellationToken cancellationToken)
    {
        await GetAsync(id, cancellationToken);
        return await store.GetAuditAsync(id, cancellationToken);
    }

    public async Task<ResponsibilityTrace> ResponsibilityAsync(Guid id, CancellationToken cancellationToken)
    {
        var p = await GetAsync(id, cancellationToken);
        return new(p.Id, p.ProposerId, p.RequiredScope, p.PolicyDecision?.PolicyName,
            p.PolicyDecision?.Outcome, "Distinct human Approver or Administrator",
            p.Approval?.Status, p.Approval?.DecidedById, p.Approval?.DecidedAtUtc,
            p.Execution?.ExecutorId, p.Target,
            p.Execution?.Result, p.CreatedAtUtc, p.ApprovedAtUtc, p.Execution?.CompletedAtUtc);
    }

    private void Audit(ActionProposal proposal, string actorId, string eventType, object metadata) =>
        store.AddAudit(new AuditEvent
        {
            CorrelationId = proposal.CorrelationId, ProposalId = proposal.Id,
            ActorId = actorId, EventType = eventType, OccurredAtUtc = UtcNow(),
            MetadataJson = JsonSerializer.Serialize(metadata)
        });

    private DateTime UtcNow() => clock.GetUtcNow().UtcDateTime;

    private static WorkflowException InvalidState(ActionProposal proposal) =>
        new("invalid_state", $"Action is not valid while proposal is {proposal.Status}.", 409);

    private static void ValidateText(string? value, string name, int minimum, int maximum)
    {
        var length = value?.Trim().Length ?? 0;
        if (length < minimum || length > maximum)
            throw new WorkflowException("validation_error", $"{name} must contain {minimum}–{maximum} characters.", 400);
    }
}
