# Onboarding Guide

This guide covers how to get started as a new contributor — whether you're an AI agent or a human. Follow the section that applies to you, then review the shared checklist at the end.

## For Agents

### Step 1: Read the Foundation Files

Read these files in order. Each one builds on the previous:

1. **`MEMORY.md`** — Current project state, recent decisions, and active context
2. **`README.md`** — Project purpose, structure, and high-level goals
3. **`.github/agents/`** — Find your agent file and read it completely
4. **`docs/conventions.md`** — Standards you must follow for code, git, and testing
5. **`docs/glossary.md`** — Terminology used throughout the project
6. **`docs/architecture.md`** — Understand past decisions before making new ones

### Step 2: Understand Your Role

Your agent file in `.github/agents/` defines:
- What you are responsible for
- What you should **not** do (scope boundaries)
- Your quality bar — the minimum standard for your output
- How to hand off work to other roles
- When to escalate instead of deciding on your own

If your task involves coordinating multiple roles or managing a workflow end-to-end, see the **Orchestrator** agent (`.github/agents/orchestrator.agent.md`) for workflow coordination guidance.

If your agent file is missing or unclear, escalate to the human operator before proceeding.

### Step 3: Orient to the Codebase

- Review the directory structure to understand how the project is organized
- Read recent commit messages to understand what's actively changing
- Check for open issues or PRs to understand current priorities
- Look for existing tests to understand expected behavior

### First Task Checklist for Agents

- [ ] Read your agent file
- [ ] Read `docs/conventions.md` and `docs/glossary.md`
- [ ] Review the project directory structure
- [ ] Identify the relevant source files for your task
- [ ] Check for existing tests related to your task
- [ ] Confirm you understand the expected output before writing code
- [ ] Run existing tests to establish a baseline before making changes

## For Humans

### Step 1: Set Up the Development Environment

1. Clone the repository
2. Install dependencies per the project's package manager
3. Run the test suite to verify your setup works
4. Review `docs/conventions.md` for the standards you'll follow

### Step 2: Understand the Agent Framework

- Read the agent files in `.github/agents/` to understand what agents exist
- Review `docs/glossary.md` for framework-specific terminology
- Check `docs/architecture.md` for key decisions and their rationale

### Step 3: Working with Agents

**Invoking agents:** Assign work by specifying the role and task clearly. Provide context — agents don't carry state between sessions.

**Reviewing agent work:** Treat agent output like a pull request from a junior contributor. Check for:
- Adherence to conventions and quality bars
- Correct scope — did the agent stay within its role?
- Test coverage and passing CI
- Sensible commit messages and PR descriptions

**Providing feedback:** Be specific. "This is wrong" doesn't help an agent improve. "The function should return a list, not a single item, because the API supports batch responses" does.

### Step 4: Maintaining the Framework

- Update agent files when roles evolve
- Add new ADRs when significant decisions are made
- Keep the glossary current as terminology develops
- Review and refine workflows based on what's working

## Shared: Definition of Ready

Before starting any task, every contributor should be able to answer:

1. **What** am I building or changing?
2. **Why** is this needed? (Link to issue, ADR, or explicit goal)
3. **How** will I verify it works? (Tests, review, demo)
4. **Who** do I hand off to when I'm done?

If you can't answer all four, gather more context before starting.
