# TODO

Security/reliability/operational hardening items live in [BACKLOG.md](BACKLOG.md) —
as of 2026-07-09 every item there is resolved (S1–S6, R1–R6, O1–O5 all ✅ DONE). This file
tracks everything else. Completed items are pruned; history lives in git.

## Verify the E2E suite on Windows

The LegacyGateway E2E fixture's Windows path (`pwsh start.ps1` / `-Stop`) is verified by
code reading only — `Start-Process` with file redirection detaches child handles, so
it should not have the pipe-inheritance hang the Makefile had. Run
`dotnet test tests\ConduitSharp.LegacyGateway.E2E.Tests` on a Windows machine once to confirm.
