using HumanLoopLab.Domain;
using Xunit;

namespace HumanLoopLab.Domain.Tests;

public sealed class ProposalStateMachineTests
{
    [Fact]
    public void RejectedProposalCannotBecomeExecutable()
    {
        var proposal = new ActionProposal { Status = ProposalStatus.Rejected };
        Assert.Throws<InvalidOperationException>(() => ProposalStateMachine.Move(proposal, ProposalStatus.Executing));
    }

    [Fact]
    public void ApprovalPathRequiresPendingState()
    {
        var proposal = new ActionProposal();
        ProposalStateMachine.Move(proposal, ProposalStatus.PendingApproval);
        ProposalStateMachine.Move(proposal, ProposalStatus.Approved);
        Assert.Equal(ProposalStatus.Approved, proposal.Status);
    }
}
