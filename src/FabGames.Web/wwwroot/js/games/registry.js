// Game id -> renderer module. A new game adds one line here (plus its board module).

export const renderers = {
  chess: async () => (await import("./chess-board.js")).ChessBoard,
  checkers: async () => (await import("./checkers-board.js")).CheckersBoard,
};
