# Security

Human Loop Lab is an educational reference implementation, not production-ready security infrastructure. It uses an unauthenticated demo actor header and simulated tools. Never expose it to an untrusted network, connect its demo approval path to real infrastructure, or commit real credentials or secrets.

Simulated executors must remain non-destructive by default. Changes that introduce real execution require a separate threat model and explicit maintainer review. If you discover a vulnerability in this repository, do not publish exploit details in an issue. Use GitHub's private security reporting after that mechanism is configured; no security email address or public remote exists yet.
