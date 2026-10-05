using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HumanLoopLab.IntegrationTests;

public sealed class WorkflowApiTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"humanlooplab-test-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public WorkflowApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databasePath}"));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task SwaggerDocumentLoads()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LowRiskAllowedActionExecutes()
    {
        var id = await Create("developer-1", "ReadLogs");
        Assert.Equal("Evaluated", await Evaluate(id, "developer-1"));
        var result = await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "developer-1", key: "read-1");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("SIMULATED", await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingScopeProducesDenyAndCannotExecute()
    {
        var id = await Create("observer-1", "DraftCodePatch");
        Assert.Equal("Denied", await Evaluate(id, "observer-1"));
        var response = await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "observer-1", key: "denied-1");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HighRiskRequiresApprovalBeforeExecution()
    {
        var id = await Create("agent-1", "DeployToProduction");
        Assert.Equal("PendingApproval", await Evaluate(id, "agent-1"));
        var response = await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "agent-1", key: "deploy-1");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("approval_required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RejectedHighRiskActionCannotExecute()
    {
        var id = await Create("agent-1", "DeployToProduction");
        await Evaluate(id, "agent-1");
        var reject = await Send(HttpMethod.Post, $"/api/proposals/{id}/reject", "approver-1");
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var execute = await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "agent-1", key: "rejected-1");
        Assert.Equal(HttpStatusCode.Conflict, execute.StatusCode);
    }

    [Fact]
    public async Task ApprovedActionExecutesOnlyOnceWithSameKey()
    {
        var id = await Create("agent-1", "DeployToProduction");
        await Evaluate(id, "agent-1");
        var approval = await Send(HttpMethod.Post, $"/api/proposals/{id}/approve", "approver-1");
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var first = await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "agent-1", key: "deploy-once");
        var second = await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "agent-1", key: "deploy-once");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False((await Json(first)).GetProperty("duplicateSuppressed").GetBoolean());
        Assert.True((await Json(second)).GetProperty("duplicateSuppressed").GetBoolean());
        var audit = await _client.GetStringAsync($"/api/proposals/{id}/audit");
        Assert.Equal(1, Count(audit, "ExecutionStarted"));
        Assert.Equal(1, Count(audit, "DuplicateExecutionSuppressed"));
    }

    [Fact]
    public async Task ExecutedProposalRejectsSecondExecutionWithDifferentKey()
    {
        var id = await Create("agent-1", "DeployToProduction");
        await Evaluate(id, "agent-1");
        var approval = await Send(HttpMethod.Post, $"/api/proposals/{id}/approve", "approver-1");
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var first = await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "agent-1", key: "first-deploy");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "agent-1", key: "different-deploy");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("already_executed", await second.Content.ReadAsStringAsync());
        var audit = await _client.GetStringAsync($"/api/proposals/{id}/audit");
        Assert.Equal(1, Count(audit, "ExecutionStarted"));
        Assert.Equal(1, Count(audit, "ExecutionSucceeded"));
    }

    [Fact]
    public async Task AgentCannotApproveItsOwnProposal()
    {
        var id = await Create("agent-1", "DeployToProduction");
        await Evaluate(id, "agent-1");
        var response = await Send(HttpMethod.Post, $"/api/proposals/{id}/approve", "agent-1");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HumanProposerCannotApproveTheirOwnHighRiskProposal()
    {
        var id = await Create("admin-1", "DeployToProduction");
        await Evaluate(id, "admin-1");
        var response = await Send(HttpMethod.Post, $"/api/proposals/{id}/approve", "admin-1");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IdempotencyKeyCannotBeReusedForAnotherProposal()
    {
        var firstId = await Create("developer-1", "ReadLogs");
        await Evaluate(firstId, "developer-1");
        await Send(HttpMethod.Post, $"/api/proposals/{firstId}/execute", "developer-1", key: "shared-key");
        var secondId = await Create("developer-1", "ReadLogs");
        await Evaluate(secondId, "developer-1");
        var response = await Send(HttpMethod.Post, $"/api/proposals/{secondId}/execute", "developer-1", key: "shared-key");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AgentCannotModifyAccessPolicy()
    {
        var id = await Create("agent-1", "ModifyAccessPolicy");
        Assert.Equal("Denied", await Evaluate(id, "agent-1"));
        var response = await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "agent-1", key: "policy-1");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuditAndResponsibilityShowFullLifecycle()
    {
        var id = await Create("agent-1", "DeployToProduction");
        await Evaluate(id, "agent-1");
        await Send(HttpMethod.Post, $"/api/proposals/{id}/approve", "approver-1");
        await Send(HttpMethod.Post, $"/api/proposals/{id}/execute", "agent-1", key: "trace-1");
        var audit = await _client.GetStringAsync($"/api/proposals/{id}/audit");
        foreach (var name in new[] { "ProposalCreated", "PolicyEvaluated", "ApprovalRequested",
                     "ProposalApproved", "ExecutionStarted", "ExecutionSucceeded" })
            Assert.Contains(name, audit);
        var trace = await _client.GetFromJsonAsync<JsonElement>($"/api/proposals/{id}/responsibility");
        Assert.Equal("agent-1", trace.GetProperty("proposerId").GetString());
        Assert.Equal("approver-1", trace.GetProperty("approvalDeciderId").GetString());
        Assert.Equal("agent-1", trace.GetProperty("executorId").GetString());
    }

    [Fact]
    public async Task UnknownActionFailsSafely()
    {
        var response = await Send(HttpMethod.Post, "/api/proposals", "developer-1",
            new { action = "TeleportDatabase", target = "demo", reason = "test", evidence = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdviceIncludesReasons()
    {
        var response = await _client.PostAsJsonAsync("/api/delegation/advice", new
        {
            consequence = "Low", reversible = true, responsibilityCritical = false,
            requiresHumanJudgment = false, auditable = true, skillMaintenanceImportant = false,
            frequent = true, skillAtrophyRisk = false
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = await Json(response);
        Assert.Equal("Delegate", value.GetProperty("suggestedMode").GetString());
        Assert.NotEqual(0, value.GetProperty("reasons").GetArrayLength());
    }

    private async Task<Guid> Create(string actor, string action)
    {
        var response = await Send(HttpMethod.Post, "/api/proposals", actor,
            new { action, target = "demo-resource", reason = "Demonstrate bounded authority.", evidence = "Demo evidence." });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<string> Evaluate(Guid id, string actor)
    {
        var response = await Send(HttpMethod.Post, $"/api/proposals/{id}/evaluate", actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("status").GetString()!;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string actor,
        object? body = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Demo-Actor-Id", actor);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static int Count(string text, string item) =>
        text.Split(item, StringSplitOptions.None).Length - 1;

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
