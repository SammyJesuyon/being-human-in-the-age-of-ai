using System.Text.Json.Serialization;
using HumanLoopLab.Application;
using HumanLoopLab.Domain;
using HumanLoopLab.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<LabDbContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=humanlooplab.db"));
builder.Services.AddScoped<IWorkflowStore, EfWorkflowStore>();
builder.Services.AddSingleton<IActorDirectory, DemoActorDirectory>();
builder.Services.AddSingleton<IAgentModel, DeterministicDemoAgent>();
builder.Services.AddSingleton<ISimulatedActionExecutor, SimulatedActionExecutor>();
builder.Services.AddSingleton<PolicyEngine>();
builder.Services.AddSingleton<DelegationAdvisor>();
builder.Services.AddSingleton<ExecutionGate>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<WorkflowService>();

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var workflow = exception as WorkflowException;
    if (workflow is null)
        context.RequestServices.GetRequiredService<ILogger<Program>>()
            .LogError(exception, "Unhandled workflow request error");
    context.Response.StatusCode = workflow?.StatusCode ?? 500;
    await Results.Problem(
        title: workflow?.Code ?? "internal_error",
        detail: workflow?.Message ?? "The request could not be completed.",
        statusCode: context.Response.StatusCode).ExecuteAsync(context);
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<LabDbContext>().Database.MigrateAsync();

var api = app.MapGroup("/api");
api.MapGet("/actions", () => Results.Ok(ActionCatalog.All));
api.MapGet("/actors", (IActorDirectory actors) => Results.Ok(actors.All.Select(a => new
{
    a.Id, a.Role, a.IsHuman, a.Scopes
})));
api.MapGet("/policies", () => Results.Ok(new
{
    rules = new[] { "required-scope", "high-risk-human-approval", "agent-no-self-authorization" },
    approvalWindowMinutes = 30,
    note = "Demonstration policy only; an actor header is not authentication."
}));
api.MapPost("/proposals", async (CreateProposalRequest request, HttpContext http,
    IActorDirectory actors, WorkflowService workflow, CancellationToken token) =>
{
    var actor = RequireActor(http, actors);
    if (!Enum.TryParse<ActionKind>(request.Action, true, out var action) ||
        !ActionCatalog.TryGet(action, out _))
        throw new WorkflowException("unknown_action", "Action must be a registered action name.", 400);
    var proposal = await workflow.CreateAsync(actor, action, request.Target, request.Reason, request.Evidence, token);
    return Results.Created($"/api/proposals/{proposal.Id}", ProposalView.From(proposal));
});
api.MapGet("/proposals/{id:guid}", async (Guid id, WorkflowService workflow, CancellationToken token) =>
    Results.Ok(ProposalView.From(await workflow.GetAsync(id, token))));
api.MapPost("/proposals/{id:guid}/evaluate", async (Guid id, HttpContext http,
    IActorDirectory actors, WorkflowService workflow, CancellationToken token) =>
    Results.Ok(ProposalView.From(await workflow.EvaluateAsync(id, RequireActor(http, actors), token))));
api.MapPost("/proposals/{id:guid}/approve", async (Guid id, HttpContext http,
    IActorDirectory actors, WorkflowService workflow, CancellationToken token) =>
    Results.Ok(ProposalView.From(await workflow.DecideAsync(id, RequireActor(http, actors), true, token))));
api.MapPost("/proposals/{id:guid}/reject", async (Guid id, HttpContext http,
    IActorDirectory actors, WorkflowService workflow, CancellationToken token) =>
    Results.Ok(ProposalView.From(await workflow.DecideAsync(id, RequireActor(http, actors), false, token))));
api.MapPost("/proposals/{id:guid}/execute", async (Guid id, HttpContext http,
    IActorDirectory actors, WorkflowService workflow, CancellationToken token) =>
{
    var key = http.Request.Headers["Idempotency-Key"].ToString();
    return Results.Ok(await workflow.ExecuteAsync(id, RequireActor(http, actors), key, token));
});
api.MapGet("/proposals/{id:guid}/audit", async (Guid id, WorkflowService workflow, CancellationToken token) =>
    Results.Ok((await workflow.AuditAsync(id, token)).Select(a => new AuditView(
        a.Id, a.CorrelationId, a.ProposalId, a.ActorId, a.EventType,
        a.OccurredAtUtc, a.MetadataJson))));
