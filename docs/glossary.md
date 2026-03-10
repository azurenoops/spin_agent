# Glossary

Consistent terminology prevents misunderstandings. This matters doubly for agents: if a guidance file says "handoff" and an agent interprets it as "deployment," work breaks down. When you encounter an unfamiliar term, check here first.

## Teamwork Framework Terms

### Role
A defined set of responsibilities, permissions, and constraints assigned to an agent or human. Roles scope what a contributor should and shouldn't do. Example roles: planner, implementer, reviewer.

### Workflow
A sequence of steps that moves work from start to completion, typically passing through multiple roles. Workflows define the order of operations and the conditions for progressing between steps.

### Handoff
The structured transfer of work from one role to another. A handoff includes context about what was done, what remains, and any decisions or blockers encountered. Handoffs are the primary coordination mechanism between agents.

### Role File
A document that defines how a role should behave — its identity, responsibilities, inputs, outputs, rules, quality bar, and escalation criteria. Agents read their role file at session start to understand their scope. Stored in `agents/roles/`.

### Quality Bar
The minimum standard that work must meet before it can be handed off or considered complete. Quality bars are defined per role and may include criteria like test coverage, documentation, code review, or passing CI.

### Escalation
The process of flagging a decision, blocker, or risk that exceeds the current role's authority or expertise. Escalations route issues to the appropriate role or to a human for resolution. Agents should escalate rather than guess.

### Orchestrator
The agent role responsible for coordinating workflow execution. The orchestrator initializes workflows, dispatches roles, validates handoffs between steps, enforces quality gates, and tracks progress through `.teamwork/` state files. It never implements, designs, reviews, or tests — it only coordinates.

### Protocol
A file-based convention for coordination between agents. Protocols define the format and location of state files, handoff artifacts, memory entries, and metrics logs in the `.teamwork/` directory. Protocols are tool-agnostic — any AI tool can read and write them.

## Template for Project-Specific Terms

Add terms below as your project develops. Use this format:

```markdown
### Term Name
Brief definition in 1-2 sentences. Include how the term is used in this
project specifically, not just its general meaning.
```

### Example Project Terms

### Workspace
The root directory of the project and its immediate configuration. Agents operate within the workspace and should not modify files outside it without explicit instruction.

### Session
A single continuous interaction between an agent and the project. Sessions are stateless by default — agents should not assume knowledge from previous sessions unless provided through guidance files or context.

### Contributor
Any entity — human or agent — that performs work on the project. Using "contributor" avoids ambiguity when instructions apply to both humans and agents equally.

---

### Context
Information provided to an agent at the start of a session to orient it. Context may include relevant files, recent changes, open issues, or specific instructions. More context generally leads to better agent output, but too much noise degrades focus.

### Scope
The boundaries of what a task, role, or session should and should not touch. Staying in scope prevents unintended side effects. If a task requires work outside your scope, escalate or hand off rather than expanding silently.

---

## Maintaining This Glossary

- Add terms when you notice inconsistent usage across docs or conversations
- Keep definitions concrete: "A service that handles things" is not useful. "The authentication service that validates JWT tokens and manages user sessions" is
- Remove terms only when they are no longer used anywhere in the project
- Review this file when onboarding new agents or contributors
