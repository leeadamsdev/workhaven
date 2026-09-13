import { ConfirmEmailPage } from './features/identity/ConfirmEmailPage';

export function App() {
  if (window.location.pathname === '/confirm-email') {
    return <ConfirmEmailPage />;
  }

  return (
    <main className="mx-auto max-w-5xl px-[clamp(1.5rem,5vw,4rem)] py-[clamp(3rem,12vh,8rem)]">
      <h1 className="text-[clamp(2.5rem,6vw,4rem)] leading-[1.15] font-semibold tracking-[-0.04em]">
        Workhaven
      </h1>
      <p className="mt-4 text-lg leading-normal text-muted-foreground">
        A place for your team’s work.
      </p>
    </main>
  );
}
