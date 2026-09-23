# Quickstart: Running the Persona End-to-End Test Suite

**Feature**: 020 | **Date**: 2026-03-05

---

## Prerequisites

| Requirement | Details |
|-------------|---------|
| **MCP Server** | Security Posture Intelligence Navigator MCP server running locally or in a deployed environment with all 118 tools registered |
| **VS Code** | VS Code with the `@ato` chat participant extension installed and connected to the MCP server |
| **Microsoft Teams** | Teams client with Security Posture Intelligence Navigator bot installed (for ISSM, SCA, AO persona tests) |
| **Azure Government** | Active Azure Government subscription (`sub-12345-abcde`) with test resources provisioned |
| **PIM Roles** | Tester must have PIM eligibility for all 5 compliance roles |
| **Test Data** | Clean environment or ability to create a new system named "Eagle Eye" |
| **Time Estimate** | ~4–6 hours for the full suite (147 test cases + 3 scenarios) |

---

## Quick Setup

### 1. Start the MCP Server

```bash
cd /Users/johnspinella/repos/ato-copilot
dotnet build Ato.Copilot.sln
dotnet run --project src/Ato.Copilot.Mcp
```

Verify the server is ready (should respond within 10 seconds per Constitution VIII):
```bash
curl http://localhost:5000/health
```

### 2. Activate the First Persona Role

Before starting tests, activate the ISSM role via PIM:

```text
@ato Activate my Compliance.SecurityLead role for 8 hours — persona test suite execution
```

### 3. Begin Testing

Open the spec at `specs/020-persona-test-cases/spec.md` and work through each persona section sequentially.

---

## Execution Flow

```text
┌─────────────────────────────────────────────────────────┐
│  1. ISSM (43 tests)                                    │
│     Activate: Compliance.SecurityLead                   │
│     Interface: Microsoft Teams                          │
│     Phases: Prepare → Categorize → Select → Implement   │
│             → Assess prep → Authorize → Monitor         │
├─────────────────────────────────────────────────────────┤
│  2. ISSO (24 tests)                                    │
│     Activate: Compliance.Analyst                        │
│     Interface: VS Code @ato                             │
│     Phases: Implement (SSP) → Monitor                   │
├─────────────────────────────────────────────────────────┤
│  3. SCA (24 tests)                                     │
│     Activate: Compliance.Auditor                        │
│     Interface: Microsoft Teams                          │
│     Phase: Assess                                       │
├─────────────────────────────────────────────────────────┤
│  4. AO (14 tests)                                      │
│     Activate: Compliance.AuthorizingOfficial             │
│     Interface: Microsoft Teams (Adaptive Cards)         │
│     Phase: Authorize                                    │
├─────────────────────────────────────────────────────────┤
│  5. Engineer (26 tests)                                │
│     Activate: Compliance.PlatformEngineer (default)     │
│     Interface: VS Code @ato                             │
│     Phases: Implement → Monitor (remediation)           │
├─────────────────────────────────────────────────────────┤
│  6. Error Handling (8 tests)                           │
│     Various personas, test error responses               │
├─────────────────────────────────────────────────────────┤
│  7. Auth/PIM (8 tests)                                 │
│     Any persona, test PIM/CAC/JIT flows                 │
├─────────────────────────────────────────────────────────┤
│  8. Cross-Persona Scenarios (3 scenarios, 40 steps)    │
│     Full lifecycle, Prisma flow, ConMon drift            │
└─────────────────────────────────────────────────────────┘
```

---

## How to Record Results

For each test case, record:

| Field | How to Record |
|-------|---------------|
| **TC-ID** | Copy from spec |
| **Pass / Fail** | ✅ if output matches expected; ❌ if not |
| **Actual Output** | Screenshot or paste of the AI response |
| **Notes** | Any discrepancies, timing, or observations |
| **Duration** | Approximate response time (should be ≤10s) |
| **Blocked** | If a precondition was not met, mark as BLOCKED with the blocking TC-ID |

### Result Template (Markdown)

```markdown
| TC-ID | Status | Duration | Notes |
|-------|--------|----------|-------|
| ISSM-01 | ✅ PASS | 3s | system_id = abc-123 |
| ISSM-02 | ✅ PASS | 4s | 3 resources added |
| ISSM-03 | ❌ FAIL | 2s | Resource not found in boundary — may need to specify exact resource name |
| ISSO-01 | ⬜ BLOCKED | — | Blocked by ISSM-13 failure (no inheritance set) |
```

---

## Role Switching Between Personas

When transitioning between personas:

1. **Deactivate current role**: `@ato Deactivate my {current} role`
2. **Activate new role**: `@ato Activate my {new role} for 4 hours — persona test suite`
3. **Verify role**: `@ato Show my active PIM roles`
4. **Switch interface** if needed (Teams ↔ VS Code)

### PIM Role Mapping

| Persona | PIM Role to Activate |
|---------|---------------------|
| ISSM | `Compliance.SecurityLead` |
| ISSO | `Compliance.Analyst` |
| SCA | `Compliance.Auditor` |
| AO | `Compliance.AuthorizingOfficial` |
| Engineer | `Compliance.PlatformEngineer` (default — may not need activation) |

---

## Troubleshooting

| Issue | Resolution |
|-------|-----------|
| Tool not found | Verify MCP server is running with all 118 tools: check `/tools/list` endpoint |
| 403 Forbidden on positive test | Wrong PIM role active — deactivate and activate the correct role |
| Test case blocked | Record as BLOCKED, note the blocking TC-ID, continue with next independent test if possible |
| Timeout (>10s) | Note the duration; this may indicate a performance regression per Constitution VIII |
| AI resolves wrong tool | Record the tool that was actually invoked; this indicates an NL→tool resolution issue |
| "Eagle Eye" already exists | If re-running, either delete the system first or use a different name (update all NL inputs accordingly) |

---

## Acceptance Criteria Checklist

After completing all tests, verify:

- [ ] Every positive test case produces expected output within 10 seconds
- [ ] Every RBAC-denied test case returns 403 Forbidden (not 404 or 500)
- [ ] Cross-persona handoffs complete with correct data flowing between personas
- [ ] PIM role activation gates tool access correctly
- [ ] All NL inputs resolve to the correct tool without manual tool specification
- [ ] Prisma import tests include Prisma-specific fields (PrismaAlertId, CloudResourceType, RemediationCli)
- [ ] Idempotent operations produce consistent results on re-run
