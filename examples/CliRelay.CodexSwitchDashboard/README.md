# CliRelay Codex Switch Dashboard

Standalone Windows Forms companion for CliRelay's management API.

What it does:

- Lists auth entries from `GET /v0/management/auth-files`
- Highlights Codex auth rows, plan type, availability, and active restrictions
- Shows current `quota-exceeded` toggles
- Shows the current Codex identity fingerprint summary
- Lets you trigger `POST /v0/management/quota/reconcile` for the selected auth

Run locally:

```powershell
dotnet run --project .\examples\CliRelay.CodexSwitchDashboard\CliRelay.CodexSwitchDashboard.csproj
```

Default management base URL is `http://127.0.0.1:8317`.

Note:

- This example is intentionally separate from `/manage`.
- The production management web UI is maintained in the separate panel repository referenced by `remote-management.panel-github-repository`.
