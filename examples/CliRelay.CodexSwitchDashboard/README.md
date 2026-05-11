# CliRelay Management Dashboard (C#)

Standalone Windows Forms management frontend for CliRelay's management API.

What it does:

- Connects to any CliRelay management endpoint with `base URL + management key`
- Shows dashboard KPI cards, system stats, Codex fingerprint summary, and runtime toggle state
- Lists auth entries from `GET /v0/management/auth-files`
- Lets you filter auth rows by provider or Codex-only view
- Lets you enable/disable auth entries, edit common auth fields, and trigger `POST /v0/management/quota/reconcile`
- Lists available models from `GET /v0/management/models`
- Shows recent usage logs from `GET /v0/management/usage/logs`
- Shows system log lines from `GET /v0/management/logs`

Run locally:

```powershell
dotnet run --project .\examples\CliRelay.CodexSwitchDashboard\CliRelay.CodexSwitchDashboard.csproj
```

Default management base URL is `http://127.0.0.1:8317`.

The dashboard does not persist the management key locally. Enter it each time before refreshing.

Note:

- This example is intentionally separate from `/manage`.
- The production management web UI is maintained in the separate panel repository referenced by `remote-management.panel-github-repository`.
