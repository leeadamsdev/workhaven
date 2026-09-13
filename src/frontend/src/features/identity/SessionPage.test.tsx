import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { StrictMode } from 'react';
import { BrowserRouter } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { sessionQuery } from './session';

const account = { id: 'account-id', email: 'member@example.test' };
const password = ' a long test passphrase ';

function mockApi(signedIn = false) {
  let authenticated = signedIn;
  let token = 0;
  const handlers: Record<string, () => Response | Promise<Response>> = {
    '/api/auth/me': () =>
      authenticated
        ? Response.json(account)
        : new Response(null, { status: 401 }),
    '/api/auth/csrf': () =>
      Response.json({ requestToken: `csrf-${String(++token)}` }),
    '/api/auth/login': () => {
      authenticated = true;
      return new Response(null, { status: 204 });
    },
    '/api/auth/logout': () => {
      authenticated = false;
      return new Response(null, { status: 204 });
    },
  };
  const request = vi.fn<typeof fetch>().mockImplementation(async (path) => {
    if (typeof path !== 'string')
      throw new Error('Expected a relative API path.');
    const handler = handlers[path];
    if (!handler) throw new Error(`Unexpected request: ${path}`);
    return handler();
  });
  vi.stubGlobal('fetch', request);
  return { request, handlers };
}

function renderSession(path = '/') {
  window.history.replaceState(null, '', path);
  const client = new QueryClient({
    defaultOptions: { queries: { gcTime: 0 }, mutations: { gcTime: 0 } },
  });
  const view = render(
    <StrictMode>
      <QueryClientProvider client={client}>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </QueryClientProvider>
    </StrictMode>,
  );
  return { client, ...view };
}

async function enterCredentials(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Email address'), account.email);
  await user.type(screen.getByLabelText('Password'), password);
}

function deferredResponse() {
  let resolve: (response: Response) => void = () => {
    throw new Error('Response was not initialized.');
  };
  const promise = new Promise<Response>((complete) => {
    resolve = complete;
  });
  return { promise, resolve };
}

