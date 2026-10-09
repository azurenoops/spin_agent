# MO / Org portal — interactive IA mock

Proposed overlay on the Feature 079 Systems workspace. **Not** a required UI
target until Feature 079 (or a follow-on) absorbs it. Does not replace
[system-overview-mock](../system-overview-mock/pages.html).

Product SoT: [mo-org-portal-product-brief.md](../mo-org-portal-product-brief.md).

Open [index.html](index.html). Synthetic records only. No Azure, Entra, eMASS,
or production APIs. Simulation state lives in memory and resets on reload.

Navigation matches live Systems code, not a custom sitemap:

- **Top:** organization links (`OrganizationNavigation`) — Systems list.
  Create / import are actions, not nav items.
- **Left:** eight `SYSTEM_SCREEN_GROUPS` labels (`SystemSidebar`). Clicking a
  group opens its first page.
- **Tabs:** pages inside the current group (`SystemTaskNavigation`). Overlay
  extras stay where live puts them: Provider hosting under System definition;
  Control inheritance under Controls & evidence.
- There is no left-nav item named Residual or Package.

Walk (same chrome as live Systems):

1. **Systems** list — no left nav. Add system / Import from eMASS are actions.
2. Open **Mission Planning** — eyebrow is the system name; title is
   “A clear path to your ATO package.” Tabs: Readiness | Monitoring & follow-up.
   Drift banner if adopted r2 / CSP r3. Next-actions, not CSP three-fact tiles.
3. **System definition** — Mission → Users → Environment & hosting (extra tab
   **Provider hosting**) → Data → Inventory & boundary → Ports. Draft ≠ ATO.
   Association ≠ inheritance. Data types ≠ approved categorization.
4. **Controls & evidence** → Responsibilities, Narratives, Evidence. Extra tab
   **Control inheritance summary** (adopt/stay). Not a left-nav item named Residual.
5. **ATO Readiness** → four package facts. Export is not submission.
6. **Continuous monitoring** → this system only.

Design bar: Empty / Unavailable / Restricted. Restricted is read-only
(SCA preview): no adopt, no apply source, no record decision.
