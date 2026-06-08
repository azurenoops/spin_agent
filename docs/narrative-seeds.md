# Narrative Seed Documents — Pipeline Reference

**Feature:** Epic #222 (Spec 068) — Org Template & Narrative Seed Admin  
**Gate task:** #314 (Mr. Terrific)  
**Last updated:** 2026-06-08  
**Status:** Stub confirmed. Feature 014 hook required before claiming "Indexed" means AI improvement.

---

## 1. What Narrative Seeds Are

Narrative seed documents are administrator-uploaded reference files (existing SSPs, ConOps, mission descriptions, prior authorizations) that are intended to give the AI narrative generator organization-specific context when it drafts NIST control implementation statements.

Admins upload seeds via `/admin/templates` → "Narrative Seeds" tab. The system queues an indexing job and transitions the document from `Pending → Indexed` (or `Failed` on error).

---

## 2. Current State (v1 Stub — Confirmed by Task #314)

### What happens today

1. Admin uploads a seed document via `POST /api/onboarding/narrative-seeds`
2. `NarrativeSeedDocumentService` stores the file in blob storage under  
   `wizard/narrative-seeds/{tenantId}/{documentId}/{filename}`
3. A `WizardJobType.NarrativeSeedIndex` job is enqueued
4. `NarrativeSeedIndexJobHandler.ExecuteAsync` runs:
   - Loads the `NarrativeSeedDocument` from the DB
   - Sets `IndexingStatus = Indexed` and `UpdatedAt = UtcNow`
   - Saves and logs `"NarrativeSeed indexed (stub)"`
5. The UI badge transitions from **Pending** (yellow) → **Indexed** (green)

### What does NOT happen today

- **No blob read.** The handler does not read the document content from storage.
- **No chunking.** No text splitting or word-count logic.
- **No vector embedding.** No calls to any embedding model or vector store.
- **No AI prompt injection.** `NarrativeTemplateService.GenerateNarrativeWithAiAsync` does not query `NarrativeSeedDocumentService`, does not load seed text, and does not include seed content in the prompt sent to the chat client.

> ⚠️ **The "Indexed" badge currently means only that the record transitioned through the job queue without error. It does NOT mean the seed document influences AI narrative output in any way.**

This is the confirmed behavior as of task #314 investigation (2026-06-08).

---

## 3. AI Narrative Generation Pipeline (Current)

`NarrativeTemplateService.GenerateNarrativeWithAiAsync` builds the following prompt:

**System message:** `Ato.Copilot.Core.Prompts.NarrativeGeneration.prompt.txt`
> "You are an expert SSP author... Write a single control implementation narrative paragraph (100-300 words)..."

**User message** (built by `BuildAiUserPrompt`):
```
Control: {controlId} — {controlTitle}
Capability: {capabilityName}
Provider: {provider}
Description: {description}
Authorization Boundary: {boundaryName}          ← if present
Technology components: {Things}                 ← if present
Personnel components: {Persons}                 ← if present
Infrastructure components: {Places}             ← if present
```

**No seed text appears anywhere in this prompt.** Seeds are not wired in.

---

## 4. What Feature 014 Must Implement

Feature 014 (citation-aware narrative suggestions) is the intended hook. To make "Indexed" meaningful and seeds actually improve AI narratives, the following work is required:

### 4a. Indexing — `NarrativeSeedIndexJobHandler.cs`

Replace the stub body with a real indexing pipeline:

1. **Read document content from blob storage**  
   `IFileStorageProvider.GetAsync($"wizard/narrative-seeds/{tenantId}/{docId}/")` — the blob key prefix pattern is already documented in the handler's TODO comments. Requires persisting `OriginalFileName` on `NarrativeSeedDocument` so the exact key can be constructed (see `TODO(feat-014)` in the file).

2. **Parse document text**  
   Use a document extraction library (e.g., `DocumentFormat.OpenXml` for .docx, `iText` for .pdf) or a simple text reader for plain text. Write raw text to a local variable.

