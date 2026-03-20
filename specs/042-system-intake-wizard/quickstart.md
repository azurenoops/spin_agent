# Quickstart: System Intake Wizard

**Feature**: 042-system-intake-wizard  
**Branch**: `042-system-intake-wizard`

## Prerequisites

- Node.js 18+ and npm
- .NET 8 SDK
- Docker (for database)
- Existing dashboard dev environment (`src/Ato.Copilot.Dashboard`)

## Getting Started

### 1. Start the Backend

```bash
cd /Users/johnspinella/repos/ato-copilot
dotnet build
dotnet run --project src/Ato.Copilot.Chat
```

### 2. Start the Dashboard

```bash
cd src/Ato.Copilot.Dashboard
npm install
npm run dev
```

### 3. Verify the Wizard

1. Open `http://localhost:5173/systems` in a browser
2. Click the **"+ Add System"** button
3. The intake wizard modal should open at Step 1
4. Fill in system registration fields and click **Next**
5. Navigate through all 7 steps to verify the flow

## Build & Test

### Frontend (Dashboard)

```bash
cd src/Ato.Copilot.Dashboard

# Type check
npx tsc --noEmit

# Run unit tests
npm test

# Run tests with coverage
npm run test:coverage

# Run Playwright E2E tests
npx playwright test
```

### Backend (.NET)

```bash
# Build
dotnet build

# Unit tests
dotnet test tests/Ato.Copilot.Tests.Unit

# Integration tests
dotnet test tests/Ato.Copilot.Tests.Integration
```

## Key Files

### New Files (Frontend)
| File | Purpose |
|------|---------|
| `src/components/wizard/IntakeWizard.tsx` | Main wizard modal container with stepper |
| `src/components/wizard/WizardStepper.tsx` | Progress indicator component |
| `src/components/wizard/steps/SystemRegistration.tsx` | Step 1: System name, type, etc. |
| `src/components/wizard/steps/SecurityCapabilities.tsx` | Step 2: Search & link capabilities |
| `src/components/wizard/steps/SystemComponents.tsx` | Step 3: Add Person/Place/Thing |
| `src/components/wizard/steps/AuthorizationBoundaries.tsx` | Step 4: Create boundaries |
| `src/components/wizard/steps/AssignRoles.tsx` | Step 5: Assign RMF roles |
| `src/components/wizard/steps/VerifyRoles.tsx` | Step 6: Read-only role summary |
| `src/components/wizard/steps/SetCategorization.tsx` | Step 7: FIPS 199 categorization |
| `src/components/wizard/steps/CompletionSummary.tsx` | Final success screen |
| `src/hooks/useIntakeWizard.ts` | Wizard state management (useReducer) |
| `src/api/capabilityLinks.ts` | API client for capability-link endpoints |
| `src/data/sp800-60-information-types.json` | Bundled SP 800-60 reference data |

### Modified Files (Frontend)
| File | Change |
|------|--------|
| `src/pages/PortfolioDashboard.tsx` | Replace Add System dialog with wizard trigger |
| `src/types/dashboard.ts` | Add setup completion fields to PortfolioSystemSummary |

### New Files (Backend)
| File | Purpose |
|------|---------|
| `Core/Models/Compliance/SystemCapabilityLink.cs` | New join entity |
| `Core/Services/SystemCapabilityLinkService.cs` | CRUD for capability links |

### Modified Files (Backend)
| File | Change |
|------|--------|
| `Core/Data/Context/AtoCopilotContext.cs` | Add DbSet for SystemCapabilityLink |
| `Core/Services/DashboardService.cs` | Add setup completion to portfolio DTO |
| `Chat/Controllers/DashboardApiController.cs` | Add capability-link endpoints |

### Documentation
| File | Change |
|------|--------|
| `docs/guides/system-intake-wizard.md` | New: full wizard guide |
| `docs/getting-started/issm.md` | Update: reference wizard |
| `docs/getting-started/isso.md` | Update: reference wizard |
| `docs/getting-started/engineer.md` | Update: reference wizard |

## Architecture Notes

- **Wizard is a modal overlay** on the `/systems` route — no new routes needed
- **State management**: `useReducer` in `useIntakeWizard.ts` manages all 7 steps
- **Data persistence**: Each step saves on "Next" via existing API endpoints
- **SP 800-60 data**: Bundled as static JSON, loaded client-side for instant search
- **"Setup Incomplete" badge**: Computed from existing data relationships (boundaries, roles, categorization)
