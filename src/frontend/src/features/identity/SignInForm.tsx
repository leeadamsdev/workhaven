import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  ArrowRight,
  CircleAlert,
  Eye,
  EyeOff,
  Info,
  LoaderCircle,
  LockKeyhole,
  Mail,
  UserRoundPlus,
} from 'lucide-react';
import { useRef, useState } from 'react';
import { ApiError } from '../../api/client';
import { sessionQuery, signIn } from './session';

function emailValidationMessage(input: HTMLInputElement) {
  if (!input.value.trim()) return 'Enter your email address.';
  return input.validity.typeMismatch
    ? 'Enter a valid email address, like you@company.com.'
    : '';
}

export function SignInForm() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [emailError, setEmailError] = useState('');
  const [passwordError, setPasswordError] = useState('');
  const [notice, setNotice] = useState('');
  const emailInput = useRef<HTMLInputElement>(null);
  const passwordInput = useRef<HTMLInputElement>(null);
  const queryClient = useQueryClient();
  const mutation = useMutation({
    mutationFn: () => signIn(email, password),
    networkMode: 'always',
    retry: false,
    gcTime: 0,
    onMutate: () => {
      setShowPassword(false);
      return queryClient.cancelQueries(sessionQuery);
    },
    onSuccess: () => {
      setPassword('');
    },
    onSettled: async () => {
      await queryClient.cancelQueries(sessionQuery);
      await queryClient.invalidateQueries(sessionQuery);
    },
  });

  const invalidCredentials =
    mutation.error instanceof ApiError && mutation.error.status === 401;
  const errorMessage = invalidCredentials
    ? 'Check your email and password, and make sure your email address is confirmed.'
    : mutation.error instanceof ApiError && mutation.error.status === 429
      ? 'Too many sign-in attempts. Wait a minute before trying again.'
      : mutation.error instanceof ApiError && mutation.error.status === 400
        ? 'Your session changed. Please try signing in again.'
        : 'We couldn’t sign you in right now. Please try again.';

  return (
    <form
      className="mt-7"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        if (mutation.isPending || !emailInput.current) return;
        const nextEmailError = emailValidationMessage(emailInput.current);
        const nextPasswordError = password ? '' : 'Enter your password.';
        setEmailError(nextEmailError);
        setPasswordError(nextPasswordError);
        setNotice('');
        mutation.reset();
        if (nextEmailError) emailInput.current.focus();
        else if (nextPasswordError) passwordInput.current?.focus();
        else mutation.mutate();
      }}
    >
      {mutation.isError && (
        <div
          id="sign-in-error"
          role="alert"
          className="mb-6 flex gap-3 rounded-lg border border-danger/15 bg-danger-subtle p-3.5 text-danger motion-safe:animate-sign-in-feedback"
        >
          <CircleAlert
            size={18}
            aria-hidden="true"
            className="mt-0.5 shrink-0"
          />
          <div className="text-sm leading-5">
            <p className="font-semibold">Unable to sign in</p>
            <p className="mt-1">{errorMessage}</p>
          </div>
        </div>
      )}
      <div>
        <label htmlFor="email" className="block text-sm font-medium">
          Email address
        </label>
        <div className="relative mt-2">
          <Mail
            size={18}
            aria-hidden="true"
            className="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-muted-foreground"
          />
          <input
            ref={emailInput}
            id="email"
            name="email"
            type="email"
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            required
            maxLength={256}
            placeholder="you@company.com"
            aria-invalid={Boolean(emailError) || invalidCredentials}
            aria-describedby={
              emailError
                ? 'email-error'
                : invalidCredentials
                  ? 'sign-in-error'
                  : undefined
            }
            value={email}
            readOnly={mutation.isPending}
            onChange={(event) => {
              setEmail(event.target.value);
              if (emailError)
                setEmailError(emailValidationMessage(event.target));
              if (mutation.isError) mutation.reset();
            }}
            className="h-12 w-full rounded-md border border-input bg-surface pr-3.5 pl-11 text-base shadow-xs transition-[border-color,box-shadow] placeholder:text-muted-foreground focus:border-primary focus:ring-4 focus:ring-primary/10 focus:outline-none aria-invalid:border-danger aria-invalid:focus:ring-danger/10 motion-reduce:transition-none"
          />
        </div>
        {emailError && (
          <p id="email-error" className="mt-2 text-sm text-danger">
            {emailError}
          </p>
        )}
      </div>
      <div className="mt-5">
        <label htmlFor="password" className="block text-sm font-medium">
          Password
        </label>
        <div className="relative mt-2">
          <LockKeyhole
            size={18}
            aria-hidden="true"
            className="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-muted-foreground"
          />
          <input
            ref={passwordInput}
            id="password"
            name="password"
            type={showPassword ? 'text' : 'password'}
            autoComplete="current-password"
            required
            maxLength={128}
            placeholder="Enter your password"
            aria-invalid={Boolean(passwordError) || invalidCredentials}
            aria-describedby={
              passwordError
                ? 'password-error'
                : invalidCredentials
                  ? 'sign-in-error'
                  : undefined
            }
            value={password}
            readOnly={mutation.isPending}
            onChange={(event) => {
              setPassword(event.target.value);
              if (passwordError && event.target.value) setPasswordError('');
              if (mutation.isError) mutation.reset();
            }}
            className="h-12 w-full rounded-md border border-input bg-surface pr-12 pl-11 text-base shadow-xs transition-[border-color,box-shadow] placeholder:text-muted-foreground focus:border-primary focus:ring-4 focus:ring-primary/10 focus:outline-none aria-invalid:border-danger aria-invalid:focus:ring-danger/10 motion-reduce:transition-none"
          />
          <button
            type="button"
            aria-label={showPassword ? 'Hide password' : 'Show password'}
            aria-disabled={mutation.isPending}
            onClick={() => {
              if (!mutation.isPending) setShowPassword(!showPassword);
            }}
            className="absolute inset-y-0 right-0.5 my-auto flex size-11 cursor-pointer items-center justify-center rounded text-muted-foreground transition-colors hover:bg-primary-subtle hover:text-primary focus-visible:outline-2 focus-visible:-outline-offset-4 focus-visible:outline-primary aria-disabled:cursor-wait motion-reduce:transition-none"
          >
            {showPassword ? (
              <EyeOff size={18} aria-hidden="true" />
            ) : (
              <Eye size={18} aria-hidden="true" />
            )}
          </button>
        </div>
        {passwordError && (
          <p id="password-error" className="mt-2 text-sm text-danger">
            {passwordError}
          </p>
        )}
      </div>
      <div className="mt-4 flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
        <label className="flex cursor-not-allowed items-center gap-2 text-xs text-muted-foreground">
          <input
            type="checkbox"
            disabled
            className="size-4 cursor-not-allowed accent-primary"
            aria-describedby="remember-me-hint"
          />
          <span>
            Remember me{' '}
            <span id="remember-me-hint" className="block text-[0.6875rem]">
              Coming soon
            </span>
          </span>
        </label>
        <button
          type="button"
          onClick={() => {
            setNotice('Password recovery is coming soon.');
          }}
          className="min-h-11 cursor-pointer rounded px-1 text-xs font-medium text-primary underline-offset-4 transition-colors hover:text-primary-hover hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary motion-reduce:transition-none"
        >
          Forgot password?
        </button>
      </div>
      <button
        type="submit"
        aria-disabled={mutation.isPending}
        className="group mt-4 flex min-h-12 w-full cursor-pointer items-center justify-center gap-2.5 rounded-md border border-primary bg-primary px-5 py-3 text-sm font-semibold text-white shadow-[0_3px_8px_-3px_rgba(49,86,211,0.45)] transition-[background-color,box-shadow,transform] hover:bg-primary-hover hover:shadow-[0_5px_14px_-4px_rgba(49,86,211,0.5)] focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-primary active:translate-y-px aria-disabled:translate-y-0 aria-disabled:cursor-wait aria-disabled:bg-primary-hover motion-reduce:transform-none motion-reduce:transition-none"
      >
        {mutation.isPending && (
          <LoaderCircle
            size={20}
            aria-hidden="true"
            className="motion-safe:animate-spin"
          />
        )}
        {mutation.isPending ? 'Signing in…' : 'Sign in'}
        {!mutation.isPending && (
          <ArrowRight
            size={17}
            aria-hidden="true"
            className="transition-transform group-hover:translate-x-0.5 motion-reduce:transform-none motion-reduce:transition-none"
          />
        )}
      </button>
      <p role="status" className="sr-only">
        {mutation.isPending ? 'Signing in to your account.' : ''}
      </p>
      <div className="mt-6 border-t border-border pt-6">
        <div className="flex items-center justify-center gap-3 rounded-lg bg-primary-subtle/60 px-4 py-3">
          <UserRoundPlus
            size={21}
            aria-hidden="true"
            className="shrink-0 text-primary"
          />
          <div className="text-sm">
            <p className="text-muted-foreground">New to Workhaven?</p>
            <button
              type="button"
              onClick={() => {
                setNotice('Registration is coming soon.');
              }}
              className="min-h-8 cursor-pointer rounded text-sm font-semibold text-primary underline decoration-primary/30 underline-offset-4 transition-colors hover:text-primary-hover hover:decoration-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary motion-reduce:transition-none"
            >
              Register now
            </button>
          </div>
        </div>
      </div>
      <div role="status">
        {notice && (
          <p className="mt-4 flex items-center gap-2 text-sm text-muted-foreground motion-safe:animate-sign-in-feedback">
            <Info size={16} aria-hidden="true" className="shrink-0" />
            {notice}
          </p>
        )}
      </div>
    </form>
  );
}
