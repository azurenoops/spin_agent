import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import ProviderOnboardingPrototype from './features/csp-onboarding/prototype/ProviderOnboardingPrototype';
import './index.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ProviderOnboardingPrototype />
  </StrictMode>,
);
