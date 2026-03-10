# Workflow Selector

A guide to choosing the right workflow for your task. See `agents/workflows/` for full workflow details.

## Decision Tree

Start at the top and follow the first question that matches your situation.

```
Is this urgent or production-critical?
├── Yes → Hotfix
└── No ↓

Is this a security vulnerability?
├── Yes → Security Response
└── No ↓

Is this a bug?
├── Yes → Bugfix
└── No ↓

Is this new functionality?
├── Yes → Feature
└── No ↓

Is this improving existing code without changing behavior?
├── Yes → Refactor
└── No ↓

Is this a dependency update?
├── Yes → Dependency Update
└── No ↓

Is this documentation-only?
├── Yes → Documentation
└── No ↓

Is this research or investigation?
├── Yes → Spike
└── No ↓

Is this preparing a release?
├── Yes → Release
└── No ↓

Not sure → Start with a Planning Request issue
```

## Symptom-to-Workflow Table

| Symptom / Situation | Workflow | Why |
|---|---|---|
| Production is down or degraded | Hotfix | Fastest path to resolution with minimal scope |
| CVE reported or vulnerability found | Security Response | Structured triage, patch, and disclosure |
| Something that worked before is broken | Bugfix | Focused fix with regression test |
| Users need a new capability | Feature | Full plan → implement → test → review cycle |
| Code is correct but messy or slow | Refactor | Behavior-preserving improvements |
| Library has a new version or security patch | Dependency Update | Controlled upgrade with compatibility checks |
| README, guides, or API docs need work | Documentation | Docs-only changes, no code behavior change |
| "How should we approach X?" | Spike | Time-boxed research with written findings |
| Ready to cut a version | Release | Version bump, changelog, tag, publish |
