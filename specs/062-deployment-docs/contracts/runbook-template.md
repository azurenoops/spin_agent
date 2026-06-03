# Contract: Runbook Template (Epic 062)

This template defines the required structure for every failure mode entry in `docs/runbook.md`.
Each failure mode section must follow this structure exactly. Do not omit sections; if a
section has no content, write "N/A — see escalation."

---

## Template

```markdown
## [FM-N] <Failure Mode Name>

**Severity**: Critical / High / Medium  
**Typical MTTR**: <estimated time to resolve>  
**Related Monitoring Alert**: <alert name in Azure Monitor or equivalent, if configured>

### Symptoms

Observable signs that this failure mode is active. These are things an engineer or end user
would notice, not internal system states.

- Symptom 1 (e.g., "All API calls return HTTP 500")
- Symptom 2 (e.g., "Azure Monitor alert fires for high error rate")
- Symptom 3 (e.g., "Teams bot shows 'Sorry, I ran into an issue'")

### Diagnosis

Step-by-step commands to confirm you are in this failure mode. Each step includes the expected
output for a confirmed failure.

**Step 1 — <description>**
```bash
<command>
```
Expected output (failure): `<what you see when this failure mode is confirmed>`  
Expected output (passing): `<what you see when healthy>`

**Step 2 — <description>**
```bash
<command>
```
Expected output (failure): `<...>`

*(Repeat for all diagnosis steps)*

### Remediation

Ordered steps to resolve the failure. Each step is self-contained and includes a verification.

**Step 1 — <action>**
```bash
<command>
```
Verify: `<how to confirm this step worked>`

**Step 2 — <action>**
```bash
<command>
```
Verify: `<...>`

*(Repeat for all remediation steps)*

After all steps, run the smoke test to verify recovery:
```bash
BASE_URL=<deployed-url> bash scripts/smoke-test.sh
```

### Rollback

How to revert to the last known-good state if remediation fails.

```bash
<rollback command(s)>
```

Or: "Redeploy the previous container image tag from the Azure Container Apps revision list."

### Escalation

Who to contact if remediation does not resolve the issue within `<time>`.

| Contact | Role | When to Escalate |
|---|---|---|
| `<name or team>` | Engineering Lead | After 30 minutes without resolution |
| `<name or team>` | Azure Support | If Azure service outage is suspected |
| `<name or team>` | Security Lead | If auth failure may indicate a security incident |
```

---

## Runbook Header Template

The `docs/runbook.md` file must begin with:

```markdown
# ATO Copilot Operations Runbook

**Last Updated**: YYYY-MM-DD  
**Scope**: Production incidents for the ATO Copilot API, MCP server, and M365 Teams bot  
**Deployment Guide**: [docs/deployment.md](./deployment.md)  
**Smoke Test**: [scripts/smoke-test.sh](../scripts/smoke-test.sh)

> This runbook covers the top 5 failure modes. For failures not covered here, collect logs
> from Azure Container Apps, check Azure Service Health, and escalate per the contact list below.

## Quick Reference

| Symptom | Go to |
|---|---|
| All API calls return 500 | [§ MCP Server Crash](#fm-1-mcp-server-crash) |
| ... | ... |

## Escalation Contacts

| Role | Contact | Response SLA |
|---|---|---|
| Engineering Lead | `<placeholder>` | 30 minutes |
| Azure Support | [Azure Portal → Support](https://portal.azure.com) | Per support plan |
| Security Lead | `<placeholder>` | 15 minutes (auth incidents) |
```

---

## Quality Bar for Each Runbook Entry

A runbook entry is considered complete when all of the following are true:

1. **Symptoms are observable without internal access** — an end user or monitoring system could
   report them.
2. **Diagnosis steps include expected output** — an engineer can confirm the failure mode
   without guessing.
3. **Remediation steps are copy-pasteable** — no step requires the engineer to fill in a value
   they don't have (use `<placeholder>` syntax for values that vary by environment).
4. **Rollback is defined** — "redeploy previous image" is an acceptable rollback if no better
   option exists.
5. **Escalation contacts are listed** — even if the contact is a team name or a support plan.
