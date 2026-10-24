// A game is identified by its room and start time: a rematch keeps the room's games-played count but is a new game.

export const gameKey = (view) => (view?.session ? `${view.room.code}:${view.session.startedAtMs}` : null);
