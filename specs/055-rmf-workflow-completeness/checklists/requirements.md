# Requirements Checklist: RMF Workflow Completeness (Epic #121)

## SDK Completeness

### Spec artifacts
- [x] `spec.md` — background, verified code state, user stories with acceptance
      criteria and edge cases
- [x] `tasks.md` — phased tasks with exact file paths and [P] markers
- [x] `data-model.md` — DTO definitions, query contract, no new entities
- [x] `research.md` — trade-off analysis (REST vs chat, page vs modal,
      pull vs push, nav visibility)
- [x] `plan.md` — phased implementation plan with dependencies and risks
- [x] `quickstart.md` — dev setup, seeder, test commands
- [x] `checklists/requirements.md` (this file)
- [x] `contracts/http-api.md` — full request/response shapes for both endpoints
- [x] `contracts/frontend-types.md` — TypeScript types for all new components

### User stories
- [x] US1 (P1): AuthorizePage — acceptance criteria + edge cases documented
- [x] US2 (P1): AO pending decisions endpoint + widget — acceptance criteria
      + edge cases documented
- [x] US3 (P2): REST endpoint — acceptance criteria + edge cases documented
- [x] US4 (P2): Package generation link — acceptance criteria documented

### Backend
- [x] New endpoint paths specified (`POST /api/v1/systems/{id}/authorize`,
      `GET /api/v1/ao/pending-decisions`)
- [x] RBAC guards specified (`Compliance.AuthorizingOfficial` on both)
- [x] DTO shapes defined (`AuthorizeSystemRequest`, `AuthorizationDecisionDto`,
      `PendingDecisionDto`, `RiskAcceptanceRequest`, `RiskAcceptanceDto`)
- [x] Error response codes documented (400, 403, 404, 409)
- [x] Pagination contract defined (page/pageSize/total)
- [x] Unit test files named with path
- [x] No new DB migrations needed — confirmed

### Frontend
- [x] New route specified (`/systems/:id/authorize`)
- [x] New component file paths named (`AuthorizePage.tsx`,
      `AoPendingDecisionsWidget.tsx`)
- [x] New hooks named (`useAuthorizeSystem`, `usePendingDecisions`)
- [x] TypeScript types defined
- [x] Role guard logic described
- [x] Unit test files named with path

### Quality gates
- [ ] PR passes existing CI (build + test + lint)
- [ ] New endpoint covered by at least 3 unit test cases
- [ ] New frontend components covered by RTL unit tests
- [ ] OpenAPI annotations added to new endpoints
- [ ] Accessibility: form labels associated with inputs (WCAG 2.1 AA)
- [ ] CHANGELOG updated

### Out of scope
- Real-time WebSocket/SSE for pending decisions widget
- Global audit log UI for authorization history
- Email notifications to AO on expiration (separate feature)
- Multi-AO approval workflow (single AO decision is the model)
