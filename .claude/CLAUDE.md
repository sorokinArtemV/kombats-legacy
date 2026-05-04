# CLAUDE.md — Kombats Frontend

## Active Lane: Frontend Implementation

The **frontend** (React SPA web client) is the current active implementation lane, following a 10-phase plan (P0–P9). The frontend architecture is defined in `docs/frontend/04-frontend-client-architecture.md` and is **binding**.

Top-level `.claude/` assets (rules, prompts, skills) are all **frontend-specific**. Use them directly.

| Asset | Location |
|-------|----------|
| Rules | `.claude/rules/` |
| Prompts | `.claude/prompts/` |
| Skills | `.claude/skills/` |
| Architecture (binding) | `docs/frontend/04-frontend-client-architecture.md` |
| Implementation plan | `docs/frontend/06-frontend-implementation-plan.md` |
| Task breakdown | `docs/frontend/07-frontend-task-breakdown.md` |
| Keycloak integration | `docs/frontend/05-keycloak-web-client-integration.md` |
| Product requirements | `docs/frontend/02-client-product-and-architecture-requirements.md` |
| Flow validation | `docs/frontend/03-flow-feasibility-validation.md` |

## Backend Lane: Archived

The backend (.NET services + BFF) is **functionally complete** and in post-implementation hardening. Backend Claude assets are preserved in `.claude/backend/` and are **not loaded by default**. Read them only when working on backend code (`src/Kombats.*`).

| Asset | Location |
|-------|----------|
| Backend rules | `.claude/backend/rules/` |
| Backend prompts | `.claude/backend/prompts/` |
| Backend skills | `.claude/backend/skills/` |
| Backend review checklist | `.claude/backend/review-checklist.md` |
| Backend architecture docs | `.claude/backend/docs/architecture/` |
| Backend implementation bootstrap | `.claude/backend/docs/implementation-bootstrap/` |
| Backend tickets | `.claude/backend/docs/tickets/` |
| BFF overlays | `.claude/backend/tasks/` |

To resume backend work: read `.claude/backend/rules/hardening-mode.md` and use backend prompts/skills from `.claude/backend/`.

---

## Repository Context

Kombats is a monorepo with a .NET 10.0 backend and a React 19 frontend client.

**Backend** (complete, hardening-only): Players, Matchmaking, Battle services + BFF. Async messaging via RabbitMQ/MassTransit 8.3.0. PostgreSQL with schema-per-service. Redis for Battle (DB 0) and Matchmaking (DB 1).

**Frontend** (active implementation): React 19 + Vite SPA. Communicates with backend exclusively through the BFF layer (HTTP + SignalR). Keycloak OIDC for authentication.

---

## Frontend Tech Stack (Non-Negotiable)

| Concern | Technology | Version |
|---------|-----------|---------|
| Framework | React | 19 |
| Build | Vite | latest |
| Routing | React Router | 7 |
| Client state | Zustand | 5 |
| Server state | TanStack Query | 5 |
| Realtime | @microsoft/signalr | 8 |
| Auth | oidc-client-ts + react-oidc-context | latest |
| Styling | Tailwind CSS | 4 |
| Accessibility | Radix UI primitives | latest |
| Animation | Framer Motion | latest |
| Testing | Vitest | latest |

No new npm packages without explicit justification. Do not propose alternatives to the above.

---

## Frontend Architecture Boundaries

Four layers with strict separation:

```
app/          → Shell, routing, guards, entry point
modules/      → Feature state, screens, feature components
transport/    → HTTP, SignalR, polling — no UI, no React, no stores
ui/           → Stateless, theme-driven primitives — no business logic
types/        → Shared TypeScript definitions
```

- `transport/` has no React, no Zustand, no TanStack Query
- `ui/` components are stateless — no stores, no transport
- Modules own their stores exclusively — no cross-module store writes
- All network calls go through `transport/` — no `fetch()` in components
- Auth tokens in-memory only — no `localStorage` (DEC-6: XSS risk with chat)
- Routes are state projections — no `navigate()` in feature components

See `.claude/rules/architecture-boundaries.md` for full rules.

---

## Frontend Forbidden Patterns

| Pattern | Why |
|---|---|
| `fetch()` or `new HubConnection()` in components/stores | Transport isolation violation |
| `localStorage` for auth tokens | Security violation (DEC-6) |
| CSS modules, styled-components, inline styles for static values | Tailwind + CSS variables only |
| Hardcoded color/spacing values | Use CSS variable tokens |
| Default exports | Named exports only |
| `React.FC` | Plain functions with typed props |
| `any` without justification | TypeScript strict mode |
| `useEffect` for data fetching | TanStack Query |
| Snapshot tests | Low signal, brittle |
| Cross-module store writes | Module boundary violation |
| `navigate()` in feature components | State-driven routing |

---

## Workflow Modes

| Mode | Prompt | Purpose |
|------|--------|---------|
| Planner | `.claude/prompts/planner.md` | Plan a phase (P0–P9) or bug fix |
| Implementer | `.claude/prompts/implementer.md` | Execute an approved plan |
| Reviewer | `.claude/prompts/reviewer.md` | Review for architecture compliance and scope |
| Architecture Review | `.claude/prompts/architecture-compliance-review.md` | Focused layer/import/state review |

---

## Execution State

Before starting work related to execution tracking, also check:

- `docs/execution/execution-log.md`
- `docs/execution/execution-issues.md`

Trust architecture docs and actual repo state over execution logs if they conflict.

---

## Truthfulness

Code is the source of truth. Distinguish between: observed in code, inferred from code, unknown/ambiguous, recommended change. Do not present guesses as facts. Do not invent intended behavior.
