# System Security Capabilities — expanded UI mocks

Proposed designs, generated with the built-in image-generation tool. No production UI or permission changes are included.

- [01–02: System capability list and implementation detail](01-overview-and-implementation.png)
- [03–04: Component view, component drawer and library selection](02-components-and-library.png)
- [05–06: Coverage/duties, evidence/narratives and removal confirmation](03-coverage-evidence-and-removal.png)
- [07–08: System applicability, final review, success and partial failure](04-applicability-and-confirmation.png)

## Design intent

One system sidebar destination, Security Capabilities, contains By capability and By component views. The top navigation retains the reusable organization library. Selected system context stays visible throughout the flow.

Provider source records remain read-only. System applicability, boundary placement, local supporting capabilities, responsibility confirmation and narrative acceptance are distinct actions. Removing a system link/subscription preserves shared library records and approved historical content.

## Implementation interpretation

These images use illustrative records, control IDs, dates, source versions and counts. They are not verified production mappings. The application's persisted mappings and permission responses must supply actual values. The component view includes all locally applicable contributors, including ones linked through supporting organization capabilities; it does not rewrite provider capability authorship.

The AU-2 confirmation button must remain disabled until required review checks, notes and current source revision are valid and the actor is authorized, even though the static drawing uses the primary button color. Narrative generation/acceptance must follow actual dependency and reviewer policies. A disabled control needs visible explanatory text.

The add flow shows a possible resumable partial-save contract, not a guarantee that the existing APIs implement one. Implementation must support safe retries before shipping that state. System-wide capability placement is not a replacement for per-component boundary relationships.

## Prompt set

All boards use the first system mock as the reference for SPIN branding, palette, shell, selected system and typography.

- Board 02 prompt: expand By component with type/source/capability/boundary columns and a read-only provider component drawer; expand Add from library with source filters, selection and a final-review inset.
- Board 03 prompt: expand Coverage & duties with per-control source comparison and ISSM/ISSO review; expand Evidence & narratives with independent policy/technical freshness and retained approved content; include a scoped removal confirmation.
- Board 04 prompt: expand the remaining add-flow steps, system/boundary applicability and final review; show separate saved versus pending work with success and resumable partial-failure outcomes.
