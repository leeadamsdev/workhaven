import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { Navigate } from 'react-router';
import { sessionQuery } from './session';
import { SignInForm } from './SignInForm';
import { SignedInAccount } from './SignedInAccount';
import { SignInLayout } from './SignInLayout';

export function SessionPage({ view }: { view: 'account' | 'sign-in' }) {
  const session = useQuery(sessionQuery);
  const stage = session.isPending
    ? 'loading'
    : session.isError
      ? 'error'
      : session.data
        ? 'account'
        : 'sign-in';
  const title =
    stage === 'loading'
      ? 'Checking your session…'
      : stage === 'error'
        ? 'Unable to connect'
        : stage === 'account'
          ? 'You’re signed in'
          : 'Welcome back';
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    document.title =
      stage === 'sign-in' ? 'Sign in · Workhaven' : `${title} · Workhaven`;
    if (stage !== 'loading') heading.current?.focus();
  }, [stage, title, view]);

  if (session.isSuccess) {
    if (session.data && view === 'sign-in') return <Navigate to="/" replace />;
    if (!session.data && view === 'account')
      return <Navigate to="/sign-in" replace />;
  }

  const content = (
    <>
      <h1
        id="session-heading"
        ref={heading}
        tabIndex={-1}
        className="text-[1.75rem] leading-tight font-semibold tracking-[-0.035em] outline-none"
      >
        {title}
      </h1>
      {stage === 'loading' ? (
        <p role="status" className="mt-3 text-muted-foreground">
          Connecting to your account.
        </p>
      ) : stage === 'error' ? (
        <>
          <p role="alert" className="mt-3 text-muted-foreground">
            We couldn’t check your session. Please try again.
          </p>
          <button
            type="button"
            aria-disabled={session.isFetching}
            onClick={() => {
              if (!session.isFetching) void session.refetch();
            }}
            className="mt-6 cursor-pointer rounded-md bg-primary px-5 py-3 font-semibold text-white hover:bg-primary-hover focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-primary aria-disabled:cursor-wait aria-disabled:opacity-70"
          >
            {session.isFetching ? 'Checking…' : 'Try again'}
          </button>
        </>
      ) : session.data ? (
        <SignedInAccount user={session.data} />
      ) : (
        <>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">
            Sign in to your Workhaven account.
          </p>
          <SignInForm />
        </>
      )}
    </>
  );

  if (view === 'sign-in') return <SignInLayout>{content}</SignInLayout>;

  return (
    <main className="mx-auto max-w-md px-6 py-12 sm:py-20">
      <div className="mb-8 flex items-center gap-3">
        <img src="/favicon.svg" alt="" width={36} height={36} />
        <span className="text-xl font-semibold tracking-tight">Workhaven</span>
      </div>
      <section
        className="rounded-xl border border-border bg-surface p-6 sm:p-8"
        aria-labelledby="session-heading"
      >
        {content}
      </section>
    </main>
  );
}
