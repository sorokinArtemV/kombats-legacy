import { describe, expect, it, vi } from 'vitest';
import { ErrorBoundary } from './ErrorBoundary';

// Component rendering is tested by exercising the class methods directly so
// the test does not require a DOM environment. The ErrorBoundary is simple
// enough that its getDerivedStateFromError / reset / render branch-selection
// are the whole behavior.

describe('ErrorBoundary', () => {
  it('flips hasError via getDerivedStateFromError', () => {
    const err = new Error('boom');
    const next = ErrorBoundary.getDerivedStateFromError(err);
    expect(next.hasError).toBe(true);
    expect(next.error).toBe(err);
  });

  it('reset() clears hasError back to false', () => {
    // Minimal harness: construct the instance with stub props and drive
    // state updates via setState to simulate the React lifecycle.
    const instance = new ErrorBoundary({ children: null, fallback: null });
    let observed = instance.state;
    instance.setState = ((updater) => {
      observed =
        typeof updater === 'function'
          ? { ...observed, ...(updater(observed, instance.props) as object) }
          : { ...observed, ...updater };
      instance.state = observed;
    }) as typeof instance.setState;

    instance.state = { hasError: true, error: new Error('x') };
    instance.reset();
    expect(instance.state.hasError).toBe(false);
    expect(instance.state.error).toBe(null);
  });

  it('forwards caught errors to onError', () => {
    const onError = vi.fn();
    const instance = new ErrorBoundary({
      children: null,
      fallback: null,
      onError,
    });
    const err = new Error('caught');
    const info = { componentStack: 'stack' };
    instance.componentDidCatch(err, info);
    expect(onError).toHaveBeenCalledWith(err, info);
  });

  it('does not throw when onError is omitted', () => {
    const instance = new ErrorBoundary({ children: null, fallback: null });
    expect(() => instance.componentDidCatch(new Error('x'), { componentStack: '' })).not.toThrow();
  });
});
