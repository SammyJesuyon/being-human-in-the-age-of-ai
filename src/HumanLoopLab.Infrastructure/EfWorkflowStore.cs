using HumanLoopLab.Application;
using HumanLoopLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace HumanLoopLab.Infrastructure;

// Local SQLite is the lab's memory. Audit rows are appended and read back in the order they were written.
public sealed class EfWorkflowStore(LabDbContext db) : IWorkflowStore
{
    public Task<ActionProposal?> GetProposalAsync(Guid id, CancellationToken cancellationToken) =>
        db.Proposals.Include(p => p.PolicyDecision).Include(p => p.Approval)
            .Include(p => p.Execution).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<IdempotencyRecord?> GetIdempotencyAsync(string key, CancellationToken cancellationToken) =>
        db.Idempotency.SingleOrDefaultAsync(i => i.Key == key, cancellationToken);

    public async Task<IReadOnlyList<AuditEvent>> GetAuditAsync(Guid proposalId, CancellationToken cancellationToken) =>
        await db.AuditEvents.AsNoTracking().Where(a => a.ProposalId == proposalId)
            .OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id).ToListAsync(cancellationToken);

    public void AddProposal(ActionProposal proposal) => db.Proposals.Add(proposal);
    public void AddPolicyDecision(PolicyDecisionRecord decision) => db.PolicyDecisions.Add(decision);
    public void AddApproval(ApprovalRequest approval) => db.Approvals.Add(approval);
    public void AddExecution(ExecutionRecord execution) => db.Executions.Add(execution);
    public void AddAudit(AuditEvent auditEvent) => db.AuditEvents.Add(auditEvent);
    public void AddIdempotency(IdempotencyRecord record) => db.Idempotency.Add(record);
    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
