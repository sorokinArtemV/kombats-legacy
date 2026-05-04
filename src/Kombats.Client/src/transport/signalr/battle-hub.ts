import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { config } from '@/config';
import type { ConnectionState } from './connection-state';
import type {
  BattleSnapshotRealtime,
  BattleReadyRealtime,
  TurnOpenedRealtime,
  PlayerDamagedRealtime,
  TurnResolvedRealtime,
  BattleStateUpdatedRealtime,
  BattleEndedRealtime,
  BattleFeedUpdate,
} from '@/types/battle';

const RECONNECT_DELAYS = [0, 1000, 2000, 5000, 10000, 30000];

// JoinBattle retry budget — tolerates the transient window between
// matchmaking publishing CreateBattle (and updating player status to Matched
// with a battleId) and the Battle service's CreateBattleConsumer finishing
// battle initialization in Redis. Permanent errors (auth, not-a-participant)
// still surface on the first attempt. Total budget: ~8s across 8 attempts.
const JOIN_BATTLE_RETRY_DELAYS_MS = [250, 500, 750, 1000, 1500, 2000, 2000];
// Matches the Battle service's "Battle {guid} not found" HubException. The
// JS SignalR client wraps it as "An unexpected error occurred invoking
// 'JoinBattle' on the server. HubException: Battle <id> not found", so we
// scan the message for the inner phrase.
const TRANSIENT_BATTLE_NOT_FOUND_RE = /Battle\s+[0-9a-fA-F-]+\s+not\s+found/;

