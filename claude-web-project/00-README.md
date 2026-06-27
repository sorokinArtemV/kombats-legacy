# Claude Web Project Kit — Kombats Companion

Два файла для создания Claude Project, который будет твоим аналитиком/наставником при
воссоздании Kombats руками.

## Файлы
- **`01-kombats-project-context.md`** — knowledge-файл: верхнеуровневая разведка эталонного
  проекта (структура, сервисы, доменный поток, стек, паттерны, инфра, порядок воссоздания).
- **`02-kombats-agent-instructions.md`** — custom instructions проекта: роль агента, как он
  учит/направляет/ревьюит, какие правила защищает.
- **`03-diagram-prompts.md`** — готовые copy-paste промпты, чтобы агент рисовал профессиональные
  схемы в Mermaid (C4-карта сервисов, messaging/sequence, ER-схемы БД, deployment в Azure,
  realtime/SignalR, state machine боя, frontend-слои, matchmaking concurrency).
- **`diagrams/`** — 8 готовых эталонных Mermaid-схем, построенных по реальному коду
  (см. `diagrams/00-INDEX.md`). Образец для сверки с тем, что нарисует веб-агент.

## Как собрать проект в claude.ai
1. claude.ai → **Projects** → **Create project** (назови, напр. `Kombats Companion`).
2. Открой **Set custom instructions** → вставь содержимое `02-kombats-agent-instructions.md`.
3. Открой **Add to project knowledge** → загрузи `01-kombats-project-context.md`.
4. (Опц.) Догружай в knowledge свои текущие файлы/наработки по мере воссоздания —
   агент будет сверять их с эталоном.

## Как обновлять
Срез в `01-...md` — снимок на конкретный момент. Если эталон меняется или ты хочешь углубить
раздел — обнови файл и перезалей его в knowledge проекта.
