export class ApiError extends Error {
  readonly status: number;

  constructor(status: number) {
    super('The request failed.');
    this.status = status;
  }
}

async function request(
  path: string,
  options: RequestInit = {},
): Promise<Response> {
  const response = await fetch(path, {
    ...options,
    signal: options.signal
      ? AbortSignal.any([options.signal, AbortSignal.timeout(10_000)])
      : AbortSignal.timeout(10_000),
  });

  if (!response.ok) {
    throw new ApiError(response.status);
  }

  return response;
}

export async function getJson(
  path: string,
  signal?: AbortSignal,
): Promise<unknown> {
  const response = await request(path, {
    signal: signal ?? null,
    cache: 'no-store',
  });
  return response.json();
}

export async function postJson(
  path: string,
  body: unknown,
  headers: Record<string, string> = {},
): Promise<void> {
  await request(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...headers },
    body: JSON.stringify(body),
  });
}
