using HumanLoopLab.Application;
using HumanLoopLab.Domain;
using Xunit;

namespace HumanLoopLab.Application.Tests;

public sealed class PolicyAndDelegationTests
{
    private readonly PolicyEngine _policy = new();

    [Fact]
    public void LowRiskScopedActionIsAllowed()
    {
        var actor = Actor("logs:read");
        var result = _policy.Evaluate(actor, Definition(ActionKind.ReadLogs));
        Assert.Equal(PolicyOutcome.Allow, result.Outcome);
    }

    [Fact]
    public void MissingScopeIsDenied()
    {
        var result = _policy.Evaluate(Actor(), Definition(ActionKind.ReadLogs));
        Assert.Equal(PolicyOutcome.Deny, result.Outcome);
    }

    [Fact]
    public void HighRiskScopedActionNeedsHumanApproval()
    {
        var result = _policy.Evaluate(Actor("deployment:production"), Definition(ActionKind.DeployToProduction));
        Assert.Equal(PolicyOutcome.RequireHumanApproval, result.Outcome);
    }

    [Fact]
    public void AgentCannotSelfAuthorizeEvenWithPolicyScope()
    {
        var agent = new Actor("agent-1", Role.Agent, false, new HashSet<string> { "policy:modify" });
        var result = _policy.Evaluate(agent, Definition(ActionKind.ModifyAccessPolicy));
        Assert.Equal(PolicyOutcome.Deny, result.Outcome);
    }

    [Theory]
    [InlineData(true, true, DelegationMode.Retain)]
    [InlineData(false, true, DelegationMode.Augment)]
    public void AdvisorExplainsHumanJudgmentModes(bool critical, bool judgment, DelegationMode expected)
    {
        var input = new DelegationCharacteristics(RiskLevel.Low, true, critical,
            judgment, true, false, true, false);
        var advice = new DelegationAdvisor().Advise(input);
        Assert.Equal(expected, advice.SuggestedMode);
        Assert.NotEmpty(advice.Reasons);
    }

    [Fact]
    public void AdvisorCanRecommendPeriodicReclamation()
    {
        var input = new DelegationCharacteristics(RiskLevel.Low, true, true,
            false, true, true, true, true);
        var advice = new DelegationAdvisor().Advise(input);
        Assert.Equal(DelegationMode.PeriodicallyReclaim, advice.SuggestedMode);
        Assert.NotEmpty(advice.Reasons);
    }

    [Fact]
    public void AdvisorCanRecommendDelegation()
    {
        var input = new DelegationCharacteristics(RiskLevel.Low, true, false,
            false, true, false, true, false);
        var advice = new DelegationAdvisor().Advise(input);
        Assert.Equal(DelegationMode.Delegate, advice.SuggestedMode);
        Assert.NotEmpty(advice.Reasons);
    }

    private static Actor Actor(params string[] scopes) =>
        new("developer-1", Role.Developer, true, scopes.ToHashSet());

    private static ActionDefinition Definition(ActionKind kind)
    {
        Assert.True(ActionCatalog.TryGet(kind, out var definition));
        return definition;
    }
}
