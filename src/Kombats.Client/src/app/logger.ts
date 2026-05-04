// Lightweight logger abstraction so we have one seam for future Sentry/OTel
// wiring. `debug` and `info` are no-ops in non-DEV builds so verbose
// diagnostics cannot leak into production bundles; `warn` and `error` always
// surface because they are genuinely actionable.

const isDev = import.meta.env.DEV;

type LogArgs = unknown[];

export const logger = {
  debug(...args: LogArgs): void {
    if (!isDev) return;
    console.debug(...args);
  },
  info(...args: LogArgs): void {
    if (!isDev) return;
    console.info(...args);
  },
  warn(...args: LogArgs): void {
    console.warn(...args);
  },
  error(...args: LogArgs): void {
    console.error(...args);
  },
};
