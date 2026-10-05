namespace HumanLoopLab.Domain;

public static class ProposalStateMachine
{
    private static readonly IReadOnlyDictionary<ProposalStatus, IReadOnlySet<ProposalStatus>> Allowed =
        new Dictionary<ProposalStatus, IReadOnlySet<ProposalStatus>>
        {
            [ProposalStatus.Draft] = new HashSet<ProposalStatus> { ProposalStatus.Evaluated, ProposalStatus.Denied, ProposalStatus.PendingApproval },
            [ProposalStatus.Evaluated] = new HashSet<ProposalStatus> { ProposalStatus.Executing },
            [ProposalStatus.Denied] = new HashSet<ProposalStatus>(),
            [ProposalStatus.PendingApproval] = new HashSet<ProposalStatus> { ProposalStatus.Approved, ProposalStatus.Rejected },
            [ProposalStatus.Approved] = new HashSet<ProposalStatus> { ProposalStatus.Executing },
            [ProposalStatus.Rejected] = new HashSet<ProposalStatus>(),
            [ProposalStatus.Executing] = new HashSet<ProposalStatus> { ProposalStatus.Executed, ProposalStatus.Failed },
            [ProposalStatus.Executed] = new HashSet<ProposalStatus>(),
            [ProposalStatus.Failed] = new HashSet<ProposalStatus>()
        };

    public static void Move(ActionProposal proposal, ProposalStatus next)
    {
        if (!Allowed[proposal.Status].Contains(next))
            throw new InvalidOperationException($"Transition {proposal.Status} -> {next} is not allowed.");
        proposal.Status = next;
    }
}
