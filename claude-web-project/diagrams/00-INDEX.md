# Kombats — Reference Diagrams (эталонные схемы)

Готовые Mermaid-схемы, построенные по **реальному коду** эталонного проекта. Используй их как
образец для сверки с тем, что нарисует веб-агент, или напрямую в доках/презентациях.

**Как смотреть:** в claude.ai / GitHub / Notion / Obsidian рендерится само. В VS Code —
расширение «Markdown Preview Mermaid Support». Быстрее всего — скопировать блок ` ```mermaid `
на **mermaid.live** и экспортировать PNG/SVG.

| Файл | Схема | Тип |
|---|---|---|
| `01-c4-container.md` | Карта сервисов и связей (C4 container) | flowchart |
| `02-messaging-sequence.md` | Gameplay-loop + каталог событий | sequence + таблица |
| `03-er-databases.md` | ER-схемы 4 БД (schema-per-service) + Redis vs Postgres | erDiagram ×4 |
| `04-deployment-azure.md` | Деплой в Azure Container Apps (Bicep) | flowchart |
| `05-realtime-signalr.md` | Realtime: backplane + BFF Relay → клиент | flowchart + sequence |
| `06-battle-state-machine.md` | Жизненный цикл матча и боя | stateDiagram |
| `07-frontend-layers.md` | 4 слоя React-клиента + forbidden patterns | flowchart |
| `08-matchmaking-concurrency.md` | Lease-lock и гонки при N репликах | sequence |

> Это снимок по коду на момент сборки. Промпты, которыми веб-агент рисует свои версии, —
> в `../03-diagram-prompts.md`.
