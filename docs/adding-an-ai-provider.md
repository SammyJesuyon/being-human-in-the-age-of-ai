# Adding an AI provider

The default `DeterministicDemoAgent` implements `IAgentModel` and returns fixed suggestions for `incident`, `patch`, `deploy`, and `logs`. It makes the app runnable without network access, credentials, or paid services.

To integrate a real model, add an Infrastructure adapter implementing `IAgentModel`; register it in API composition instead of the demo adapter. Parse provider output into a bounded `DemoSuggestion` (or a reviewed replacement contract), reject unknown actions, enforce target/reason/evidence limits, and never treat text from the model as policy or approval. Store credentials outside source control and avoid recording prompt contents or secrets in audit events.

Keep these responsibilities separate:

1. The provider proposes an action.
2. `ActionCatalog` and `PolicyEngine` determine its required scope and whether it may proceed.
3. A distinct human decides any required approval.
4. `WorkflowService` checks persisted authority immediately before calling a simulator/tool.

Do not give the provider adapter direct database, policy, deployment, or administrator access. If replacing simulators with real tools, treat that as a new security project: design authentication, tool-specific scopes/idempotency, monitoring, rollback, review UI, secret handling, and threat-model updates first. This repository intentionally does not implement real tool execution.
