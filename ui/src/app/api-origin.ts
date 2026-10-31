export function isLocalWorkspace(hostname: string = globalThis.location?.hostname ?? 'localhost'): boolean {
  return ['localhost', '127.0.0.1', '[::1]', '::1'].includes(hostname);
}

/** Production uses its HTTPS origin and reverse proxy; local ports remain selectable. */
export function apiOrigin(port: number = 5391, location: Pick<Location, 'hostname' | 'origin'> = globalThis.location): string {
  return !location || isLocalWorkspace(location.hostname) ? `http://127.0.0.1:${port}` : location.origin;
}
