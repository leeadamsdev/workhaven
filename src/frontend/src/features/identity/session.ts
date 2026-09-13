import { queryOptions } from '@tanstack/react-query';
import { ApiError, getJson, postJson } from '../../api/client';

export interface CurrentUser {
  id: string;
  email: string | null;
}

async function currentUser(signal: AbortSignal): Promise<CurrentUser | null> {
  let data: unknown;
  try {
    data = await getJson('/api/auth/me', signal);
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null;
    throw error;
  }

  if (
    typeof data !== 'object' ||
    data === null ||
    !('id' in data) ||
    typeof data.id !== 'string' ||
    !data.id ||
    !('email' in data) ||
    (data.email !== null && typeof data.email !== 'string')
  ) {
    throw new Error('Invalid account response.');
  }

  return { id: data.id, email: data.email };
}

export const sessionQuery = queryOptions({
  queryKey: ['identity', 'session'],
  queryFn: ({ signal }) => currentUser(signal),
  retry: false,
  networkMode: 'always',
});

async function csrfHeaders(): Promise<Record<string, string>> {
  // Tokens are identity-bound. Fetch a fresh token for each session-changing request.
  const data = await getJson('/api/auth/csrf');
  if (
    typeof data !== 'object' ||
    data === null ||
    !('requestToken' in data) ||
    typeof data.requestToken !== 'string' ||
    !data.requestToken
  ) {
    throw new Error('Invalid CSRF response.');
  }

  return { 'X-CSRF-TOKEN': data.requestToken };
}

export async function signIn(email: string, password: string): Promise<void> {
  await postJson(
    '/api/auth/login',
    { email: email.trim(), password },
    await csrfHeaders(),
  );
}

export async function signOut(): Promise<void> {
  await postJson('/api/auth/logout', undefined, await csrfHeaders());
}