api.MapGet("/proposals/{id:guid}/responsibility", async (Guid id, WorkflowService workflow, CancellationToken token) =>
    Results.Ok(await workflow.ResponsibilityAsync(id, token)));
api.MapPost("/delegation/advice", (DelegationCharacteristics input, DelegationAdvisor advisor) =>
    Results.Ok(advisor.Advise(input)));
api.MapGet("/delegation/scenarios", (DelegationAdvisor advisor) => Results.Ok(
    DelegationScenarios.All.Select(s => new { s.Name, s.Context, s.Characteristics,
        Advice = advisor.Advise(s.Characteristics) })));
api.MapPost("/demo/agent-proposals", async (DemoAgentRequest request, HttpContext http,
    IActorDirectory actors, IAgentModel agent, WorkflowService workflow, CancellationToken token) =>
{
    var actor = RequireActor(http, actors);
    if (actor.Role != Role.Agent)
        throw new WorkflowException("permission_denied", "Only the fixed demo agent can use this route.", 403);
    var suggestion = agent.Suggest(request.Scenario);
    var proposal = await workflow.CreateAsync(actor, suggestion.Action, suggestion.Target,
        suggestion.Reason, suggestion.Evidence, token);
    return Results.Created($"/api/proposals/{proposal.Id}", ProposalView.From(proposal));
});

app.Run();

static Actor RequireActor(HttpContext http, IActorDirectory actors)
{
    var id = http.Request.Headers["X-Demo-Actor-Id"].ToString();
    return actors.Find(id) ?? throw new WorkflowException("unknown_actor",
        "Supply a fixed X-Demo-Actor-Id from GET /api/actors.", 403);
}

public sealed record CreateProposalRequest(string Action, string Target, string Reason, string Evidence);
public sealed record DemoAgentRequest(string Scenario);
public sealed record ProposalView(
    Guid Id, Guid CorrelationId, string ProposerId, Role ProposerRole, ActionKind Action,
    string Target, string Reason, string Evidence, string RequiredScope, RiskLevel Risk,
    bool Reversible, ProposalStatus Status, DateTime CreatedAtUtc, PolicyView? Policy,
    ApprovalView? Approval, ExecutionView? Execution)
{
    public static ProposalView From(ActionProposal p) => new(p.Id, p.CorrelationId, p.ProposerId,
        p.ProposerRole, p.Action, p.Target, p.Reason, p.Evidence, p.RequiredScope, p.Risk,
        p.Reversible, p.Status, p.CreatedAtUtc,
        p.PolicyDecision is { } d ? new(d.Outcome, d.Reason, d.PolicyName, d.DecidedAtUtc) : null,
        p.Approval is { } a ? new(a.Status, a.BoundAction, a.BoundTarget, a.BoundScope,
            a.RequestedAtUtc, a.ExpiresAtUtc, a.DecidedById, a.DecidedAtUtc) : null,
        p.Execution is { } e ? new(e.Id, e.Status, e.ExecutorId, e.StartedAtUtc,
            e.CompletedAtUtc, e.Result) : null);
}
public sealed record PolicyView(PolicyOutcome Outcome, string Reason, string PolicyName, DateTime DecidedAtUtc);
public sealed record ApprovalView(ApprovalStatus Status, ActionKind BoundAction, string BoundTarget,
    string BoundScope, DateTime RequestedAtUtc, DateTime ExpiresAtUtc, string? DecidedById,
    DateTime? DecidedAtUtc);
public sealed record ExecutionView(Guid Id, ExecutionStatus Status, string ExecutorId,
    DateTime StartedAtUtc, DateTime? CompletedAtUtc, string Result);
public sealed record AuditView(long Id, Guid CorrelationId, Guid ProposalId, string ActorId,
    string EventType, DateTime OccurredAtUtc, string MetadataJson);
public partial class Program;
