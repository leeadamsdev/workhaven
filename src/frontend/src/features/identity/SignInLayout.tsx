import { Layers3, UsersRound, Zap } from 'lucide-react';
import type { ReactNode } from 'react';

export function SignInLayout({ children }: { children: ReactNode }) {
  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <div className="relative isolate mx-auto min-h-[calc(100svh-2rem)] max-w-7xl overflow-hidden rounded-2xl border border-primary/10 bg-linear-to-br from-white via-[#fafcff] to-primary-subtle/60 sm:min-h-[calc(100svh-3rem)] lg:min-h-[calc(100svh-4rem)]">
        <svg
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 -z-10 size-full text-primary"
          viewBox="0 0 1440 900"
          preserveAspectRatio="none"
          fill="none"
        >
          <path
            d="M0 635C280 735 470 867 850 669C1080 549 1200 279 1440 186V900H0Z"
            fill="currentColor"
            opacity=".025"
          />
          <path
            d="M0 766C260 650 440 955 920 733C1140 632 1280 724 1440 780V900H0Z"
            fill="currentColor"
            opacity=".035"
          />
        </svg>
        <header className="mx-auto flex max-w-5xl items-center gap-3 px-6 pt-7 sm:px-10 sm:pt-10 xl:px-0">
          <img
            src="/favicon.svg"
            alt=""
            width={34}
            height={34}
            className="rounded-lg shadow-sm"
          />
          <span className="text-xl font-semibold tracking-[-0.04em]">
            Workhaven
          </span>
        </header>

        <div className="mx-auto grid max-w-5xl items-center gap-16 px-5 py-8 sm:px-10 sm:py-14 lg:grid-cols-[1.1fr_1fr] lg:py-12 xl:px-0">
          <section
            aria-labelledby="sign-in-introduction"
            className="hidden lg:block"
          >
            <p className="text-[11px] font-semibold tracking-[0.22em] text-muted-foreground">
              BETTER WORK TOGETHER
            </p>
            <h2
              id="sign-in-introduction"
              className="mt-5 text-[clamp(2.5rem,3.7vw,3.25rem)] leading-[1.08] font-semibold tracking-[-0.045em]"
            >
              A place for
              <br />
              your team’s work.
            </h2>
            <p className="mt-5 max-w-sm text-lg leading-7 text-muted-foreground">
              Bring people and ideas together. Make room for the work that
              matters.
            </p>

            <ul className="mt-10 space-y-7">
              <li className="flex items-start gap-4">
                <span className="flex size-11 shrink-0 items-center justify-center rounded-full border border-primary/10 bg-primary-subtle text-primary">
                  <UsersRound size={21} aria-hidden="true" />
                </span>
                <div className="pt-0.5">
                  <h3 className="text-sm font-semibold">Work together</h3>
                  <p className="mt-1 text-sm leading-6 text-muted-foreground">
                    Good work starts with shared understanding.
                  </p>
                </div>
              </li>
              <li className="flex items-start gap-4">
                <span className="flex size-11 shrink-0 items-center justify-center rounded-full border border-success/10 bg-success-subtle text-success">
                  <Zap size={21} aria-hidden="true" />
                </span>
                <div className="pt-0.5">
                  <h3 className="text-sm font-semibold">Find your focus</h3>
                  <p className="mt-1 text-sm leading-6 text-muted-foreground">
                    Less noise. More room for meaningful work.
                  </p>
                </div>
              </li>
              <li className="flex items-start gap-4">
                <span className="flex size-11 shrink-0 items-center justify-center rounded-full border border-primary/10 bg-primary-subtle text-primary">
                  <Layers3 size={21} aria-hidden="true" />
                </span>
                <div className="pt-0.5">
                  <h3 className="text-sm font-semibold">Build what’s next</h3>
                  <p className="mt-1 text-sm leading-6 text-muted-foreground">
                    A shared home for your team’s next chapter.
                  </p>
                </div>
              </li>
            </ul>
          </section>

          <section
            aria-labelledby="session-heading"
            className="mx-auto w-full max-w-105 rounded-xl border border-border/70 bg-surface p-6 shadow-[0_16px_56px_-20px_rgba(49,86,211,0.18)] motion-safe:animate-sign-in-enter sm:p-9"
          >
            {children}
          </section>
        </div>

        <footer className="mx-auto max-w-5xl px-6 pb-7 text-center text-xs leading-5 text-muted-foreground sm:px-10 sm:pb-10 lg:text-left xl:px-0">
          A calmer way to begin your workday.
        </footer>
      </div>
    </main>
  );
}
