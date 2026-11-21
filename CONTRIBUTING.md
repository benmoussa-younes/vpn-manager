# Contributing

The project is maintained by its owner. Issues that state a reproduction, the
Debian release and the observed behaviour are welcome. A pull request is
accepted when it was agreed in an issue first, keeps the gate green and adds the
test that failed before the change.

## Workflow

1. Branch from `main`: `git switch -c work/<slug>`.
2. Write the smallest case that fails for the expected reason.
3. Keep the change small; run `dotnet build VpnManager.slnx -c Release`,
   `dotnet test VpnManager.slnx -c Release --no-build` and
   `scripts run tools/scripts/binaries/gate.scs`.
4. Open a pull request; the owner reviews and merges. A release is the owner's
   decision.

## Code

- C# only, `.NET 10`, Native AOT, warnings as errors.
- `Result<T>`/`CoreError` for an expected failure; an exception is a bug.
- No secret in a file, a log, a command line or a test fixture.
- English in code, comments, commit messages and repository files.

## Security

Do not open a public issue for a vulnerability; follow [`SECURITY.md`](SECURITY.md).
