import { useCallback, useState, type FocusEvent } from 'react';
import { LogOut } from 'lucide-react';
import { useAuth } from '@/modules/auth/hooks';

// Glass header surface — multi-stop alpha gradient that can't be expressed
// as a Tailwind utility, plus the `ease` timing curve for the border-color
// reveal (Tailwind's default is ease-in-out). Mirrors the panelStyle in
// design_V2/composed/TopNavBar.tsx exactly.
const headerSurfaceStyle = {
  background:
    'linear-gradient(to bottom, rgba(0, 0, 0, 0.55) 0%, rgba(var(--rgb-ink-navy), 0.35) 50%, transparent 100%)',
  transition: 'border-color 300ms ease',
};

// Content-row opacity transition uses ease (300ms) to match design_V2.
const contentRowStyle = {
  transition: 'opacity 300ms ease',
};

// Wordmark gold halo behind the KOMBATS letters.
const wordmarkBloomStyle = {
  textShadow: 'var(--shadow-title-soft)',
};

export function AppHeader() {
  const { logout } = useAuth();
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const revealed = hovered || focused;

  const handleBlur = useCallback((e: FocusEvent<HTMLElement>) => {
    const next = e.relatedTarget instanceof Node ? e.relatedTarget : null;
    if (!e.currentTarget.contains(next)) {
      setFocused(false);
    }
  }, []);

  return (
    <header
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={handleBlur}
      className={`relative w-full border-b backdrop-blur-[20px] ${
        revealed ? 'border-accent-primary/40' : 'border-border-subtle'
      }`}
      style={headerSurfaceStyle}
    >
      <div
        className={`flex items-center justify-between px-8 py-3 ${
          revealed ? 'opacity-100' : 'opacity-70'
        }`}
        style={contentRowStyle}
      >
        <div className="flex items-center gap-3">
          <div
            aria-hidden
            className="kombats-diamond"
            style={
              {
                '--kombats-diamond-size': '36px',
                '--kombats-diamond-glyph-size': '18px',
                boxShadow: 'var(--shadow-accent-soft)',
              } as React.CSSProperties
            }
          >
            <span className="kombats-diamond-glyph">拳</span>
          </div>
          <div className="flex flex-col leading-none">
            <span className="text-[9px] uppercase tracking-[0.5em] text-text-muted">The</span>
            <span
              className="mt-1.5 font-wordmark text-[22px] font-semibold leading-none tracking-[0.34em] text-accent-primary"
              style={wordmarkBloomStyle}
            >
              KOMBATS
            </span>
          </div>
        </div>

        <nav className="flex items-center">
          <IconActionButton
            icon={<LogOut className="h-4 w-4" aria-hidden />}
            label="Logout"
            onClick={logout}
          />
        </nav>
      </div>
    </header>
  );
}

interface IconActionButtonProps {
  icon: React.ReactNode;
  label: string;
  onClick: () => void;
}

function IconActionButton({ icon, label, onClick }: IconActionButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      className="p-2 text-text-muted transition-colors hover:text-accent-primary focus:text-accent-primary focus:outline-none"
    >
      {icon}
    </button>
  );
}
