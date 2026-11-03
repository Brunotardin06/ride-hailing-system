# Frontend

React + TypeScript + Vite. One app with three areas: **/passenger**, **/driver** and **/experiments**.

```bash
npm install
npm run dev      # http://localhost:5173
npm run build    # type-check + production build
npm run lint     # oxlint
```

Service URLs come from `VITE_*` variables (`.env.example`; the defaults match the standard ports).

## How it talks to the backend

- **REST** (`src/api/client.ts`): every action goes directly to the owning service: RideService, DriverService, AnalyticsService, and MatchingService for experiment controls only.
- **SignalR** (`src/realtime/RealtimeProvider.tsx`): one connection per tab to RealtimeService. It:
  - registers the tab as passenger or driver;
  - receives notifications and chat;
  - acknowledges every message (used for delivery metrics);
  - re-registers after a reconnect.

  Notifications only trigger a REST reload of the current state.

Each tab picks its own identity (stored in `sessionStorage`), so a passenger tab and a driver tab can run side by side.

## Structure

```
src/
├── api/                 REST client + types
├── realtime/            SignalR provider and hook
├── state/               Identity, area contexts, toasts
├── components/
│   ├── layout/          App shell, navigation, identity bar
│   ├── ui/              Card, ChatPanel, RideTimeline, LiveFeed, badges…
│   └── charts/          Small SVG charts (bar, line, column) with tooltips
├── features/
│   ├── passenger/       Area layout + request, current ride, history, chat
│   ├── driver/          Area layout + dashboard, incoming requests, active ride, chat
│   └── experiments/     Dashboard tabs: metrics, runs, business events, Kafka
└── styles/              global.css (tokens, layout) + app.css (components)
```
