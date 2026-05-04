import { Link } from 'react-router';
import mitsudamoeIcon from '@/ui/assets/icons/mitsudamoe.png';

// Branded 404 rendered by the router's top-level catch-all. Composition
// mirrors DESIGN_REFERENCE.md §1.9: glass panel + mitsudomoe mark +
// Cinzel 404 + "Path Not Found" eyebrow + single primary CTA. The
// "/" target is intentional — UnauthenticatedShell forwards authenticated
// users to /lobby; unauthenticated users land on the entry screen.
export function NotFoundScreen() {
  return (
    <div
      role="alert"
      aria-labelledby="not-found-title"
      className="flex min-h-screen items-center justify-center bg-kombats-ink-navy px-6"
    >
      <div className="flex w-full max-w-md flex-col items-center gap-6 rounded-md border-[0.5px] border-border-subtle bg-glass p-10 text-center shadow-[var(--shadow-panel)] backdrop-blur-[20px]">
        <img
          src={mitsudamoeIcon}
          alt=""
          aria-hidden="true"
          width={100}
          height={100}
          className="opacity-35"
        />

        <h1
          id="not-found-title"
          className="font-display text-[64px] font-bold uppercase leading-none tracking-[0.16em] text-accent-primary"
          // Cinzel title bloom per DESIGN_REFERENCE.md §3.4 — gold halo
          // text-shadow isn't expressible via a static Tailwind utility.
          style={{ textShadow: 'var(--shadow-title-neutral)' }}
        >
          404
        </h1>

        <p className="font-display text-[11px] uppercase tracking-[0.24em] text-text-muted">
          Path Not Found
        </p>

        <Link
          to="/"
          className="inline-flex items-center justify-center rounded-md bg-accent-primary px-6 py-2.5 text-[13px] font-medium uppercase tracking-[0.18em] text-text-on-accent transition-colors duration-150 hover:bg-kombats-gold-light"
        >
          Return Home
        </Link>
      </div>
    </div>
  );
}
