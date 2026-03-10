---
applyTo: ".teamwork/**"
---
# Teamwork Configuration Guidelines

- State files (`.teamwork/state/`) use YAML format with fields: `workflow-id`, `current-step`, `current-role`, `status`, `created`, `updated`.
- Handoff artifacts (`.teamwork/handoffs/`) use Markdown with sections: Context, Decisions Made, Artifacts, Open Questions, Next Steps.
- Memory files (`.teamwork/memory/`) use JSONL format — one JSON object per line with `domain`, `type` (pattern/antipattern/decision/feedback), and `content` fields.
- Metrics files (`.teamwork/metrics/`) are gitignored — do not commit them.
- Config file (`.teamwork/config.yaml`) defines model tier mappings and project-level settings.
- Always validate YAML syntax before writing state files.
- When updating state files, preserve existing fields and only modify the ones relevant to your step.
