import type { AccountInfo, IPublicClientApplication } from '@azure/msal-browser';

type AccountSource = Pick<IPublicClientApplication, 'getAllAccounts'>
  & Partial<Pick<IPublicClientApplication, 'getActiveAccount'>>;

export function selectMsalAccount(source: AccountSource): AccountInfo | null {
  return source.getActiveAccount?.() ?? source.getAllAccounts()[0] ?? null;
}

export function msalAccountKey(source: AccountSource | null): string {
  const account = source ? selectMsalAccount(source) : null;
  return account ? JSON.stringify([
    account.environment, account.homeAccountId, account.tenantId, account.localAccountId,
  ]) : '';
}
