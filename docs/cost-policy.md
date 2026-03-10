# Cost Policy

Every agent invocation consumes AI API tokens, which cost money. Be deliberate about
when and how agents are used.

## Guidelines

1. **Keep prompts focused.** Don't send an entire repository as context when a few
   relevant files will do. Scope the input to what the agent actually needs.

2. **Run cheap checks first.** Before invoking an expensive AI agent, run linters, type
   checkers, and tests locally. Catching a syntax error with `eslint` is cheaper than
   having a Reviewer agent find it.

3. **Break large tasks into smaller pieces.** One massive prompt with an entire feature
   spec is more expensive and less reliable than several focused prompts for individual
   components.

4. **Track your usage.** Set personal budget limits and monitor spend through your AI
   provider's dashboard. Know what you're spending before it surprises you.

5. **Match the model to the task.** Use cheaper, faster models for routine work like
   linting, formatting checks, or simple code generation. Reserve premium models for
   complex tasks like architecture decisions, security auditing, or nuanced code review.

## Cost by Workflow

| Workflow | Relative Cost | Why |
|----------|--------------|-----|
| Documentation | Low | Small context, straightforward generation |
| Bug Fix | Medium | Focused scope, but multiple agent steps |
| Feature | High | Large context, many agents involved, iteration loops |
| Refactor | Medium–High | Needs broad codebase understanding |
| Security Response | Medium | Focused but requires careful analysis |
| Spike | Low–Medium | Research-oriented, less code generation |
| Hotfix | Low–Medium | Abbreviated workflow, minimal scope |
| Rollback | Low | Mostly mechanical — revert and validate |

## Tips

- If a Coder agent produces code that fails lint or tests, fix the obvious issues
  locally before re-invoking the agent.
- Use the Planner to scope work tightly — a well-defined task is cheaper to execute
  than a vague one.
- Review agent output yourself before passing it to the next workflow step. Catching
  issues early avoids expensive rework downstream.
