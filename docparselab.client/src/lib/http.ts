export function authHeaders(token: string | null): HeadersInit {
  return token ? { Authorization: `Bearer ${token}` } : {};
}

export async function readResponseError(response: Response, fallback: string): Promise<string> {
  const text = await response.text();
  return text.trim() ? text : fallback;
}
