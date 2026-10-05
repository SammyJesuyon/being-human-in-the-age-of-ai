# Threat model (educational demo)

The trusted boundary is the application policy/execution gate. Model suggestions and HTTP input are untrusted. The `X-Demo-Actor-Id` header is **not authenticated**, so this lab must be local-only. SQLite is a convenience store, not a tamper-proof log.

| Threat | Consequence | Demo mitigation | Production requirement beyond demo |
| --- | --- | --- | --- |
| Privilege escalation / excessive permissions | Agent gains high-impact tools | Fixed role scopes; agent lacks policy scope | Trusted IAM, least-privilege reviews, short-lived credentials |
| Agent self-authorization | Agent changes its own permissions | Explicit deny in `PolicyEngine` | Separate policy administration, independent change control |
| Forged approval | High-risk action appears human-approved | Fixed approver role and separation checks only | Real authentication, signed/verified identity, review UI, MFA as appropriate |
| Replay or duplicate execution | Consequential action runs twice | Required idempotency key, unique SQLite record, local gate | Durable cross-instance idempotency and tool-level deduplication |
| Missing or tampered audit | Incident cannot be reconstructed | Append-oriented events for transitions | Immutable/remote audit store, integrity checks, retention and alerting |
| Policy tampering | Unsafe action becomes allowed | Policy outside model, agent policy-change deny | Authorized config pipeline, code review, integrity controls |
| Unauthorized execution | Tool runs without grant | Policy/approval rechecked at execution | Defense in depth at actual tool/service boundary |
| Stale approval or changed target | Approval reused outside intent | 30-minute expiry and action/target/scope binding | Versioned action payloads and transaction-bound approvals |
| Malicious or incorrect model proposal | Unsafe target/reason | Catalog and boundary validation; simulators only | Content validation, monitoring, red-teaming, human competence and escalation |

Approval in this demo changes system state, but the header can be forged and the interface cannot ensure an approver understands the change. The lab demonstrates architecture, not security assurance or moral accountability.
