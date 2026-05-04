import { Component, type ErrorInfo, type ReactNode } from 'react';

type FallbackRenderProps = { error: unknown; reset: () => void };
type FallbackRender = (props: FallbackRenderProps) => ReactNode;

interface ErrorBoundaryProps {
  children: ReactNode;
  fallback: ReactNode | FallbackRender;
  onError?: (error: unknown, info: ErrorInfo) => void;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: unknown;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false, error: null };

  static getDerivedStateFromError(error: unknown): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: unknown, info: ErrorInfo): void {
    this.props.onError?.(error, info);
  }

  reset = (): void => {
    this.setState({ hasError: false, error: null });
  };

  render() {
    if (!this.state.hasError) return this.props.children;

    const { fallback } = this.props;
    if (typeof fallback === 'function') {
      return fallback({ error: this.state.error, reset: this.reset });
    }
    return fallback;
  }
}
