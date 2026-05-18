import type { TenantOnboardingProgress } from '../api';

/**
 * Common props for every step component in the tenant wizard.
 * `beforeSubmit` flips the parent's `busy` flag (disables Next button);
 * `onAdvance` accepts the latest progress payload from the server;
 * `onError` surfaces a server message to the wizard shell.
 */
export interface StepProps {
  busy: boolean;
  beforeSubmit: () => void;
  onAdvance: (next: TenantOnboardingProgress) => void;
  onError: (message: string) => void;
}
