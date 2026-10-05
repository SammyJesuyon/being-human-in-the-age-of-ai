using HumanLoopLab.Application;
using HumanLoopLab.Domain;
using Xunit;

namespace HumanLoopLab.Application.Tests;

public sealed class WorkflowGuardTests
{
    private static readonly Actor Agent = new("agent-1", Role.Agent, false,
        new HashSet<string> { "deployment:production" });
    private static readonly Actor Approver = new("approver-1", Role.Approver, true,
        new HashSet<string> { "approval:grant" });

    [Fact]
    public async Task ExpiredApprovedActionCannotExecuteOrCallSimulator()
    {
        var fixture = new WorkflowFixture();
        var proposal = await fixture.ApprovedDeploymentAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(31));

        var error = await Assert.ThrowsAsync<WorkflowException>(() =>
            fixture.Workflow.ExecuteAsync(proposal.Id, Agent, "expired-deploy", CancellationToken.None));

        Assert.Equal("approval_required", error.Code);
        Assert.Equal(0, fixture.Executor.CallCount);
        Assert.Equal(ProposalStatus.Approved, proposal.Status);
        Assert.Null(proposal.Execution);
        Assert.DoesNotContain(fixture.Store.Events, audit => audit.EventType == "ExecutionStarted");
    }

    [Theory]
    [InlineData("action")]
    [InlineData("target")]
    [InlineData("scope")]
    public async Task ApprovalBoundToDifferentActionTargetOrScopeCannotExecute(string mismatch)
    {
        var fixture = new WorkflowFixture();
        var proposal = await fixture.ApprovedDeploymentAsync();
        var approval = Assert.IsType<ApprovalRequest>(proposal.Approval);
        switch (mismatch)
        {
            case "action": approval.BoundAction = ActionKind.ReadLogs; break;
            case "target": approval.BoundTarget = "another-environment"; break;
            case "scope": approval.BoundScope = "logs:read"; break;
        }

        var error = await Assert.ThrowsAsync<WorkflowException>(() =>
            fixture.Workflow.ExecuteAsync(proposal.Id, Agent, $"binding-{mismatch}", CancellationToken.None));

        Assert.Equal("approval_required", error.Code);
        Assert.Equal(0, fixture.Executor.CallCount);
        Assert.Null(proposal.Execution);
        Assert.DoesNotContain(fixture.Store.Events, audit => audit.EventType == "ExecutionStarted");
    }

    private sealed class WorkflowFixture
    {
        public MemoryWorkflowStore Store { get; } = new();
        public MutableTimeProvider Clock { get; } = new();
        public CountingExecutor Executor { get; } = new();
        public WorkflowService Workflow { get; }

        public WorkflowFixture() => Workflow = new(Store, new PolicyEngine(), Executor,
            new ExecutionGate(), Clock);

        public async Task<ActionProposal> ApprovedDeploymentAsync()
        {
            var proposal = await Workflow.CreateAsync(Agent, ActionKind.DeployToProduction,
                "demo-production", "Test a bounded deployment.", "Demo evidence.", CancellationToken.None);
            await Workflow.EvaluateAsync(proposal.Id, Agent, CancellationToken.None);
            await Workflow.DecideAsync(proposal.Id, Approver, true, CancellationToken.None);
            Assert.Equal(ProposalStatus.Approved, proposal.Status);
            return proposal;
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan interval) => _now = _now.Add(interval);
    }

    private sealed class CountingExecutor : ISimulatedActionExecutor
    {
        public int CallCount { get; private set; }
        public Task<string> ExecuteAsync(ActionProposal proposal, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult("SIMULATED");
        }
    }

    private sealed class MemoryWorkflowStore : IWorkflowStore
    {
        private readonly Dictionary<Guid, ActionProposal> _proposals = [];
        private readonly Dictionary<string, IdempotencyRecord> _idempotency = [];
        public List<AuditEvent> Events { get; } = [];

        public Task<ActionProposal?> GetProposalAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_proposals.GetValueOrDefault(id));
        public Task<IdempotencyRecord?> GetIdempotencyAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_idempotency.GetValueOrDefault(key));
        public Task<IReadOnlyList<AuditEvent>> GetAuditAsync(Guid proposalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(Events.Where(a => a.ProposalId == proposalId).ToList());
        public void AddProposal(ActionProposal proposal) => _proposals.Add(proposal.Id, proposal);
        public void AddPolicyDecision(PolicyDecisionRecord decision) { }
        public void AddApproval(ApprovalRequest approval) { }
        public void AddExecution(ExecutionRecord execution) { }
        public void AddAudit(AuditEvent auditEvent) => Events.Add(auditEvent);
        public void AddIdempotency(IdempotencyRecord record) => _idempotency.Add(record.Key, record);
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