3. **Chunk into ~500-word segments**  
   The wave5-222 branch contained a more complete version of this handler with the chunking logic (`SplitIntoChunks`). That code should be ported here.

4. **Embed and store chunks**  
   Call the configured embedding model (via `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>` or equivalent) on each chunk. Store `(tenantId, documentId, chunkIndex, chunkText, embedding)` in a vector store or in a new `NarrativeSeedChunk` table with a `pgvector` column.

5. **Persist metadata**  
   Set `NarrativeSeedDocument.IndexedChunkCount = chunkCount`, `IndexedAt = UtcNow`, `IndexingStatus = Indexed`.

### 4b. Retrieval — `NarrativeTemplateService.cs`

Inject `INarrativeSeedDocumentService` (or a new `INarrativeSeedRetriever`) and modify `GenerateNarrativeWithAiAsync` to:

1. Query the vector store for the top-K chunks most similar to the control/capability description (embedding similarity search by tenant).
2. Append to the user prompt:

```
Reference Documents (from your organization's seed library — cite if relevant):
---
[chunk 1 text]
---
[chunk 2 text]
---
```

3. Add a citation instruction to the system prompt: "When a provided reference document is directly relevant, quote or paraphrase it and note it was drawn from the organization's reference library."

### 4c. Interface changes required

`IControlNarrativeService.GenerateNarrativeWithAiAsync` must accept a `string tenantId` parameter (or a `NarrativeGenerationContext` value object) so the retrieval step can scope the seed query to the caller's tenant. This is a **breaking change** to the interface — all callers must be updated.

---

## 5. Prerequisites Before Feature 014 Can Ship

| Prerequisite | Status | Owner |
|---|---|---|
| `NarrativeSeedDocument.OriginalFileName` column persisted on upload | ❌ Not done | Backend |
| Vector store / pgvector infrastructure provisioned | ❌ Not done | Infra / Cyborg |
| Embedding model wired in DI (`IEmbeddingGenerator`) | ❌ Not done | Backend |
| `NarrativeSeedChunk` table (or vector collection) migration | ❌ Not done | Backend |
| `INarrativeSeedRetriever` interface + implementation | ❌ Not done | Backend |
| `GenerateNarrativeWithAiAsync` signature updated + callers migrated | ❌ Not done | Backend |
| Eval harness: does seed injection improve narrative quality? | ❌ Not done | Mr. Terrific |

---

## 6. Acceptance Gate for US3 (Spec 068)

Per `specs/068-org-template-admin/spec.md` US3 and tasks T017-T018:

> US3 acceptance criteria cannot close and "Indexed" badge cannot be claimed as meaningful until task #314 confirms the AI pipeline injection is implemented.

Current recommendation: **keep the "Indexed" badge in the UI** (it is accurate — the record was processed without error) but ensure no marketing copy claims "seeds improve AI narratives" until feature 014 ships. Add a UI tooltip or inline note: "Indexed — document stored. AI-assisted narrative improvement requires feature 014 (citation-aware suggestions)."

---

## 7. File Locations

| File | Purpose |
|---|---|
| `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/NarrativeSeeds/Handlers/NarrativeSeedIndexJobHandler.cs` | Indexing job handler (stub) |
| `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/NarrativeSeeds/NarrativeSeedDocumentService.cs` | CRUD + blob storage service |
| `src/Ato.Copilot.Core/Interfaces/Onboarding/INarrativeSeedDocumentService.cs` | Service interface |
| `src/Ato.Copilot.Core/Models/Onboarding/NarrativeSeedDocument.cs` | EF entity |
| `src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs` | AI narrative generation (injection point) |
| `src/Ato.Copilot.Core/Prompts/NarrativeGeneration.prompt.txt` | System prompt (seed context section to be added) |
| `src/Ato.Copilot.Mcp/Endpoints/Onboarding/NarrativeSeedEndpoints.cs` | REST endpoints for seed management |

---

*Authored by Mr. Terrific (task #314 investigation). For feature 014 scope, see `specs/014-agent-ui-enrichment/spec.md`.*
