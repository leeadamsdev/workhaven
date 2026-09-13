import { useMutation } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { ApiError, postJson } from '../../api/client';

function readConfirmation() {
  const parameters = new URLSearchParams(window.location.hash.slice(1));
  return { userId: parameters.get('userId'), token: parameters.get('token') };
}

export function ConfirmEmailPage() {
  const [confirmation, setConfirmation] = useState(readConfirmation);
  const navigate = useNavigate();

  useEffect(() => {
    void navigate('/confirm-email', { replace: true });
    document.title = 'Confirm your email · Workhaven';
  }, [navigate]);

  const mutation = useMutation({
    mutationFn: () => postJson('/api/auth/confirm-email', confirmation),
    networkMode: 'always',
    gcTime: 0,
  });
  const { reset } = mutation;

  useEffect(() => {
    function readNewLink() {
      setConfirmation(readConfirmation());
      reset();
      void navigate('/confirm-email', { replace: true });
    }

    window.addEventListener('hashchange', readNewLink);
    return () => {
      window.removeEventListener('hashchange', readNewLink);
    };
  }, [reset, navigate]);

  const invalidLink =
    !confirmation.userId ||
    !confirmation.token ||
    (mutation.error instanceof ApiError && mutation.error.status === 400);
  const outcome = useRef<HTMLParagraphElement>(null);

  useEffect(() => {
    if (mutation.isSuccess || invalidLink) outcome.current?.focus();
  }, [mutation.isSuccess, invalidLink]);

  return (
    <main className="mx-auto max-w-xl px-6 py-16 sm:py-24">
      <Link
        to="/"
        className="rounded text-sm font-semibold text-primary focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-primary"
      >
        Workhaven
      </Link>
      <h1 className="mt-8 text-3xl font-semibold tracking-tight">
        Confirm your email
      </h1>

      {mutation.isSuccess ? (
        <p
          ref={outcome}
          tabIndex={-1}
          role="status"
          className="mt-4 text-success"
        >
          Your email address is confirmed.
        </p>
      ) : invalidLink ? (
        <p
          ref={outcome}
          tabIndex={-1}
          role="alert"
          className="mt-4 text-danger"
        >
          This confirmation link is invalid or has expired. Open the latest
          confirmation email and try again.
        </p>
      ) : (
        <form
          className="mt-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (!mutation.isPending) mutation.mutate();
          }}
        >
          <p className="text-muted-foreground">
            Confirm this email address for your Workhaven account.
          </p>
          {mutation.isError && (
            <p role="alert" className="mt-4 text-danger">
              {mutation.error instanceof ApiError &&
              mutation.error.status === 429
                ? 'Too many attempts. Wait a minute before trying again.'
                : 'We couldn’t confirm your email right now. Please try again.'}
            </p>
          )}
          <button
            type="submit"
            aria-disabled={mutation.isPending}
            className="mt-6 cursor-pointer rounded-md bg-primary px-5 py-3 font-semibold text-white hover:bg-primary-hover focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-primary aria-disabled:cursor-wait aria-disabled:opacity-70"
          >
            {mutation.isPending
              ? 'Confirming…'
              : mutation.isError
                ? 'Try again'
                : 'Confirm email'}
          </button>
          <p role="status" className="sr-only">
            {mutation.isPending ? 'Confirming your email address.' : ''}
          </p>
        </form>
      )}
    </main>
  );
}
