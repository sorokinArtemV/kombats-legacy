export type ConnectionState =
  | 'disconnected'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  // Terminal: automatic reconnect budget exhausted and the hub closed.
  // Distinguishes a final "gave up" state from a transient 'disconnected'
  // so the UI can switch from an optimistic "reconnecting…" indicator to an
  // honest "lost" state and offer a manual recovery path.
  | 'failed';