describe('browser session', () => {
  it('can reveal and hide a password by keyboard without submitting the form', async () => {
    const api = mockApi();
    const user = userEvent.setup();
    renderSession('/sign-in');
    await enterCredentials(user);
    const input = screen.getByLabelText<HTMLInputElement>('Password');
    expect(input.type).toBe('password');
    await user.tab();
    expect(document.activeElement).toBe(
      screen.getByRole('button', { name: 'Show password' }),
    );
    await user.keyboard('{Enter}');
    expect(input.type).toBe('text');
    expect(input.value).toBe(password);
    expect(document.activeElement).toBe(
      screen.getByRole('button', { name: 'Hide password' }),
    );
    await user.keyboard('{Enter}');
    expect(input.type).toBe('password');
    expect(
      api.request.mock.calls.some(([path]) => path === '/api/auth/login'),
    ).toBe(false);
  });

  it('hides a revealed password when sign-in begins', async () => {
    const api = mockApi();
    const pending = deferredResponse();
    api.handlers['/api/auth/login'] = () => pending.promise;
    const user = userEvent.setup();
    renderSession('/sign-in');
    await enterCredentials(user);
    await user.click(screen.getByRole('button', { name: 'Show password' }));
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByRole('button', { name: 'Signing in…' });
    expect(screen.getByLabelText<HTMLInputElement>('Password').type).toBe(
      'password',
    );
    const toggle = screen.getByRole('button', { name: 'Show password' });
    expect(toggle.getAttribute('aria-disabled')).toBe('true');
    await user.click(toggle);
    expect(screen.getByLabelText<HTMLInputElement>('Password').type).toBe(
      'password',
    );
    pending.resolve(new Response(null, { status: 401 }));
    const error = await screen.findByRole('alert');
    expect(error.id).toBe(
      screen.getByLabelText('Password').getAttribute('aria-describedby'),
    );
    expect(screen.getByLabelText('Password').getAttribute('aria-invalid')).toBe(
      'true',
    );
    expect(
      screen.getByLabelText('Email address').getAttribute('aria-invalid'),
    ).toBe('true');
    expect(screen.getByLabelText<HTMLInputElement>('Password').value).toBe(
      password,
    );
    await user.click(toggle);
    expect(screen.getByLabelText<HTMLInputElement>('Password').type).toBe(
      'text',
    );
  });

  it('redirects signed-out home requests to the sign-in route', async () => {
    mockApi();
    renderSession('/');
    await screen.findByRole('button', { name: 'Sign in' });
    expect(window.location.pathname).toBe('/sign-in');
  });

  it('redirects an already signed-in browser away from the sign-in route', async () => {
    mockApi(true);
    renderSession('/sign-in');
    await screen.findByText(account.email);
    expect(window.location.pathname).toBe('/');
    expect(screen.queryByLabelText('Password')).toBeNull();
  });

  it('shows a not-found page and navigates home without reloading the document', async () => {
    const api = mockApi();
    const user = userEvent.setup();
    renderSession('/missing');
    expect(screen.getByRole('heading').textContent).toBe('Page not found');
    expect(api.request).not.toHaveBeenCalled();
    await user.click(screen.getByRole('link', { name: 'Return to Workhaven' }));
    await screen.findByRole('button', { name: 'Sign in' });
    expect(window.location.pathname).toBe('/sign-in');
  });

  it('returns to sign-in when a later session check expires', async () => {
    const api = mockApi(true);
    const { client } = renderSession();
    await screen.findByText(account.email);
    api.handlers['/api/auth/me'] = () => new Response(null, { status: 401 });
    await act(async () => {
      await client.invalidateQueries(sessionQuery);
    });
    await screen.findByRole('button', { name: 'Sign in' });
    expect(window.location.pathname).toBe('/sign-in');
    expect(screen.queryByText(account.email)).toBeNull();
  });

  it('waits for the session check before showing a form', async () => {
    const api = mockApi();
    const pending = deferredResponse();
    api.handlers['/api/auth/me'] = () => pending.promise;
    renderSession();
    expect(screen.getByRole('status').textContent).toContain('Connecting');
    expect(screen.queryByLabelText('Password')).toBeNull();
    pending.resolve(new Response(null, { status: 401 }));
    const heading = await screen.findByRole('heading', {
      name: 'Welcome back',
    });
    await waitFor(() => {
      expect(document.activeElement).toBe(heading);
    });
  });

  it('signs in by keyboard with a fresh CSRF token and restores the session after remount', async () => {
    const api = mockApi();
    const user = userEvent.setup();
    const view = renderSession();
    await screen.findByLabelText('Email address');
    await user.tab();
    expect(document.activeElement).toBe(screen.getByLabelText('Email address'));
    await user.keyboard(account.email);
    await user.tab();
    await user.keyboard(password);
    await user.tab();
    expect(document.activeElement).toBe(
      screen.getByRole('button', { name: 'Show password' }),
    );
    await user.tab();
    expect(document.activeElement).toBe(
      screen.getByRole('button', { name: 'Forgot password?' }),
    );
    await user.tab();
    await user.keyboard('{Enter}');

    const heading = await screen.findByRole('heading', {
      name: 'You’re signed in',
    });
    expect(document.activeElement).toBe(heading);
    expect(screen.getByText(account.email)).toBeTruthy();
    const login = api.request.mock.calls.find(
      ([path]) => path === '/api/auth/login',
    );
    expect(login?.[1]?.headers).toEqual({
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': 'csrf-1',
    });
    expect(login?.[1]?.body).toBe(
      JSON.stringify({ email: account.email, password }),
    );
    expect(login?.[1]?.method).toBe('POST');
    expect(screen.queryByLabelText('Password')).toBeNull();
    view.unmount();
    renderSession();
    await screen.findByRole('heading', { name: 'You’re signed in' });
    expect(
      api.request.mock.calls.filter(([path]) => path === '/api/auth/login'),
    ).toHaveLength(1);
  });

  it('shows custom field errors and focuses the first invalid input without making a request', async () => {
    const api = mockApi();
    const user = userEvent.setup();
    renderSession();
    await screen.findByLabelText('Email address');
    expect(screen.queryByText('Enter your email address.')).toBeNull();
    await user.click(await screen.findByRole('button', { name: 'Sign in' }));
    const email = screen.getByLabelText('Email address');
    const passwordInput = screen.getByLabelText('Password');
    expect(screen.getByText('Enter your email address.').id).toBe(
      email.getAttribute('aria-describedby'),
    );
    expect(screen.getByText('Enter your password.').id).toBe(
      passwordInput.getAttribute('aria-describedby'),
    );
    expect(email.getAttribute('aria-invalid')).toBe('true');
    expect(passwordInput.getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(email);
    expect(email.closest('form')?.noValidate).toBe(true);
    await user.type(screen.getByLabelText('Email address'), 'invalid-email');
    await user.type(screen.getByLabelText('Password'), password);
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(
      screen.getByText('Enter a valid email address, like you@company.com.'),
    ).toBeTruthy();
    expect(screen.queryByText('Enter your password.')).toBeNull();
    expect(document.activeElement).toBe(email);
    expect(
      api.request.mock.calls.some(
        ([path]) => path === '/api/auth/csrf' || path === '/api/auth/login',
      ),
    ).toBe(false);
    await user.clear(email);
    await user.type(email, account.email);
    expect(email.getAttribute('aria-invalid')).toBe('false');
    expect(email.getAttribute('aria-describedby')).toBeNull();
    await user.clear(passwordInput);
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(document.activeElement).toBe(passwordInput);
    await user.type(passwordInput, password);
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByText(account.email);
  });

  it('explains upcoming controls without navigating or submitting credentials', async () => {
    const api = mockApi();
    const user = userEvent.setup();
    renderSession('/sign-in');
    const remember = await screen.findByRole<HTMLInputElement>('checkbox', {
      name: /Remember me/,
    });
    expect(remember.disabled).toBe(true);
    expect(remember.checked).toBe(false);
    await user.click(screen.getByRole('button', { name: 'Forgot password?' }));
    expect(
      screen
        .getByText('Password recovery is coming soon.')
        .closest('[role="status"]'),
    ).not.toBeNull();
    await user.click(screen.getByRole('button', { name: 'Register now' }));
    expect(
      screen
        .getByText('Registration is coming soon.')
        .closest('[role="status"]'),
    ).not.toBeNull();
    expect(screen.queryByText('Password recovery is coming soon.')).toBeNull();
    expect(window.location.pathname).toBe('/sign-in');
    expect(
      api.request.mock.calls.every(([path]) => path === '/api/auth/me'),
    ).toBe(true);
  });

  it('prevents duplicate submissions and keeps the submit button focused while pending', async () => {
    const api = mockApi();
    const pending = deferredResponse();
    api.handlers['/api/auth/login'] = () => pending.promise;
    const user = userEvent.setup();
    renderSession();
    await enterCredentials(user);
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    const button = await screen.findByRole('button', { name: 'Signing in…' });
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(document.activeElement).toBe(button);
    await user.click(button);
    expect(
      api.request.mock.calls.filter(([path]) => path === '/api/auth/login'),
    ).toHaveLength(1);
    pending.resolve(new Response(null, { status: 401 }));
    await screen.findByRole('alert');
  });

  it.each([
    [401, 'Check your email and password'],
    [429, 'Wait a minute'],
    [400, 'Your session changed'],
    [503, 'Please try again'],
  ])(
    'retains the masked password for correction and retry after sign-in HTTP %s',
    async (status, message) => {
      const api = mockApi();
      const success = api.handlers['/api/auth/login'];
      api.handlers['/api/auth/login'] = () => new Response(null, { status });
      const user = userEvent.setup();
      renderSession();
      await enterCredentials(user);
      await user.click(screen.getByRole('button', { name: 'Sign in' }));
      expect((await screen.findByRole('alert')).textContent).toContain(message);
      expect(screen.getByLabelText<HTMLInputElement>('Password').value).toBe(
        password,
      );
      expect(screen.getByLabelText<HTMLInputElement>('Password').type).toBe(
        'password',
      );
      expect(
        screen.getByLabelText<HTMLInputElement>('Email address').value,
      ).toBe(account.email);
      if (!success) throw new Error('Missing sign-in handler.');
      api.handlers['/api/auth/login'] = success;
      await user.type(screen.getByLabelText('Password'), ' corrected');
      expect(screen.queryByRole('alert')).toBeNull();
      await user.click(screen.getByRole('button', { name: 'Sign in' }));
      await screen.findByRole('heading', { name: 'You’re signed in' });
      const logins = api.request.mock.calls.filter(
        ([path]) => path === '/api/auth/login',
      );
      expect(logins).toHaveLength(2);
      expect(logins[1]?.[1]?.body).toBe(
        JSON.stringify({
          email: account.email,
          password: `${password} corrected`,
        }),
      );
      expect(logins[1]?.[1]?.headers).toEqual({
        'Content-Type': 'application/json',
        'X-CSRF-TOKEN': 'csrf-2',
      });
    },
  );

  it('does not post credentials when the CSRF request fails', async () => {
    const api = mockApi();
    api.handlers['/api/auth/csrf'] = () => {
      throw new TypeError('private failure details');
    };
    const user = userEvent.setup();
    renderSession();
    await enterCredentials(user);
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect((await screen.findByRole('alert')).textContent).toContain(
      'Please try again',
    );
    expect(document.body.textContent).not.toContain('private failure details');
    expect(
      api.request.mock.calls.some(([path]) => path === '/api/auth/login'),
    ).toBe(false);
  });

  it.each(['unavailable', 'malformed'])(
    'offers session retry instead of treating %s as signed out',
    async (scenario) => {
      const api = mockApi(true);
      const success = api.handlers['/api/auth/me'];
      api.handlers['/api/auth/me'] = () =>
        scenario === 'unavailable'
          ? new Response(null, { status: 503 })
          : Response.json({ id: account.id });
      const user = userEvent.setup();
      renderSession();
      await screen.findByRole('alert');
      expect(screen.queryByLabelText('Password')).toBeNull();
      if (!success) throw new Error('Missing account handler.');
      api.handlers['/api/auth/me'] = success;
      await user.click(screen.getByRole('button', { name: 'Try again' }));
      await screen.findByText(account.email);
    },
  );

  it('offers session retry when sign-in succeeds but the account check fails', async () => {
    const api = mockApi();
    api.handlers['/api/auth/login'] = () => {
      api.handlers['/api/auth/me'] = () => new Response(null, { status: 503 });
      return new Response(null, { status: 204 });
    };
    const user = userEvent.setup();
    renderSession();
    await enterCredentials(user);
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByRole('heading', { name: 'Unable to connect' });
    expect(screen.queryByLabelText('Password')).toBeNull();
    api.handlers['/api/auth/me'] = () => Response.json(account);
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    await screen.findByText(account.email);
    expect(
      api.request.mock.calls.filter(([path]) => path === '/api/auth/login'),
    ).toHaveLength(1);
  });

  it('signs out with a fresh token, prevents duplicate requests, and permits signing in again', async () => {
    const api = mockApi(true);
    const pending = deferredResponse();
    api.handlers['/api/auth/logout'] = () => pending.promise;
    const user = userEvent.setup();
    renderSession();
    await user.click(await screen.findByRole('button', { name: 'Sign out' }));
    const button = await screen.findByRole('button', { name: 'Signing out…' });
    await user.click(button);
    expect(
      api.request.mock.calls.filter(([path]) => path === '/api/auth/logout'),
    ).toHaveLength(1);
    const logout = api.request.mock.calls.find(
      ([path]) => path === '/api/auth/logout',
    );
    expect(logout?.[1]?.method).toBe('POST');
    expect(logout?.[1]?.headers).toEqual({
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': 'csrf-1',
    });
    pending.resolve(new Response(null, { status: 204 }));
    const heading = await screen.findByRole('heading', {
      name: 'Welcome back',
    });
    expect(document.activeElement).toBe(heading);
    await enterCredentials(user);
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByText(account.email);
    const login = api.request.mock.calls.find(
      ([path]) => path === '/api/auth/login',
    );
    expect(login?.[1]?.headers).toEqual({
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': 'csrf-2',
    });
  });

  it('keeps a failed sign-out recoverable', async () => {
    const api = mockApi(true);
    const success = api.handlers['/api/auth/logout'];
    api.handlers['/api/auth/logout'] = () =>
      new Response(null, { status: 503 });
    const user = userEvent.setup();
    renderSession();
    await user.click(await screen.findByRole('button', { name: 'Sign out' }));
    expect((await screen.findByRole('alert')).textContent).toContain(
      'couldn’t sign you out',
    );
    expect(screen.getByText(account.email)).toBeTruthy();
    if (!success) throw new Error('Missing sign-out handler.');
    api.handlers['/api/auth/logout'] = success;
    await user.click(screen.getByRole('button', { name: 'Sign out' }));
    await screen.findByRole('button', { name: 'Sign in' });
  });

  it('cancels an older session response so it cannot restore the account after sign-out', async () => {
    const api = mockApi(true);
    const user = userEvent.setup();
    const { client } = renderSession();
    await screen.findByText(account.email);
    const pending = deferredResponse();
    api.handlers['/api/auth/me'] = () => pending.promise;
    let refresh: Promise<void> | undefined;
    act(() => {
      refresh = client.invalidateQueries(sessionQuery);
    });
    await waitFor(() => {
      expect(client.isFetching(sessionQuery)).toBe(1);
    });
    const signal = api.request.mock.calls
      .filter(([path]) => path === '/api/auth/me')
      .at(-1)?.[1]?.signal;
    await user.click(screen.getByRole('button', { name: 'Sign out' }));
    await screen.findByRole('button', { name: 'Sign in' });
    expect(signal?.aborted).toBe(true);
    await act(async () => {
      pending.resolve(Response.json(account));
      await refresh;
    });
    expect(screen.queryByText(account.email)).toBeNull();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeTruthy();
  });
});
