// Thin wrapper over the SignalR JS client. Every call resolves to { ok, error, data }; server events are
// re-dispatched as DOM CustomEvents whose `detail` holds the payload.

const signalR = window.signalR;

const EVENTS = {
  PlayerJoinedRoom: (roomCode, player) => ({ roomCode, player }),
  PlayerLeftRoom: (roomCode, playerId) => ({ roomCode, playerId }),
  PlayerReconnecting: (roomCode, playerId, secondsLeft) => ({ roomCode, playerId, secondsLeft }),
  PlayerReconnected: (roomCode, playerId) => ({ roomCode, playerId }),
  ReadyStateChanged: (view) => view,
  GameStarted: (view) => view,
  StateUpdated: (view) => view,
  ActionRejected: (roomCode, reason) => ({ roomCode, reason }),
  DrawOffered: (roomCode, playerId) => ({ roomCode, playerId }),
  DrawDeclined: (roomCode, playerId) => ({ roomCode, playerId }),
  RematchRequested: (roomCode, playerId) => ({ roomCode, playerId }),
  GameOver: (roomCode, result) => ({ roomCode, result }),
  ChatMessageReceived: (roomCode, message) => ({ roomCode, message }),
};

export class HubClient extends EventTarget {
  constructor(url = document.body.dataset.hubUrl ?? "/hubs/game") {
    super();
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect([0, 1000, 2000, 4000, 8000, 15000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    for (const [name, shape] of Object.entries(EVENTS)) {
      this.connection.on(name, (...args) => this.dispatchEvent(new CustomEvent(name, { detail: shape(...args) })));
    }
    this.connection.onreconnecting(() => this.dispatchEvent(new CustomEvent("connection-lost")));
    this.connection.onreconnected(() => this.dispatchEvent(new CustomEvent("connection-restored")));
    this.connection.onclose(() => this.dispatchEvent(new CustomEvent("connection-closed")));
  }

  get connected() {
    return this.connection.state === signalR.HubConnectionState.Connected;
  }

  async start() {
    if (this.connection.state === signalR.HubConnectionState.Disconnected) {
      await this.connection.start();
    }
  }

  async #call(method, ...args) {
    try {
      const response = await this.connection.invoke(method, ...args);
      if (response && typeof response.ok === "boolean") {
        return { ok: response.ok, error: response.error ?? null, data: response.data ?? null };
      }
      return { ok: true, error: null, data: response };
    } catch (err) {
      const message = String(err?.message ?? err);
      const code = message.match(/RATE_LIMITED/)?.[0] ?? "UNKNOWN";
      return { ok: false, error: code, data: null };
    }
  }

  createRoom(request) { return this.#call("CreateRoom", request); }
  joinRoom(request) { return this.#call("JoinRoom", request); }
  rejoinRoom(roomCode, playerId) { return this.#call("RejoinRoom", roomCode, playerId); }
  leaveRoom(roomCode) { return this.#call("LeaveRoom", roomCode); }
  setReady(roomCode, ready) { return this.#call("SetReady", roomCode, ready); }
  startGame(roomCode) { return this.#call("StartGame", roomCode); }
  makeMove(roomCode, action, expectedVersion) { return this.#call("MakeMove", roomCode, JSON.stringify(action), expectedVersion); }
  resign(roomCode) { return this.#call("Resign", roomCode); }
  offerDraw(roomCode) { return this.#call("OfferDraw", roomCode); }
  respondDraw(roomCode, accept) { return this.#call("RespondDraw", roomCode, accept); }
  requestRematch(roomCode) { return this.#call("RequestRematch", roomCode); }
  sendChat(roomCode, message) { return this.#call("SendChat", roomCode, message); }
}
