using HumanLoopLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HumanLoopLab.Infrastructure;

public sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<ActionProposal> Proposals => Set<ActionProposal>();
    public DbSet<PolicyDecisionRecord> PolicyDecisions => Set<PolicyDecisionRecord>();
    public DbSet<ApprovalRequest> Approvals => Set<ApprovalRequest>();
    public DbSet<ExecutionRecord> Executions => Set<ExecutionRecord>();
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ActionProposal>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => p.CorrelationId).IsUnique();
            entity.Property(p => p.ProposerId).HasMaxLength(100);
            entity.Property(p => p.Target).HasMaxLength(200);
            entity.Property(p => p.Reason).HasMaxLength(500);
            entity.Property(p => p.Evidence).HasMaxLength(2000);
            entity.Property(p => p.RequiredScope).HasMaxLength(100);
            entity.Property(p => p.Action).HasConversion<string>();
            entity.Property(p => p.ProposerRole).HasConversion<string>();
            entity.Property(p => p.Risk).HasConversion<string>();
            entity.Property(p => p.Status).HasConversion<string>();
            entity.HasOne(p => p.PolicyDecision).WithOne().HasForeignKey<PolicyDecisionRecord>(d => d.ProposalId);
            entity.HasOne(p => p.Approval).WithOne().HasForeignKey<ApprovalRequest>(a => a.ProposalId);
            entity.HasOne(p => p.Execution).WithOne().HasForeignKey<ExecutionRecord>(e => e.ProposalId);
        });
        model.Entity<PolicyDecisionRecord>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Outcome).HasConversion<string>();
            entity.Property(d => d.PolicyName).HasMaxLength(100);
        });
        model.Entity<ApprovalRequest>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.BoundAction).HasConversion<string>();
            entity.Property(a => a.Status).HasConversion<string>();
            entity.Property(a => a.BoundTarget).HasMaxLength(200);
            entity.Property(a => a.BoundScope).HasMaxLength(100);
        });
        model.Entity<ExecutionRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100);
            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
        });
        model.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(i => i.Key);
            entity.Property(i => i.Key).HasMaxLength(100);
        });
        model.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => new { a.ProposalId, a.OccurredAtUtc });
            entity.Property(a => a.ActorId).HasMaxLength(100);
            entity.Property(a => a.EventType).HasMaxLength(100);
        });
    }
}

public sealed class LabDbContextFactory : IDesignTimeDbContextFactory<LabDbContext>
{
    public LabDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LabDbContext>()
            .UseSqlite("Data Source=humanlooplab.db").Options);
}
