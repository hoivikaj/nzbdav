/** Whether to warn that AUTHINFO credentials will be sent without TLS. */
export function shouldWarnCleartextCredentials(useSsl: boolean, user: string): boolean {
  return !useSsl && user.trim().length > 0;
}
