import { useEffect, type ReactNode } from 'react';
import ChatPanel from './components/chat/ChatPanel';
import { ChatPanelProvider, useChatPanel } from './components/chat/ChatPanelContext';
import { OrganizationContextProvider } from './hooks/useOrganizationContext';
import SystemDataProvider from './components/SystemRoute';
import CspOnboardingGuard from './features/csp-onboarding/CspOnboardingGuard';
import TenantOnboardingGuard from './features/onboarding/TenantWizard/TenantOnboardingGuard';
import OnboardingGate from './features/onboarding/OnboardingGate';
import IdleWarningModal from './features/auth/IdleWarningModal';
import RestoreUnsavedChangesPrompt from './features/auth/RestoreUnsavedChangesPrompt';
import ImpersonationBanner from './features/auth/ImpersonationBanner';
import { useIdleTimer } from './features/auth/useIdleTimer';
import { useLoginConfig } from './features/auth/LoginConfigContext';
import { useMe } from './features/auth/useMe';
import { useWorkspaceSession } from './features/workspaces/WorkspaceBoundary';
import WorkspaceHeader from './features/workspaces/WorkspaceHeader';

export default function ApplicationFrame({ children }: { children: ReactNode }) {
  const session = useWorkspaceSession();
  const content = <ChatPanelProvider><OrganizationContextProvider>
    <SystemDataProvider><FrameContent>{children}</FrameContent></SystemDataProvider>
  </OrganizationContextProvider></ChatPanelProvider>;
  // The legacy probes are retained only for genuinely unscoped servers.
  // An ordinary organization route must never probe provider onboarding.
  if (!session) return <CspOnboardingGuard><TenantOnboardingGuard>{content}</TenantOnboardingGuard></CspOnboardingGuard>;
  if (session.target.kind === 'csp') return <CspOnboardingGuard>{content}</CspOnboardingGuard>;
  return session.workspace.permissions.canManageOrganization
    ? <TenantOnboardingGuard>{content}</TenantOnboardingGuard> : content;
}

function FrameContent({ children }: { children: ReactNode }) {
  const { panelState, togglePanel, closePanel, setWidth } = useChatPanel();
  const { idleTimeoutMinutes } = useLoginConfig();
  const { data: identity } = useMe();
  const session = useWorkspaceSession();
  useIdleTimer(idleTimeoutMinutes);
  useEffect(() => {
    const shortcut = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key === 'C') {
        event.preventDefault();
        togglePanel();
      }
    };
    document.addEventListener('keydown', shortcut);
    return () => document.removeEventListener('keydown', shortcut);
  }, [togglePanel]);
  return (
    <div className="flex h-dvh flex-col overflow-hidden">
      {/* Authentication is server validated, including cookie-only sessions. */}
      <ImpersonationBanner />
      <IdleWarningModal />
      {identity?.oid && <RestoreUnsavedChangesPrompt oid={identity.oid} />}
      <WorkspaceHeader />
      <div className="min-h-0 flex-1 overflow-auto">{children}</div>
      <ChatPanel isOpen={panelState.isOpen} onClose={closePanel} width={panelState.width} onWidthChange={setWidth} />
      {!session && <OnboardingGate />}
    </div>
  );
}
