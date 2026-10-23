# 🌌 Game Universe

An open, ad-free online board game platform built with **.NET Core** and **SignalR**. 

Game Universe is designed as an extensible multiplayer engine where developers can plug in custom turn-based board games. The platform ships with two initial titles:

- **Chess** (2 players)
- **Monopoly** (2–6 players)

No ads, no trackers—just clean peer-to-peer style multiplayer over WebSockets.

---

## 🚀 Key Features

- **Ad-Free Multiplayer:** Pure gameplay without promotional interruptions.
- **Pluggable Game Architecture:** Add any new turn-based game by implementing the core `IGameEngine` interface.
- **Real-Time Synchronization:** Powered by ASP.NET Core SignalR for low-latency state updates and room management.
- **Authoritative Server:** Game rules and actions are validated strictly on the backend to prevent cheating.

---

## 🛠️ Tech Stack

- **Backend:** .NET Core (C#)
- **Real-Time Networking:** ASP.NET Core SignalR
- **Architecture:** Interface-driven Modular Monolith

---

## 📁 Repository Structure

```text
GameUniverse/
├── src/
│   ├── GameUniverse.Core/           # Core room management and IGameEngine contract
│   ├── GameUniverse.Games.Chess/    # Chess rule validation and state machine
│   ├── GameUniverse.Games.Monopoly/ # Monopoly rule validation and board state
│   ├── GameUniverse.Server/         # SignalR hubs and API entry point
│   └── GameUniverse.Client/         # Frontend web application
├── tests/                           # Unit and integration tests
└── LICENSE                          # Non-commercial license terms
