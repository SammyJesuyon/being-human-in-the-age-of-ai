# Contributing

Thanks for helping improve the lab. Once a public remote exists, fork it, create a focused branch, and open a pull request explaining the behavior changed and why. Until then, this is a local author-review repository.

Run `dotnet restore`, `dotnet build`, and `dotnet test` before proposing code changes. Add tests for policy, approval, state-transition, and idempotency changes. Keep simulated executors non-destructive and default operation credential-free.

To add an action, update the action catalog, required scope/risk/reversibility policy, simulator, API examples and tests. To add a policy, keep it in the application policy boundary, document the new authority rule and test both allowed and denied paths. To add a model adapter, follow [the provider guide](docs/adding-an-ai-provider.md); model output must not become authorization. Documentation and accessibility improvements are welcome.

Please avoid copying substantial book prose into code or docs. Follow the [code of conduct](CODE_OF_CONDUCT.md) and report suspected security issues privately when a repository security-reporting mechanism is configured.
