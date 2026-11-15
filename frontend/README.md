# Frontend

React + TypeScript + Vite. One app with two areas: **/passenger** and **/driver**.

```bash
npm install
npm run dev      # http://localhost:5173
npm run build    # type-check + production build
npm run lint     # oxlint
```

Service URLs come from `VITE_*` variables (`.env.example`; the defaults match the standard ports).

## How it talks to the backend

- **REST** (`src/api/client.ts`): every action goes directly to the owning service: RideService and DriverService.
- **SignalR** (`src/realtime/RealtimeProvider.tsx`): one connection per tab to RealtimeService. It:
  - registers the tab as passenger or driver;
  - receives notifications, chat and the driver's live position;
  - sends the driver's position (driver tabs);
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
│   └── ui/              Card, ChatPanel, RideTimeline, TripMap, LiveFeed, badges…
├── features/
│   ├── passenger/       Area layout + request, current ride, history, chat
│   └── driver/          Area layout + dashboard, incoming requests, active ride, chat, location
└── styles/              global.css (tokens, layout) + app.css (components)
```
