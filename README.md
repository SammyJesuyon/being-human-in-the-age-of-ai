# Human Loop Lab

Companion engineering repository for *Being Human in the Age of Artificial Intelligence* by Samson Kitigo.

How can an AI-capable system be useful without treating capability as authority, or human approval as a ceremonial checkbox? Human Loop Lab is a runnable .NET 8 reference application for exploring that question. It models proposals, scoped policy decisions, bounded human approval, simulated execution, audit events, responsibility traces, and explicit delegation modes.

The book is licensed separately under CC BY 4.0. This software is MIT-licensed. A publication link will be added when the public book record exists. No GitHub remote or book URL has been assigned yet.

## What it demonstrates

- Fixed demo actors with configurable role scopes; no agent administrator role.
- A deny-by-default policy with `Allow`, `Deny`, and `RequireHumanApproval` outcomes.
- High-risk, non-reversible actions requiring a distinct human approver.
- Approval bound to the exact action, target, scope, and 30-minute window.
- Explicit proposal states; rejected and denied proposals cannot execute.
- Idempotency keys that suppress repeated simulated execution.
- Append-oriented audit events and a separate responsibility trace.
- An illustrative `DelegationAdvisor`: Retain, Augment, Delegate, Periodically Reclaim.
- A deterministic local agent adapter, with no API key, network model, cloud account, or paid service.

All operational actions are **simulated**. The only real database writes are to the lab's local SQLite state. This is not production-ready identity, authorization, deployment, or AI-safety infrastructure.

## Quick start

Requires the .NET 8 SDK. The placeholder URL below must be replaced only after a real GitHub repository exists. If you already have this directory locally, begin at `cd`.

```sh
git clone <repository-url>
cd being-human-ai-companion
dotnet restore
dotnet test
dotnet run --project src/HumanLoopLab.Api
```

Open [Swagger UI](http://localhost:5080/swagger). The API creates/migrates `humanlooplab.db` on first startup; the file is ignored by Git. No separate database initialization is needed. The app listens on port 5080 through its development launch profile. For terminal-driven requests, see [examples/requests.http](examples/requests.http).

## A short walkthrough

1. `POST /api/demo/agent-proposals` with scenario `deploy` and `X-Demo-Actor-Id: agent-1` creates a production-deployment **proposal**, not a deployment.
2. `POST /api/proposals/{id}/evaluate` with the same actor returns `PendingApproval`: the agent has a bounded proposal scope, but high risk requires separate human authority.
3. Execution before approval returns `409 approval_required`.
4. `approver-1` reviews the full proposal and calls `POST /api/proposals/{id}/approve`. The recorded decision is bound to its action, target, and scope.
5. The agent calls `POST /api/proposals/{id}/execute` with an `Idempotency-Key`. The result is explicitly `SIMULATED`.
6. Repeating that request with the same key returns the stored result with `duplicateSuppressed: true`; no second execution starts.
7. `GET /api/proposals/{id}/audit` and `/responsibility` show what happened and who participated.

For the denied path, propose `ModifyAccessPolicy` as `agent-1`, then evaluate. Policy `agent-no-self-authorization` denies it even if someone later adds that scope to the agent. A denied proposal cannot execute.

The `X-Demo-Actor-Id` header selects one of six fixed educational actors. **It is not authentication.** Anyone with API access can impersonate a demo actor. Do not expose this API to an untrusted network or connect it to real tools. The approval click illustrates an authority transition; it cannot prove that the reviewer understood the action.

## API overview

| Route | Purpose |
| --- | --- |
| `GET /api/actions`, `/api/actors`, `/api/policies` | Inspect demo catalog and policy setup |
| `POST /api/proposals`, `GET /api/proposals/{id}` | Create and inspect a proposal |
| `POST /api/proposals/{id}/evaluate` | Apply scoped policy |
| `POST /api/proposals/{id}/approve`, `/reject` | Human decision |
| `POST /api/proposals/{id}/execute` | Simulate once; requires `Idempotency-Key` |
| `GET /api/proposals/{id}/audit` | Read append-oriented events |
| `GET /api/proposals/{id}/responsibility` | Inspect proposer, policy, approver, executor, resource and result |
| `POST /api/delegation/advice`, `GET /api/delegation/scenarios` | Explore illustrative delegation modes |
| `POST /api/demo/agent-proposals` | Deterministic local demo-agent suggestion |

Error responses use Problem Details and distinguish missing proposals, denied permission, missing approval, rejected/executed state, unknown actions, and idempotency conflicts. See Swagger for request schemas.

## Architecture

```text
src/HumanLoopLab.Domain          States, actions, actor and audit models
src/HumanLoopLab.Application     Workflow, policy, delegation advice, ports
src/HumanLoopLab.Infrastructure  EF Core/SQLite, fixed actor directory, simulators
src/HumanLoopLab.Api             HTTP boundary, DTOs, Swagger, composition
tests/                           Domain, application and API tests
docs/                            Architecture, book map, threat model, provider guide
examples/                        Runnable HTTP requests
```

The API depends inward on application/domain behavior. The application layer depends on interfaces; Infrastructure provides SQLite storage and the deterministic agent. See [architecture.md](docs/architecture.md) for the state machine, authorization boundary, and idempotency details.

Run the tests and build with:

```sh
dotnet restore HumanLoopLab.sln
dotnet build HumanLoopLab.sln --no-restore
dotnet test HumanLoopLab.sln --no-build
```

Docker is optional: `docker build -t human-loop-lab .` followed by `docker run --rm -p 8080:8080 human-loop-lab`. The container's SQLite file is ephemeral unless you mount `/app/data` as a volume. Swagger is enabled only in Development; use `-e ASPNETCORE_ENVIRONMENT=Development` to view it in a local-only demo container. Do not publish that demo endpoint publicly.

## Limits and extension points

This app does not solve accountability, human competence, model reliability, or organizational governance. The responsibility trace attributes recorded actions; it does not decide moral responsibility. The delegation advisor is an illustrative policy, not a universal formula or scientific score. A real system needs authentication, tamper-resistant audit storage, distributed locking/transactions, carefully reviewed policy administration, real review interfaces, operational monitoring, and a threat assessment. See [threat-model.md](docs/threat-model.md).

To add a provider, implement `IAgentModel` outside the application core; see [adding-an-ai-provider.md](docs/adding-an-ai-provider.md). Keep authorization and execution gates outside the model. To contribute, read [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## License

Code: [MIT](LICENSE), Copyright (c) 2026 Samson Kitigo. Book: CC BY 4.0, separately licensed; no book manuscript or production assets are included here.
