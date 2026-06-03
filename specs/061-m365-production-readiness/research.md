# Research: M365 Teams Extension Production Readiness (Epic 061)

## 1. CI Path Filter Approach

**GitHub Actions path filtering** can be implemented two ways:

**Option A — Native `paths` filter** (simplest):
```yaml
on:
  push:
    paths:
      - 'extensions/m365/**'
  pull_request:
    paths:
      - 'extensions/m365/**'
```
Limitation: if the job is required for branch protection, a PR that does NOT touch
`extensions/m365/**` will have the job skipped (not passing), which can block merge unless the
branch protection rule uses "Require status checks to pass" with the "Allow skipping" option or
`dorny/paths-filter` is used to emit a skip signal.

**Option B — `dorny/paths-filter`** (recommended when job is required):
```yaml
- uses: dorny/paths-filter@v3
  id: filter
  with:
    filters: |
      m365:
        - 'extensions/m365/**'
- if: steps.filter.outputs.m365 == 'true'
  run: ...
```
This allows the job to report "success (skipped)" to branch protection.

**Decision**: Use Option B for the required job. Native path filter for an optional build-Docker
job.

---

## 2. Azure Table Storage Token Persistence

**SDK**: `@azure/data-tables` v13.x (GA)  
**Key operations**:
- `TableClient.upsertEntity()` — create or replace a token record
- `TableClient.getEntity()` — retrieve by partition + row key
- `TableClient.deleteEntity()` — delete on sign-out

**Throughput**: Azure Table Storage supports ~20,000 RPS per account. Bot token operations are
infrequent (once per conversation initiation); throughput is not a concern.

**Cost**: Table Storage is ~$0.045/GB/month + $0.00036/10K operations. Token records are <1 KB
each; cost is negligible.

**Alternative — Azure Cosmos DB Table API**: Drop-in replacement for Table Storage with
geo-replication and SLA guarantees. Worth noting but out of scope for this epic.

---

## 3. AES-256-GCM Encryption in Node.js

Node.js `crypto` module provides native AES-256-GCM without additional dependencies:

```typescript
import { createCipheriv, createDecipheriv, randomBytes } from 'crypto';

function encrypt(plaintext: string, key: Buffer): { iv: string; ciphertext: string } {
  const iv = randomBytes(12);
  const cipher = createCipheriv('aes-256-gcm', key, iv);
  const encrypted = Buffer.concat([cipher.update(plaintext, 'utf8'), cipher.final()]);
  const authTag = cipher.getAuthTag();
  return {
    iv: iv.toString('base64'),
    ciphertext: Buffer.concat([encrypted, authTag]).toString('base64'),
  };
}

function decrypt(ciphertext: string, iv: string, key: Buffer): string {
  const data = Buffer.from(ciphertext, 'base64');
  const authTag = data.subarray(data.length - 16);
  const encrypted = data.subarray(0, data.length - 16);
  const decipher = createDecipheriv('aes-256-gcm', key, Buffer.from(iv, 'base64'));
  decipher.setAuthTag(authTag);
  return Buffer.concat([decipher.update(encrypted), decipher.final()]).toString('utf8');
}
```

Key derivation: `IDENTITY_STORE_ENCRYPTION_KEY` must be a base64-encoded 32-byte value.
Operators should generate it with `openssl rand -base64 32`.

---

## 4. Bot Framework SSE + Teams Proxy Timeout

Teams routes bot messages through its own HTTPS proxy. This proxy enforces a ~30-second
response timeout. For a standard Bot Framework `invoke` activity, if the bot does not respond
within 30 seconds, Teams shows a generic error to the user.

**Mitigation strategies**:

1. **Immediate ACK + proactive message** (recommended): The bot returns an empty `202 Accepted`
   to the Teams proxy within 1 second, then sends the result as a proactive message when ready.
   Requires the bot to have `serviceUrl` + conversation reference stored.

2. **Interim card at T-5s**: Send an interim Adaptive Card update at 25 seconds if the stream
   is still open. This resets the proxy timeout clock in some Bot Framework versions. **Not
   reliable** — the initial HTTP connection is already in progress; the proxy timeout applies to
   the response, not subsequent proactive calls.

3. **Chunked streaming** (experimental): Bot Framework has experimental streaming support
   (`botframework-streaming`). Not yet production-ready for all Teams clients.

**Decision**: Use Strategy 1 (immediate ACK + proactive message) as the primary pattern.
Strategy 2 (interim card) is applied as a UX improvement within the 25-second window to avoid
showing a blank state in the Adaptive Card.

---

## 5. Multi-Stage Docker Build Size Analysis

Base image `node:20-alpine`: ~180 MB
After `npm ci` (all deps): +150 MB approx
After pruning to prod deps (`npm ci --omit=dev`): +40–80 MB
Compiled TypeScript output: +5–15 MB

**Estimated final image**: ~220–260 MB — well under the 300 MB target.

---

## 6. Azure Container Apps for Bot Framework

Key requirements for Bot Framework bots on Container Apps:
- **HTTPS required**: Bot Framework validates the messaging endpoint uses HTTPS.
  Container Apps provides a built-in HTTPS ingress; no custom certificate needed.
- **Min replicas = 1**: Bot Framework expects the messaging endpoint to always be available.
  Scale-to-zero causes cold starts that may time out before Teams resends the message.
  Set `minReplicas: 1`.
- **Sticky sessions**: Not required. Bot Framework conversation state is stored externally
  (identity store); any replica can handle any request.
- **Health probe**: Container Apps supports HTTP health probes. Point to `GET /health`.

---

## 7. Existing Test File Inventory

| File | Description |
|---|---|
| `test/authorizationCard.test.ts` | Adaptive Card rendering |
| `test/complianceCard.test.ts` | Adaptive Card rendering |
| `test/dashboardCard.test.ts` | Adaptive Card rendering |
| `test/kanbanBoardCard.test.ts` | Adaptive Card rendering |
| `test/sseClient.test.ts` | SSE streaming client |
| `test/auth/*.test.ts` | SSO dispatcher tests |
| (+ 11 more) | Other card and integration tests |

17 files total. All use mocha. None currently run in CI.
