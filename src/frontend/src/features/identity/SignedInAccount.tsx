import { useMutation, useQueryClient } from '@tanstack/react-query';
import { sessionQuery, signOut } from './session';
import type { CurrentUser } from './session';

export function SignedInAccount({ user }: { user: CurrentUser }) {
  const queryClient = useQueryClient();
  const logout = useMutation({
    mutationFn: signOut,
    networkMode: 'always',
    retry: false,
    gcTime: 0,
    onMutate: () => queryClient.cancelQueries(sessionQuery),
    onSuccess: async () => {
      await queryClient.cancelQueries(sessionQuery);
      queryClient.setQueryData(sessionQuery.queryKey, null);
    },
    onError: () => queryClient.invalidateQueries(sessionQuery),
  });

  return (
    <>
      <p className="mt-3 text-sm text-muted-foreground">Signed in as</p>
      <p className="mt-1 font-medium wrap-anywhere">
        {user.email ?? 'Your Workhaven account'}
      </p>
      {logout.isError && (
        <p role="alert" className="mt-5 text-sm leading-6 text-danger">
          We couldn’t sign you out. Please try again.
        </p>
      )}
      <button
        type="button"
        aria-disabled={logout.isPending}
        onClick={() => {
          if (!logout.isPending) logout.mutate();
        }}
        className="mt-6 w-full cursor-pointer rounded-md border border-input px-5 py-3 font-semibold hover:bg-background focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-primary aria-disabled:cursor-wait aria-disabled:opacity-70"
      >
        {logout.isPending ? 'Signing out…' : 'Sign out'}
      </button>
      <p role="status" className="sr-only">
        {logout.isPending ? 'Signing out of your account.' : ''}
      </p>
    </>
  );
}
