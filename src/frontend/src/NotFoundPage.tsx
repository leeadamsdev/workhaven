import { useEffect, useRef } from 'react';
import { Link } from 'react-router';

export function NotFoundPage() {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    document.title = 'Page not found · Workhaven';
    heading.current?.focus();
  }, []);

  return (
    <main className="mx-auto max-w-xl px-6 py-16 sm:py-24">
      <h1
        ref={heading}
        tabIndex={-1}
        className="text-3xl font-semibold tracking-tight outline-none"
      >
        Page not found
      </h1>
      <p className="mt-4 text-muted-foreground">
        This page doesn’t exist. Check the address or return to Workhaven.
      </p>
      <Link
        to="/"
        className="mt-6 inline-block rounded font-semibold text-primary underline underline-offset-4 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-primary"
      >
        Return to Workhaven
      </Link>
    </main>
  );
}
