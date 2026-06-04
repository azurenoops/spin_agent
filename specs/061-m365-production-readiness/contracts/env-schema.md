# Contract: Environment Variable Schema (Epic 061)

This is the single source of truth for all environment variables consumed by the M365 Teams bot
(`extensions/m365/`). The `validateEnv()` function in `src/config.ts` must enforce every rule
in this table.

---

## Required Variables

Failure to provide any of these causes the bot to exit with code 1 before binding a port.

| Variable | Type | Validation Rule | Example |
|---|---|---|---|
| `ATO_API_URL` | URL string | Must parse as a valid URL with `http` or `https` scheme; no trailing slash required | `https://ato-api.example.com` |
| `BOT_ID` | UUID string | Must match UUID v4 pattern `[0-9a-f]{8}-...-4...` | `a1b2c3d4-...` |
| `BOT_PASSWORD` | string | Non-empty; length ≥ 1 | `s3cr3t!` |

> **Note on BOT_ID / BOT_PASSWORD**: If these are absent, the current code logs a WARNING and
> continues. **After T061-03 ships**, missing `BOT_ID` or `BOT_PASSWORD` must cause fail-fast
> exit. The bot cannot authenticate with Bot Framework without them.

---

## Optional Variables

These have defaults or are conditionally required based on the value of other variables.

| Variable | Type | Default | Validation Rule | Conditional Requirement |
|---|---|---|---|---|
| `PORT` | integer | `3978` | Integer in range 1–65535 | — |
| `ATO_API_KEY` | string | (none) | Non-empty if set | — |
| `AUTH_TEAMS_SSO_MODE` | enum | `Disabled` | Must be one of: `Disabled`, `Optional`, `Required` | — |
| `AUTH_TEAMS_SSO_CONNECTION_NAME` | string | (none) | Non-empty if set | Required when `AUTH_TEAMS_SSO_MODE` ≠ `Disabled` |
| `IDENTITY_STORE_BACKEND` | enum | `memory` | Must be one of: `memory`, `azure-table`, `redis` | — |
| `AZURE_STORAGE_CONNECTION_STRING` | string | (none) | Valid Azure Storage connection string | Required when `IDENTITY_STORE_BACKEND=azure-table` |
| `IDENTITY_STORE_TABLE_NAME` | string | `AtoCopilotBotTokens` | Non-empty if set; alphanumeric only | — |
| `REDIS_URL` | URL string | (none) | Must parse as `redis://` or `rediss://` URL | Required when `IDENTITY_STORE_BACKEND=redis` |
| `IDENTITY_STORE_ENCRYPTION_KEY` | base64 string | (none) | Must decode to exactly 32 bytes | Required when `IDENTITY_STORE_BACKEND` ≠ `memory` |
| `SSE_TIMEOUT_MS` | integer | `25000` | Integer ≥ 5000 | — |
| `NODE_ENV` | enum | `production` | One of: `development`, `test`, `production` | — |

---

## Fail-Fast Specification

### Triggering Conditions

`validateEnv()` collects errors and exits if **any** of the following are true:

1. `ATO_API_URL` is not set or does not parse as a valid URL.
2. `BOT_ID` is not set or does not match UUID format.
3. `BOT_PASSWORD` is not set.
4. `PORT` is set but is not an integer in 1–65535.
5. `AUTH_TEAMS_SSO_MODE` is set but is not one of the allowed enum values.
6. `AUTH_TEAMS_SSO_MODE` ≠ `Disabled` and `AUTH_TEAMS_SSO_CONNECTION_NAME` is not set.
7. `IDENTITY_STORE_BACKEND` is set but not one of the allowed enum values.
8. `IDENTITY_STORE_BACKEND=azure-table` and `AZURE_STORAGE_CONNECTION_STRING` is not set.
9. `IDENTITY_STORE_BACKEND=redis` and `REDIS_URL` is not set.
10. `IDENTITY_STORE_BACKEND` ≠ `memory` and `IDENTITY_STORE_ENCRYPTION_KEY` is not set or does
    not decode to 32 bytes.
11. `SSE_TIMEOUT_MS` is set but is not an integer ≥ 5000.

### Error Output Format

```
[ATO-M365] Configuration errors — bot cannot start:
  ✗ ATO_API_URL: required, not set
  ✗ BOT_ID: required, not set
  ✗ AUTH_TEAMS_SSO_CONNECTION_NAME: required when AUTH_TEAMS_SSO_MODE=Required, not set
```

### Exit Code

- `process.exit(1)` — misconfiguration
- `process.exit(0)` — clean shutdown
- `process.exit(2)` — unexpected internal error (uncaught exception)

### Startup Sequence

```
validateEnv()                    ← exits with 1 if any error
  ↓
createIdentityStore(process.env) ← initializes backend
  ↓
app.listen(PORT)                 ← only reached if validation passes
  ↓
console.log(`Listening on :${PORT}`)
```

---

## Environment-Specific Defaults

| Environment | `IDENTITY_STORE_BACKEND` | `NODE_ENV` |
|---|---|---|
| Local development | `memory` | `development` |
| CI | `memory` | `test` |
| Production (Azure) | `azure-table` | `production` |

---

## Secret Handling Rules

- `BOT_PASSWORD`, `ATO_API_KEY`, `AZURE_STORAGE_CONNECTION_STRING`, `REDIS_URL`,
  `IDENTITY_STORE_ENCRYPTION_KEY` must **never** appear in logs.
- `validateEnv()` must not log the values of these variables, only their names.
- Docker image must not bake in any secret values. All secrets are injected at runtime via
  Azure Container Apps secrets or Kubernetes secrets.
