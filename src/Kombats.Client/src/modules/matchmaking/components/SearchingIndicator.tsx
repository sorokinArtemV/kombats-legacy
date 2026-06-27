import { motion, useReducedMotion } from 'motion/react';
import mitsudamoeSrc from '@/ui/assets/icons/mitsudamoe.png';

// DESIGN_REFERENCE.md §3.12 — Mitsudomoe spinner (scaled-down 200/140/88
// variant for §5.10 QueueCard searching state). Radial glow + counter-rotating
// hairline ring + rotating gold icon blended into the background.
const glowStyle = {
  background:
    'radial-gradient(circle, rgba(var(--rgb-gold-accent), 0.18) 0%, rgba(var(--rgb-gold-accent), 0.08) 35%, rgba(var(--rgb-gold-accent), 0.03) 60%, transparent 80%)',
};

const ringStyle = {
  border: '1px solid rgba(var(--rgb-gold-accent), 0.15)',
};

export function SearchingIndicator() {
  const reduceMotion = useReducedMotion();

  return (
    <div className="relative flex h-[200px] w-[200px] items-center justify-center" aria-hidden>
      <div
        className="pointer-events-none absolute h-[200px] w-[200px] rounded-full"
        style={glowStyle}
      />
      <motion.div
        className="pointer-events-none absolute h-[140px] w-[140px] rounded-full"
        style={ringStyle}
        animate={reduceMotion ? undefined : { rotate: -360 }}
        transition={reduceMotion ? undefined : { duration: 12, repeat: Infinity, ease: 'linear' }}
      />
      <motion.img
        src={mitsudamoeSrc}
        alt=""
        className="pointer-events-none h-[88px] w-[88px] opacity-50"
        animate={reduceMotion ? undefined : { rotate: 360 }}
        transition={reduceMotion ? undefined : { duration: 8, repeat: Infinity, ease: 'linear' }}
      />
    </div>
  );
}
