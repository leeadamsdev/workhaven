import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { StrictMode } from 'react';
import { BrowserRouter } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../../App';

function renderConfirmation(
  fragment = '#userId=account-id&token=secret%2Btoken%3D',
) {
  window.history.replaceState(null, '', `/confirm-email${fragment}`);
  render(
    <StrictMode>
      <QueryClientProvider client={new QueryClient()}>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </QueryClientProvider>
    </StrictMode>,
  );
}

describe('email confirmation', () => {
  it('navigates from confirmation to sign-in through the router', async () => {
    const request = vi
      .fn<typeof fetch>()
      .mockResolvedValue(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', request);
    const user = userEvent.setup();
    renderConfirmation('');
    await user.click(screen.getByRole('link', { name: 'Workhaven' }));
    await screen.findByRole('button', { name: 'Sign in' });
    expect(window.location.pathname).toBe('/sign-in');
    expect(document.title).toBe('Sign in · Workhaven');
  });

  it('waits for an explicit keyboard action, posts the token, and focuses success', async () => {
    const request = vi
      .fn<typeof fetch>()
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', request);
    const user = userEvent.setup();
    renderConfirmation();

    expect(window.location.hash).toBe('');
    expect(request).not.toHaveBeenCalled();
    await user.tab();
    await user.tab();
    expect(document.activeElement).toBe(
      screen.getByRole('button', { name: 'Confirm email' }),
    );
    await user.keyboard('{Enter}');

    const success = await screen.findByText('Your email address is confirmed.');
    expect(document.activeElement).toBe(success);
    expect(request).toHaveBeenCalledTimes(1);
    const [path, options] = request.mock.calls[0] ?? [];
    expect(path).toBe('/api/auth/confirm-email');
    expect(options?.method).toBe('POST');
    expect(options?.headers).toEqual({ 'Content-Type': 'application/json' });
    expect(options?.body).toBe(
      JSON.stringify({ userId: 'account-id', token: 'secret+token=' }),
    );
    expect(options?.signal).toBeInstanceOf(AbortSignal);
  });

  it.each(['', '#userId=account-id', '#token=secret'])(
    'rejects incomplete links without a request: %s',
    (fragment) => {
      const request = vi.fn<typeof fetch>();
      vi.stubGlobal('fetch', request);
      renderConfirmation(fragment);

      expect(screen.getByRole('alert').textContent).toContain(
        'invalid or has expired',
      );
      expect(screen.queryByRole('button')).toBeNull();
      expect(request).not.toHaveBeenCalled();
    },
  );

  it('prevents duplicate submissions while pending', async () => {
    let complete: ((response: Response) => void) | undefined;
    const request = vi.fn<typeof fetch>().mockImplementation(
      () =>
        new Promise((resolve) => {
          complete = resolve;
        }),
    );
    vi.stubGlobal('fetch', request);
    const user = userEvent.setup();
    renderConfirmation();
    await user.click(screen.getByRole('button', { name: 'Confirm email' }));
    const pending = await screen.findByRole('button', { name: 'Confirming…' });
    expect(pending.getAttribute('aria-disabled')).toBe('true');
    await user.click(pending);
    expect(request).toHaveBeenCalledTimes(1);
    complete?.(new Response(null, { status: 204 }));
    await screen.findByText('Your email address is confirmed.');
  });

  it('shows an expired-link response and focuses the error', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn<typeof fetch>()
        .mockResolvedValue(new Response(null, { status: 400 })),
    );
    const user = userEvent.setup();
    renderConfirmation();
    await user.click(screen.getByRole('button', { name: 'Confirm email' }));

    const error = await screen.findByRole('alert');
    expect(error.textContent).toContain('invalid or has expired');
    expect(document.activeElement).toBe(error);
    expect(screen.queryByRole('button')).toBeNull();
  });

  it.each([503, 429])('allows retry after HTTP %s', async (status) => {
    const request = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(new Response(null, { status }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', request);
    const user = userEvent.setup();
    renderConfirmation();
    await user.click(screen.getByRole('button', { name: 'Confirm email' }));
    const error = await screen.findByRole('alert');
    expect(error.textContent).toContain(
      status === 429 ? 'Wait a minute' : 'Please try again',
    );
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    await screen.findByText('Your email address is confirmed.');
    expect(request).toHaveBeenCalledTimes(2);
  });

  it('shows a recoverable connection error without exposing exception details', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn<typeof fetch>()
        .mockRejectedValue(new TypeError('private failure details')),
    );
    const user = userEvent.setup();
    renderConfirmation();
    await user.click(screen.getByRole('button', { name: 'Confirm email' }));
    await waitFor(() => {
      expect(screen.getByRole('alert').textContent).toContain(
        'Please try again',
      );
    });
    expect(document.body.textContent).not.toContain('private failure details');
  });

  it('uses a newly opened confirmation link in the same tab', async () => {
    const request = vi
      .fn<typeof fetch>()
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', request);
    const user = userEvent.setup();
    renderConfirmation();
    await user.click(screen.getByRole('button', { name: 'Confirm email' }));
    await screen.findByText('Your email address is confirmed.');
    window.location.hash = '#userId=another-account&token=another-token';
    await user.click(
      await screen.findByRole('button', { name: 'Confirm email' }),
    );
    await screen.findByText('Your email address is confirmed.');
    expect(request.mock.calls[1]?.[1]?.body).toBe(
      JSON.stringify({ userId: 'another-account', token: 'another-token' }),
    );
    expect(window.location.hash).toBe('');
  });
});
