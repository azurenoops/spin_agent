# Feature Specification: Enhanced Technical Evidence Automation

**Spec Number**: 077  
**Feature Branch**: `spec/418-enhanced-evidence-automation`  
**GitHub Issue**: [#418](https://github.com/azurenoops/ato-copilot/issues/418)  
**Created**: 2026-06-22  
**Status**: Approved  
**Wave**: W8  
**Priority**: P1  
**Owner**: Cyborg

---

## Context

SPIN Agent currently ships 10 specialized Azure SDK evidence collectors covering:
`AC, AU, CA, CM, CP, IA, IR, RA, SC, SI`

The remaining 10 NIST 800-53 Rev 5 families (`AT, MA, MP, PE, PL, PM, PS, PT, SA, SR`)
fall through to the `DefaultEvidenceCollector`, producing generic evidence with no
family-specific Azure SDK calls. This leaves us positioned as a "narrative generator"
rather than an evidence-collection platform—unacceptable against Tanium/Wiz competition.

This spec closes the coverage gap (2x = all 20 specialized collectors), adds a
**correlation engine** to link multiple evidence sources to a single control,
**freshness tracking** with staleness alerts, an **audit trail** for evidence lineage,
and the backend API contracts that power future evidence visualization UI.

---

## Requirements (from Issue #418)

| ID | Requirement |
|----|-------------|
| R1 | Azure SDK evidence collectors expanded to 2x current resource type coverage (all 20 specialized) |
| R2 | Evidence correlation engine — multiple evidence sources link to a single control |
| R3 | Evidence freshness tracking and staleness alerts |
| R4 | Evidence chain visualization data (API backend only; UI is future wave) |
| R5 | Evidence audit trail — queryable by control, subscription, actor |

---

## Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | All 20 NIST 800-53 families have specialized collectors (zero fall-through to Default for known families) |
| AC-2 | EvidenceCorrelationEngine links evidence sources to controls and persists ControlEvidenceMapping |
| AC-3 | EvidenceFreshnessService marks evidence stale after TTL (24h automated, 90d manual) |
| AC-4 | GetStaleEvidenceAsync() returns all artifacts beyond TTL |
| AC-5 | Every automated collection writes an EvidenceAuditEvent record |
| AC-6 | GET /api/v1/evidence/audit-trail returns audit events filterable by controlId/subscriptionId |
| AC-7 | GET /api/v1/evidence/stale returns stale evidence items |
| AC-8 | POST /api/v1/evidence/correlate correctly maps evidence to control |

---

## New Evidence Collectors (10)

All extend BaseEvidenceCollector. 5 evidence types per family.

| Collector | FamilyCode | Key Azure Resources |
|-----------|------------|---------------------|
| AwarenessTrainingEvidenceCollector | AT | VMs, role assignments, policy state |
| MaintenanceEvidenceCollector | MA | maintenanceConfigurations, update schedules, locks |
| MediaProtectionEvidenceCollector | MP | storageAccounts, encryption, policy |
| PhysicalEnvironmentalEvidenceCollector | PE | geo-redundancy, location distribution |
| PlanningEvidenceCollector | PL | tags/governance policies, resource org |
| ProgramManagementEvidenceCollector | PM | subscriptions inventory, management groups |
| PersonnelSecurityEvidenceCollector | PS | role assignments, conditional access |
| PiiProcessingEvidenceCollector | PT | Key Vaults, data classification policies |
| SystemServicesAcquisitionEvidenceCollector | SA | container registries, approved images |
| SupplyChainRiskEvidenceCollector | SR | container registries, Defender supply chain |

---

## New Data Models

### ControlEvidenceMapping
- Id (Guid), ControlId, SubscriptionId, EvidenceSourceType, EvidenceReferenceId
- MappingNote, CorrelationScore (0.0-1.0), MappedAt, MappedBy, TenantId

### EvidenceFreshnessRecord
- Id (Guid), ControlId, SubscriptionId, LastCollectedAt
- FreshnessWindowHours (24 automated / 2160 manual), StaleAfter, EvidenceSourceType, TenantId

### EvidenceAuditEvent
- Id (Guid), EventType (Collected/Mapped/Archived/StaleAlertFired), ControlId
- SubscriptionId, ActorId, Description, Metadata (JSON), OccurredAt, TenantId

---

## New API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| POST | /api/v1/evidence/correlate | Map evidence to control |
| GET | /api/v1/evidence/{subscriptionId}/mappings/{controlId} | Evidence mapped to control |
| GET | /api/v1/evidence/{subscriptionId}/stale | Stale evidence for subscription |
| GET | /api/v1/evidence/{subscriptionId}/freshness/{controlId} | Freshness record |
| GET | /api/v1/evidence/{subscriptionId}/audit-trail | Audit events |

---

## OSCAL Backend Contract

See oscal-api-contract.md (companion document for Issue #419 / Mr. Terrific)

---

## Definition of Done

- 10 new evidence collectors implemented
- EvidenceCorrelationEngine implemented and tested
- EvidenceFreshnessService implemented and tested  
- EvidenceAuditService implemented and tested
- EF migration for 3 new tables
- 5 new API endpoints
- All collectors registered in ServiceCollectionExtensions
- OSCAL contract published
- CI green, PR merged
