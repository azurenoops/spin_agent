# Frontend TypeScript Types: RMF Workflow Completeness (Epic #121)

**File**: `src/Ato.Copilot.Dashboard/src/types/authorization.ts`

---

## Enums

```typescript
export type DecisionType = 'ATO' | 'AtoWithConditions' | 'IATT' | 'DATO';

export type RiskLevel = 'Low' | 'Moderate' | 'High' | 'Critical';

export type AuthorizationStatus = DecisionType | 'None';
```

---

## Domain types

```typescript
export interface RiskAcceptance {
  id: string;
  controlId: string;
  justification: string;
}

export interface AuthorizationDecision {
  id: string;
  systemId: string;
  decisionType: DecisionType;
  expirationDate: string;       // ISO 8601
  issuedAt: string;             // ISO 8601
  issuedBy: string;             // AO email / UPN
  residualRiskLevel: RiskLevel;
  termsAndConditions: string | null;
  residualRiskJustification: string;
  riskAcceptances: RiskAcceptance[];
}
```

---

## Request types

```typescript
export interface RiskAcceptanceRequest {
  controlId: string;
  justification: string;
}

export interface AuthorizeSystemRequest {
  decisionType: DecisionType;
  expirationDate: string;               // ISO 8601
  termsAndConditions?: string | null;
  residualRiskLevel: RiskLevel;
  residualRiskJustification: string;
  riskAcceptances: RiskAcceptanceRequest[];
}
```

---

## Pending decisions types

```typescript
export interface PendingDecision {
  systemId: string;
  systemName: string;
  authorizationStatus: AuthorizationStatus;
  expirationDate: string | null;        // ISO 8601
  daysUntilExpiration: number;          // negative if overdue
  lastDecisionType: DecisionType | null;
}

export interface PendingDecisionsPage {
  items: PendingDecision[];
  page: number;
  pageSize: number;
  total: number;
}
```

---

## Hook return types

```typescript
// useAuthorizeSystem
export interface UseAuthorizeSystemResult {
  mutate: (payload: AuthorizeSystemRequest) => Promise<AuthorizationDecision>;
  isLoading: boolean;
  error: ApiError | null;
  data: AuthorizationDecision | null;
  reset: () => void;
}

// usePendingDecisions
export interface UsePendingDecisionsResult {
  data: PendingDecisionsPage | null;
  isLoading: boolean;
  error: ApiError | null;
  refetch: () => void;
}

// Shared error shape
export interface ApiError {
  code: string;
  message: string;
  fields?: Record<string, string>;
}
```

---

## Component prop types

```typescript
// AuthorizePage — no external props; reads systemId from useParams()

// AoPendingDecisionsWidget
export interface AoPendingDecisionsWidgetProps {
  /** Optional override for initial page size. Defaults to 20. */
  pageSize?: number;
}

// AuthorizePageStatusPanel (sub-component)
export interface AuthorizePageStatusPanelProps {
  decision: AuthorizationDecision | null;
  openPoamCount: number;
}

// AuthorizePageForm (sub-component)
export interface AuthorizePageFormProps {
  systemId: string;
  onSuccess: (decision: AuthorizationDecision) => void;
}
```
