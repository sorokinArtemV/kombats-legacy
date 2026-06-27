import { Outlet } from 'react-router';

/**
 * Lobby/matchmaking screens render in the SessionShell's central region.
 * Top header and bottom chat dock are owned by SessionShell.
 */
export function LobbyShell() {
  return (
    <div className="flex h-full min-h-0 flex-col overflow-hidden">
      <Outlet />
    </div>
  );
}
