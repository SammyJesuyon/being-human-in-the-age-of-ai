using HumanLoopLab.Domain;

namespace HumanLoopLab.Application;

public sealed record DelegationCharacteristics(
    RiskLevel Consequence, bool Reversible, bool ResponsibilityCritical,
    bool RequiresHumanJudgment, bool Auditable, bool SkillMaintenanceImportant,
    bool Frequent, bool SkillAtrophyRisk);

public sealed record DelegationAdvice(DelegationMode SuggestedMode, IReadOnlyList<string> Reasons);

public sealed class DelegationAdvisor
{
    public DelegationAdvice Advise(DelegationCharacteristics task)
    {
        var reasons = new List<string>();
        if (task.RequiresHumanJudgment && task.ResponsibilityCritical)
        {
            reasons.Add("Judgment and responsibility must remain meaningfully human in this practice.");
            return new(DelegationMode.Retain, reasons);
        }
        if (task.RequiresHumanJudgment || task.Consequence >= RiskLevel.High || !task.Reversible)
        {
            reasons.Add("Consequence, reversibility or judgment calls for active human participation.");
            if (!task.Auditable) reasons.Add("The result is not yet observable enough for safe delegation.");
            return new(DelegationMode.Augment, reasons);
        }
        if (task.ResponsibilityCritical && task.SkillMaintenanceImportant && task.SkillAtrophyRisk)
        {
            reasons.Add("Ordinary execution may be delegated, but responsibility still requires a maintained human capability.");
            return new(DelegationMode.PeriodicallyReclaim, reasons);
        }
        if (!task.Auditable)
        {
            reasons.Add("The outcome cannot currently be reviewed or reconstructed well enough.");
            return new(DelegationMode.Augment, reasons);
        }
        reasons.Add("The bounded, observable, reversible task can be delegated in this illustrative policy.");
        if (task.Frequent) reasons.Add("Repetition makes a bounded automation path useful.");
        return new(DelegationMode.Delegate, reasons);
    }
}