function isTransientBattleNotFound(err: unknown): boolean {
  return err instanceof Error && TRANSIENT_BATTLE_NOT_FOUND_RE.test(err.message);
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export type BattleHubEvents = {
  onBattleReady?: (data: BattleReadyRealtime) => void;
  onTurnOpened?: (data: TurnOpenedRealtime) => void;
  onPlayerDamaged?: (data: PlayerDamagedRealtime) => void;
  onTurnResolved?: (data: TurnResolvedRealtime) => void;
  onBattleStateUpdated?: (data: BattleStateUpdatedRealtime) => void;
  onBattleEnded?: (data: BattleEndedRealtime) => void;
  onBattleFeedUpdated?: (data: BattleFeedUpdate) => void;
  onBattleConnectionLost?: () => void;
  onConnectionStateChanged?: (state: ConnectionState) => void;
};

export class BattleHubManager {
  private connection: HubConnection | null = null;
  private events: BattleHubEvents = {};
  private _connectionState: ConnectionState = 'disconnected';
  private accessTokenFactory: () => string;
  // Serialized queue so disconnect() never races against an in-flight start().
  // React 19 StrictMode's mount → cleanup → mount cycle would otherwise cause
  // stop() to abort negotiate and reject start() with "The connection was
  // stopped during negotiation."
  private pending: Promise<void> = Promise.resolve();
  // True once we've observed `onreconnecting` — used by `onclose` to decide
  // whether a close is a clean teardown (still `disconnected`) or terminal
  // reconnect exhaustion (`failed`).
  private reconnectAttemptSeen = false;
  // True when the consumer is intentionally tearing the connection down;
  // prevents `onclose` from flipping to 'failed' during normal unmount.
  private intentionalDisconnect = false;

  constructor(accessTokenFactory: () => string) {
    this.accessTokenFactory = accessTokenFactory;
  }

  get connectionState(): ConnectionState {
    return this._connectionState;
  }

  setEventHandlers(handlers: BattleHubEvents): void {
    this.events = handlers;
  }

  connect(): Promise<void> {
    const prev = this.pending;
    const next = (async () => {
      await prev.catch(() => {});
      if (this.connection?.state === HubConnectionState.Connected) return;

      this.setConnectionState('connecting');
      this.reconnectAttemptSeen = false;
      this.intentionalDisconnect = false;

      const conn = new HubConnectionBuilder()
        .withUrl(`${config.bff.baseUrl}/battlehub`, {
          accessTokenFactory: this.accessTokenFactory,
        })
        .withAutomaticReconnect(RECONNECT_DELAYS)
        .configureLogging(LogLevel.Warning)
        .build();

      this.registerEvents(conn);
      this.registerLifecycle(conn);
      this.connection = conn;

      try {
        await conn.start();
        this.setConnectionState('connected');
      } catch (err) {
        if (this.connection === conn) this.connection = null;
        this.setConnectionState('disconnected');
        throw err;
      }
    })();
    this.pending = next.catch(() => {});
    return next;
  }

  disconnect(): Promise<void> {
    const prev = this.pending;
    this.intentionalDisconnect = true;
    const next = (async () => {
      await prev.catch(() => {});
      const conn = this.connection;
      this.connection = null;
      if (conn) {
        await conn.stop();
      }
      this.setConnectionState('disconnected');
    })();
    this.pending = next.catch(() => {});
    return next;
  }

  async joinBattle(battleId: string): Promise<BattleSnapshotRealtime> {
    this.assertConnected();

    let lastErr: unknown;
    for (let attempt = 0; attempt <= JOIN_BATTLE_RETRY_DELAYS_MS.length; attempt++) {
      if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
        throw lastErr ?? new Error('BattleHubManager: not connected');
      }
      try {
        return await this.connection.invoke<BattleSnapshotRealtime>('JoinBattle', battleId);
      } catch (err) {
        lastErr = err;
        if (!isTransientBattleNotFound(err)) throw err;
        if (attempt === JOIN_BATTLE_RETRY_DELAYS_MS.length) break;
        await sleep(JOIN_BATTLE_RETRY_DELAYS_MS[attempt]);
      }
    }
    throw lastErr;
  }

  async submitTurnAction(battleId: string, turnIndex: number, payload: string): Promise<void> {
    this.assertConnected();
    await this.connection!.invoke('SubmitTurnAction', battleId, turnIndex, payload);
  }

  private registerEvents(conn: HubConnection): void {
    conn.on('BattleReady', (data: BattleReadyRealtime) => this.events.onBattleReady?.(data));
    conn.on('TurnOpened', (data: TurnOpenedRealtime) => this.events.onTurnOpened?.(data));
    conn.on('PlayerDamaged', (data: PlayerDamagedRealtime) => this.events.onPlayerDamaged?.(data));
    conn.on('TurnResolved', (data: TurnResolvedRealtime) => this.events.onTurnResolved?.(data));
    conn.on('BattleStateUpdated', (data: BattleStateUpdatedRealtime) =>
      this.events.onBattleStateUpdated?.(data),
    );
    conn.on('BattleEnded', (data: BattleEndedRealtime) => this.events.onBattleEnded?.(data));
    conn.on('BattleFeedUpdated', (data: BattleFeedUpdate) =>
      this.events.onBattleFeedUpdated?.(data),
    );
    conn.on('BattleConnectionLost', () => this.events.onBattleConnectionLost?.());
  }

  private registerLifecycle(conn: HubConnection): void {
    conn.onreconnecting(() => {
      this.reconnectAttemptSeen = true;
      this.setConnectionState('reconnecting');
    });

    conn.onreconnected(() => {
      this.reconnectAttemptSeen = false;
      this.setConnectionState('connected');
    });

    conn.onclose(() => {
      // Terminal 'failed' only when automatic reconnect actually ran and gave
      // up — consumer-initiated stop() sets intentionalDisconnect, and a
      // close without a prior reconnect attempt is a clean server-side end.
      const terminal = this.reconnectAttemptSeen && !this.intentionalDisconnect;
      this.reconnectAttemptSeen = false;
      this.setConnectionState(terminal ? 'failed' : 'disconnected');
    });
  }

  private setConnectionState(state: ConnectionState): void {
    this._connectionState = state;
    this.events.onConnectionStateChanged?.(state);
  }

  private assertConnected(): void {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      throw new Error('BattleHubManager: not connected');
    }
  }
}
