import type { AccountInfo, IPublicClientApplication } from '@azure/msal-browser';

type AccountSource = Pick<IPublicClientApplication, 'getAllAccounts'>
  & Partial<Pick<IPublicClientApplication, 'getActiveAccount'>>;

export function selectMsalAccount(source: AccountSource): AccountInfo | null {
  return source.getActiveAccount?.() ?? source.getAllAccounts()[0] ?? null;
}
