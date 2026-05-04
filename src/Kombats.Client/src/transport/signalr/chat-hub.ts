import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { config } from '@/config';
import type { ConnectionState } from './connection-state';
import type {
  JoinGlobalChatResponse,
  SendDirectMessageResponse,
  GlobalMessageEvent,
  DirectMessageEvent,
  PlayerOnlineEvent,
  PlayerOfflineEvent,
  ChatErrorEvent,
} from '@/types/chat';

const RECONNECT_DELAYS = [0, 1000, 2000, 5000, 10000, 30000];

export type ChatHubEvents = {
  onGlobalMessageReceived?: (data: GlobalMessageEvent) => void;
  onDirectMessageReceived?: (data: DirectMessageEvent) => void;
  onPlayerOnline?: (data: PlayerOnlineEvent) => void;
  onPlayerOffline?: (data: PlayerOfflineEvent) => void;
  onChatError?: (data: ChatErrorEvent) => void;
  onChatConnectionLost?: () => void;
  onConnectionStateChanged?: (state: ConnectionState) => void;
};

export class ChatHubManager {
  private connection: HubConnection | null = null;
  private events: ChatHubEvents = {};
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

  setEventHandlers(handlers: ChatHubEvents): void {
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
        .withUrl(`${config.bff.baseUrl}/chathub`, {
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

  async joinGlobalChat(): Promise<JoinGlobalChatResponse> {
    this.assertConnected();
    return await this.connection!.invoke<JoinGlobalChatResponse>('JoinGlobalChat');
  }

  async sendGlobalMessage(content: string): Promise<void> {
    this.assertConnected();
    await this.connection!.invoke('SendGlobalMessage', content);
  }

  async sendDirectMessage(
    recipientPlayerId: string,
    content: string,
  ): Promise<SendDirectMessageResponse> {
    this.assertConnected();
    return await this.connection!.invoke<SendDirectMessageResponse>(
      'SendDirectMessage',
      recipientPlayerId,
      content,
    );
  }

  private registerEvents(conn: HubConnection): void {
    conn.on('GlobalMessageReceived', (data: GlobalMessageEvent) =>
      this.events.onGlobalMessageReceived?.(data),
    );
    conn.on('DirectMessageReceived', (data: DirectMessageEvent) =>
      this.events.onDirectMessageReceived?.(data),
    );
    conn.on('PlayerOnline', (data: PlayerOnlineEvent) => this.events.onPlayerOnline?.(data));
    conn.on('PlayerOffline', (data: PlayerOfflineEvent) => this.events.onPlayerOffline?.(data));
    conn.on('ChatError', (data: ChatErrorEvent) => this.events.onChatError?.(data));
    conn.on('ChatConnectionLost', () => this.events.onChatConnectionLost?.());
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
      throw new Error('ChatHubManager: not connected');
    }
  }
}
