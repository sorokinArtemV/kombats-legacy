# Kombats — Frontend 4-слойная архитектура

React 19 SPA. Слои и реальные папки из `src/Kombats.Client/src`. Стрелки — разрешённые
зависимости (сверху вниз). Запрещённые направления — в таблице.

```mermaid
flowchart TB
  classDef app fill:#1e3a5f,stroke:#4a90d9,color:#fff
  classDef mod fill:#1e4d2b,stroke:#4ad97a,color:#fff
  classDef tr fill:#5f3a1e,stroke:#d99a4a,color:#fff
  classDef ui fill:#3a1e5f,stroke:#9a4ad9,color:#fff
  classDef ty fill:#444,stroke:#aaa,color:#fff

  subgraph App["app/ — shell · router · guards · shells"]
    A["App · router · guards · GameStateLoader · crash-recovery"]:::app
  end

  subgraph Modules["modules/ — фичи (стейт + экраны + компоненты)"]
    Mauth["auth"]:::mod
    Monb["onboarding"]:::mod
    Mpl["player"]:::mod
    Mmm["matchmaking"]:::mod
    Mbt["battle"]:::mod
    Mch["chat"]:::mod
  end

  subgraph Transport["transport/ — НЕТ React/Zustand/Query"]
    Thttp["http"]:::tr
    Tsig["signalr"]:::tr
    Tpoll["polling"]:::tr
  end

  subgraph UI["ui/ — stateless примитивы"]
    Ucomp["components"]:::ui
    Uhook["hooks"]:::ui
    Utheme["theme"]:::ui
  end

  Types["types/ — общие TS-типы"]:::ty

  A --> Modules
  Modules --> Transport
  Modules --> UI
  Transport -->|HTTP + SignalR| BFF["BFF (backend)"]
  Modules -.-> Types
  Transport -.-> Types
  UI -.-> Types
```

## Forbidden patterns — правило → почему

| Правило | Почему |
|---|---|
| Сетевые вызовы только через `transport/` (нет `fetch()` / `new HubConnection()` в компонентах) | Изоляция транспорта, тестируемость, единая точка ретраев/ошибок |
| `transport/` без React / Zustand / TanStack Query | Транспорт — чистый, переиспользуемый, без UI-зависимостей |
| `ui/` — stateless (без сторов и транспорта) | Примитивы переиспользуемы и предсказуемы |
| Модуль не пишет в чужой стор | Границы фич, нет скрытых связей |
| Auth-токены только в памяти (не `localStorage`) | XSS-риск из-за чата (DEC-6) |
| Данные через TanStack Query, не `useEffect` | Кэш, ретраи, инвалидация из коробки |
| Роуты — проекция состояния (нет `navigate()` в фичах) | Предсказуемая навигация, единый источник истины |
| Только Tailwind + CSS-переменные; named exports; без `React.FC`; без `any` | Единый стиль, strict TypeScript |
