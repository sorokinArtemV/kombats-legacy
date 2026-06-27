import { motion, useReducedMotion } from 'motion/react';
import mitsudamoeSrc from '@/ui/assets/icons/mitsudamoe.png';

// Radial gold halo and hairline-ring alpha/mix-blend effects are not expressible
// as Tailwind utilities — scoped style blocks copy the CSS from DESIGN_REFERENCE §3.12.
const glowStyle = {
  background:
    'radial-gradient(circle, rgba(var(--rgb-gold-accent), 0.18) 0%, rgba(var(--rgb-gold-accent), 0.08) 35%, rgba(var(--rgb-gold-accent), 0.03) 60%, transparent 80%)',
};

const ringStyle = {
  border: '1px solid rgba(var(--rgb-gold-accent), 0.15)',
};

export function SplashScreen() {
  const reduceMotion = useReducedMotion();

  return (
    <div
      className="flex min-h-screen flex-col items-center justify-center gap-6 bg-kombats-ink-navy px-4 text-center"
      role="status"
      aria-live="polite"
      aria-label="Loading Kombats"
    >
      <div className="relative flex h-80 w-80 items-center justify-center">
        <div
          aria-hidden
          className="pointer-events-none absolute h-80 w-80 rounded-full"
          style={glowStyle}
        />
        <motion.div
          aria-hidden
          className="pointer-events-none absolute h-[220px] w-[220px] rounded-full"
          style={ringStyle}
          animate={reduceMotion ? undefined : { rotate: -360 }}
          transition={reduceMotion ? undefined : { duration: 12, repeat: Infinity, ease: 'linear' }}
        />
        <motion.img
          src={mitsudamoeSrc}
          alt=""
          aria-hidden
          className="pointer-events-none h-[140px] w-[140px] opacity-50 mix-blend-screen"
          animate={reduceMotion ? undefined : { rotate: 360 }}
          transition={reduceMotion ? undefined : { duration: 8, repeat: Infinity, ease: 'linear' }}
        />
      </div>

      <motion.span
        className="font-display text-[14px] font-semibold uppercase tracking-[0.24em] text-accent-muted"
        animate={reduceMotion ? undefined : { opacity: [0.6, 1, 0.6] }}
        transition={reduceMotion ? undefined : { duration: 3, repeat: Infinity, ease: 'easeInOut' }}
      >
        Loading
      </motion.span>
    </div>
  );
}
