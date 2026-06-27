import { describe, it, expect } from 'vitest';
import { shouldRetryQuery } from './query-client';

describe('shouldRetryQuery', () => {
  it('does not retry 401 (auth-expiry handled by client)', () => {
    expect(shouldRetryQuery(0, { status: 401 })).toBe(false);
  });

  it('does not retry 403/404', () => {
    expect(shouldRetryQuery(0, { status: 403 })).toBe(false);
    expect(shouldRetryQuery(0, { status: 404 })).toBe(false);
  });

  it('does not retry 409 (conflict is caller-handled)', () => {
    expect(shouldRetryQuery(0, { status: 409 })).toBe(false);
  });

  it('retries 500 / 503 up to 3 times', () => {
    expect(shouldRetryQuery(0, { status: 500 })).toBe(true);
    expect(shouldRetryQuery(2, { status: 503 })).toBe(true);
    expect(shouldRetryQuery(3, { status: 500 })).toBe(false);
  });

  it('retries network-like errors with no status field', () => {
    expect(shouldRetryQuery(0, new Error('Network fail'))).toBe(true);
  });

  it('caps at 3 retries regardless of error shape', () => {
    expect(shouldRetryQuery(3, new Error('x'))).toBe(false);
  });
});
