using HumanLoopLab.Domain;

namespace HumanLoopLab.Application;

// Deny by default, in this order. An agent may not enlarge its own authority.
// A missing scope is a denial. High risk or poor reversibility waits for a distinct human.
// Everything else that is in scope may proceed.
public sealed class PolicyEngine
{
    public PolicyResult Evaluate(Actor actor, ActionDefinition action)
    {
        // This denial stands even if someone later grants the agent policy:modify.
        // Holding a scope is not a license to rewrite the license.
        if (actor.Role == Role.Agent && action.Kind == ActionKind.ModifyAccessPolicy)
            return new(PolicyOutcome.Deny, "An agent cannot modify or expand its own authority.", "agent-no-self-authorization");

        if (!actor.Scopes.Contains(action.RequiredScope))
            return new(PolicyOutcome.Deny, $"Missing required scope: {action.RequiredScope}.", "required-scope");

        if (action.RequiresHumanApproval || action.Risk >= RiskLevel.High || !action.Reversible)
            return new(PolicyOutcome.RequireHumanApproval,
                "Risk or limited reversibility requires a distinct human to approve this bounded action.",
                "high-risk-human-approval");

        return new(PolicyOutcome.Allow, "The actor has the bounded scope and the action is low or medium risk.",
            "scoped-low-risk");
    }
}
