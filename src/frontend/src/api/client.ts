export class ApiError extends Error {
  readonly status: number;

  constructor(status: number) {
    super('The request failed.');
    this.status = status;
  }
}

export async function postJson(path: string, body: unknown): Promise<void> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(10_000),
  });

  if (!response.ok) {
    throw new ApiError(response.status);
  }
}
