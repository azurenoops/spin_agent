# Research: Dashboard User Documentation

**Date**: 2026-03-15 | **Feature**: 032-dashboard-documentation

## R1: Custom Popover/Tooltip Implementation (No UI Library)

**Decision**: Build custom `HelpTooltip` component using Tailwind CSS and native React

**Rationale**: The dashboard has no UI component library (no HeadlessUI, Radix, or react-tooltip). The existing `TodoActionDialog` already demonstrates a custom overlay pattern with:
- Fixed backdrop with `bg-black/40 backdrop-blur-sm`
- Click-outside closing via `e.target === e.currentTarget`
- Escape key closing via `useEffect` keyboard listener
- ARIA attributes (`role="dialog"`, `aria-modal="true"`)

For tooltips, a simpler pattern is appropriate:
- Positioned absolutely relative to the trigger button
- Shown/hidden via state toggle (click, not hover — better for accessibility and mobile)
- Escape key to close
- Click-outside to close
- No backdrop overlay (unlike dialog — tooltip should not block interaction)

**Alternatives Considered**:
- **@headlessui/react Popover**: Would add ~25KB dependency for one component. Rejected — unnecessary complexity.
- **@radix-ui/react-popover**: Adds dependency + requires portal rendering. Rejected — custom solution is simpler.
- **react-tooltip**: Hover-only, not suitable for rich content. Rejected.

## R2: Slide-Out Help Panel Pattern

**Decision**: Build `HelpPanel` as a right-side slide-out panel, replicating the TodoPanel/sidebar pattern

**Rationale**: The existing `PageLayout` has a side panel mechanism:
- Side panel uses `sidePanelOpen` state with `useState(true)`
- Rendered in a `w-80` container
- Toggle button with arrow icon rotates on state change
- Only visible at `xl` breakpoint (`hidden xl:flex`)

The HelpPanel should:
- Replace the TodoPanel/sidePanel content when the help icon is clicked
- Use the same `w-80` container width
- Have its own close button
- Display scrollable documentation content
- Be mutually exclusive with TodoPanel (one visible at a time)

**Implementation approach**: Add `helpPanelOpen` state to PageLayout. When help icon clicked, show HelpPanel in the sidePanel slot. When a system page has a TodoPanel, toggling help replaces it (and vice versa).

**Alternatives Considered**:
- **Separate overlay panel**: Would require z-index management and could conflict with TodoActionDialog. Rejected.
- **Full-page route**: Would lose user context. Rejected per clarification answer.

## R3: Section Title Patterns for Help Icon Insertion

**Decision**: Help icons will be inserted via two approaches depending on component type

**Rationale**: Research reveals two distinct patterns:

### Pattern A: Titles rendered in SystemDetail.tsx (parent)
These sections render their title as an `<h2>` in `SystemDetail.tsx`, then pass data to a child component:
- **RMF Phase Progress**: `<h2 className="mb-3 text-sm font-semibold text-gray-700">RMF Phase Progress</h2>`
- **Control Family Heatmap**: `<h2>` in SystemDetail
- **Compliance Trends**: `<h2>` in SystemDetail
- **Recent Activity**: `<h2>` in SystemDetail

→ Insert help icon directly in the `<h2>` tag in SystemDetail.tsx

### Pattern B: Titles rendered inside the component
These components render their own title inside their JSX:
- **MetricCard** (Compliance Score, POA&Ms, Narrative Coverage): `<p className="text-sm font-medium text-gray-500">{title}</p>`
- **FindingsSeverityCard**: `<p className="text-sm font-medium text-gray-500">Findings</p>`
- **ATO Status**: Custom `<div>` in SystemDetail (not a MetricCard)
- **TodoPanel**: `<h2 className="text-lg font-semibold text-gray-900">To do</h2>`

→ Add optional `helpKey` prop to MetricCard; add help icon directly inside component titles

## R4: Help Content Structure

**Decision**: Store help content as a static TypeScript record keyed by section identifier

**Rationale**: Help content is static text that does not change at runtime. No API endpoint is needed. A TypeScript file with a `Record<string, HelpContent>` provides:
- Type safety
- Easy maintenance
- No network requests
- Bundle-time inclusion

Structure per help entry:
```typescript
interface HelpContent {
  title: string;
  description: string;      // 2–3 sentences per clarification answer
  emptyStateHint?: string;   // What to do when section has no data (per clarification)
}
```

9 help entries needed:
1. `todo` — To Do panel
2. `rmfProgress` — RMF Phase Progress
3. `complianceScore` — Compliance Score metric
4. `atoStatus` — ATO Status metric
5. `poams` — POA&Ms metric
6. `narrativeCoverage` — Narrative Coverage metric
7. `findings` — Findings severity
8. `complianceTrends` — Compliance Trends chart
9. `recentActivity` — Recent Activity feed

## R5: Existing Documentation Gap Analysis

**Decision**: Update existing `docs/guides/compliance-dashboard.md` rather than create new file

**Rationale**: The file already exists with partial coverage (Portfolio + System Detail basics). Missing sections:
- Component Inventory page
- Gap Analysis page
- Implementation Roadmap page
- Capability Library page
- ToDo panel & action dialog guide
- Contextual help tooltips documentation

The MkDocs nav in `mkdocs.yml` needs to add the dashboard guide entry under the Guides section — it currently exists as a file but is not listed in navigation.

**Alternatives Considered**:
- **Create new file**: Would create duplication with existing content. Rejected.
- **Multiple files**: Per clarification, user chose single comprehensive page. Rejected.

## R6: Help Panel Content Organization

**Decision**: Organize help panel content into collapsible sections mirroring dashboard pages

**Rationale**: The help panel will display a scrollable single-page guide with collapsible sections:
1. Getting Started (layout, navigation, terminology)
2. Portfolio Dashboard
3. System Detail (with sub-sections for each card/chart)
4. Capability Library
5. Component Inventory
6. Gap Analysis
7. Implementation Roadmap
8. To Do Panel
9. Reference (color codes, severity levels, glossary)

This matches the spec's FR-013 (single comprehensive page) and the user's clarified preference.
