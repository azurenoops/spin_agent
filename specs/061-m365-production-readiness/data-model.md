# Data Model: M365 Teams Extension (Epic 061)

There is no database schema for the M365 bot itself — it delegates all persistence to the
ATO Copilot API. This document covers two persistence concerns the bot owns:

1. **Environment variable schema** — the configuration contract (see also `contracts/env-schema.md`)
2. **Identity store state model** — the shape of SSO token records persisted by
   `ConversationStateIdentityStore`

---

## 1. Environment Variable Schema

See `contracts/env-schema.md` for the full table with types, required/optional, default values,
and validation rules.

Summary of required variables:

| Variable | Type | Required | Default |
|---|---|---|---|
| `ATO_API_URL` | URL string | ✅ Yes | — |
| `BOT_ID` | UUID string | ✅ Yes (auth) | — |
| `BOT_PASSWORD` | string | ✅ Yes (auth) | — |
| `PORT` | integer 1–65535 | No | `3978` |
| `ATO_API_KEY` | string | No | — |
| `AUTH_TEAMS_SSO_MODE` | enum | No | `Disabled` |
| `AUTH_TEAMS_SSO_CONNECTION_NAME` | string | Conditional | — |
| `IDENTITY_STORE_BACKEND` | enum | No | `memory` |
| `AZURE_STORAGE_CONNECTION_STRING` | string | Conditional | — |
| `REDIS_URL` | URL string | Conditional | — |
| `IDENTITY_STORE_ENCRYPTION_KEY` | base64 string (32 bytes) | Conditional | — |
| `SSE_TIMEOUT_MS` | integer ≥ 5000 | No | `25000` |

---

## 2. Identity Store State Model

### Purpose

The identity store persists SSO token records so that authenticated user sessions survive bot
restarts and Container Apps scaling events.

### Token Record Shape

```typescript
interface StoredTokenRecord {
  // Partition key (Azure Table) or hash key (Redis)
  conversationId: string;      // Bot Framework conversation ID
  userId: string;              // AAD object ID of the Teams user

  // Token payload (stored encrypted at rest)
  accessToken: string;         // AAD access token
  tokenType: string;           // Always "Bearer"
  expiresOnMs: number;         // Unix timestamp (ms) when token expires
  scopes: string[];            // OAuth scopes granted

  // Metadata
  storedAtMs: number;          // Unix timestamp (ms) when record was written
  connectionName: string;      // AUTH_TEAMS_SSO_CONNECTION_NAME value
}
```

### Azure Table Storage Layout

- **Table name**: `AtoCopilotBotTokens` (configurable via `IDENTITY_STORE_TABLE_NAME`)
- **PartitionKey**: `conversationId`
- **RowKey**: `userId`
- **Properties**:
  - `encryptedPayload` (string) — AES-256-GCM encrypted JSON of `StoredTokenRecord` (minus
    `conversationId` and `userId`)
  - `iv` (string) — base64-encoded 12-byte IV used for encryption
  - `expiresOnMs` (int64) — stored in plaintext for TTL-based expiry queries
  - `connectionName` (string)

### Redis Layout

- **Key pattern**: `ato:bot:token:{conversationId}:{userId}`
- **Value**: AES-256-GCM encrypted JSON of the full `StoredTokenRecord`
- **TTL**: set to `Math.floor((expiresOnMs - Date.now()) / 1000)` seconds at write time

### Encryption

- Algorithm: AES-256-GCM
- Key: 32-byte key from `IDENTITY_STORE_ENCRYPTION_KEY` (base64-encoded)
- IV: 12 random bytes generated per write
- Auth tag: appended to ciphertext and verified on decrypt
- If decryption fails (tampered record or wrong key): `getToken()` returns `null` and logs a
  WARNING. It does not throw.

### Expiry Handling

- Tokens with `expiresOnMs < Date.now()` are treated as absent (`getToken()` returns `null`).
- The store does not proactively delete expired records. Expired records accumulate and are
  cleaned up by:
  - Azure Table Storage: a nightly Azure Function or Logic App (out of scope for this epic).
  - Redis: TTL-based automatic expiry (handled by Redis itself).

### In-Memory Backend (test / dev)

```typescript
interface MemoryStore {
  // Map key: `${conversationId}:${userId}`
  records: Map<string, StoredTokenRecord>;
}
```

No encryption is applied in the memory backend. All records are lost on process restart by
design.

---

## 3. Card State (stateless)

All Adaptive Card payloads are generated on-the-fly from API responses. There is no
client-side card state stored in the bot. Card types (authorizationCard, complianceCard,
dashboardCard, kanbanBoardCard, etc.) are pure render functions that accept API data and return
an `Attachment` object.
