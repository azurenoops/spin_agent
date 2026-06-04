# Data Model: RMF Workflow Completeness (Epic #121)

## No new database entities required

All EF Core entities needed for this feature already exist:
- `AuthorizationDecision` — stores AO decisions (decision_type, expiration_date,
  terms_and_conditions, residual_risk_level, etc.)
- `RiskAcceptance` — child rows of `AuthorizationDecision`
- `PoamItem` — read-only reference for open POAM count on AuthorizePage

No migrations needed. The feature adds API surface and UI only.

---

## New DTOs

### `AuthorizeSystemRequest` (request body for POST /authorize)

```csharp
public record AuthorizeSystemRequest
{
    [Required]
    public DecisionType DecisionType { get; init; }       // ATO | AtoWithConditions | IATT | DATO

    [Required]
    public DateTimeOffset ExpirationDate { get; init; }   // must be > UtcNow

    public string? TermsAndConditions { get; init; }

    [Required]
    public RiskLevel ResidualRiskLevel { get; init; }     // Low | Moderate | High | Critical

    [Required]
    public string ResidualRiskJustification { get; init; }

    public List<RiskAcceptanceRequest> RiskAcceptances { get; init; } = [];
}

public record RiskAcceptanceRequest
{
    [Required]
    public string ControlId { get; init; }

    [Required]
    public string Justification { get; init; }
}
```

### `AuthorizationDecisionDto` (response from POST /authorize and GET status)

```csharp
public record AuthorizationDecisionDto
{
    public Guid Id { get; init; }
    public Guid SystemId { get; init; }
    public string DecisionType { get; init; }
    public DateTimeOffset ExpirationDate { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public string IssuedBy { get; init; }              // email / UPN of AO
    public string ResidualRiskLevel { get; init; }
    public string? TermsAndConditions { get; init; }
    public string ResidualRiskJustification { get; init; }
    public List<RiskAcceptanceDto> RiskAcceptances { get; init; }
}

public record RiskAcceptanceDto
{
    public Guid Id { get; init; }
    public string ControlId { get; init; }
    public string Justification { get; init; }
}
```

### `PendingDecisionDto` (response items for GET /ao/pending-decisions)

```csharp
public record PendingDecisionDto
{
    public Guid SystemId { get; init; }
    public string SystemName { get; init; }
    public string AuthorizationStatus { get; init; }   // ATO | AtoWithConditions | IATT | DATO | None
    public DateTimeOffset? ExpirationDate { get; init; }
    public int DaysUntilExpiration { get; init; }      // negative if overdue
    public string? LastDecisionType { get; init; }
}
```

### Paginated envelope (standard house style)

```json
{
  "items": [ /* PendingDecisionDto[] */ ],
  "page": 1,
  "pageSize": 20,
  "total": 5
}
```

---

## Enum additions

No new enums. `DecisionType` and `RiskLevel` enums already exist in the domain.
DTOs serialize them as strings for API stability.

---

## Query: Pending Decisions

```sql
SELECT s.Id AS SystemId, s.Name AS SystemName,
       ad.DecisionType AS LastDecisionType,
       ad.ExpirationDate,
       DATEDIFF(day, GETUTCDATE(), ad.ExpirationDate) AS DaysUntilExpiration
FROM Systems s
INNER JOIN AuthorizationDecisions ad
       ON ad.Id = (
           SELECT TOP 1 Id FROM AuthorizationDecisions
           WHERE SystemId = s.Id
           ORDER BY IssuedAt DESC
       )
WHERE ad.ExpirationDate <= DATEADD(day, 30, GETUTCDATE())
  AND s.TenantId = @tenantId
  AND /* AO has role on this system */
ORDER BY ad.ExpirationDate ASC
```

Implemented as an EF Core LINQ query with `OrderBy(x => x.ExpirationDate)`;
the raw SQL is illustrative.
