import { Routes, Route, Navigate, useLocation } from './features/workspaces/workspaceNavigation';
import PortfolioRoute from './pages/PortfolioRoute';
import SystemsRoute from './pages/SystemsRoute';
import SystemsNewRoute from './pages/SystemsNewRoute';
import ComponentsRoute from './pages/ComponentsRoute';
import CapabilitiesRoute from './pages/CapabilitiesRoute';
import ControlsRoute from './pages/ControlsRoute';
import OverrideReviewPage from './pages/OverrideReviewPage';
import SystemDetail from './pages/SystemDetail';
import AuthorizationPage from './pages/AuthorizationPage';
import RolesManagementPage from './pages/RolesManagementPage';
import BoundaryManagement from './pages/BoundaryManagement';
import Documents from './pages/Documents';
import ConMon from './pages/ConMon';
import Assessments from './pages/Assessments';
import Remediation from './pages/Remediation';
import NarrativeWorkspace from './pages/NarrativeWorkspace';
import StandaloneNarrativeLibrary from './pages/StandaloneNarrativeLibrary';
import DeviationsPage from './pages/DeviationsPage';
import CapabilityCoverage from './pages/CapabilityCoverage';
import EvidenceRepository from './pages/EvidenceRepository';
import LegalRegulatory from './pages/LegalRegulatory';
import ComponentInventory from './pages/ComponentInventory';
import PoamManagement from './pages/PoamManagement';
import ControlInheritance from './pages/ControlInheritance';
import CapabilityResponsibilityReview from './pages/CapabilityResponsibilityReview';
import BaselineManagement from './pages/BaselineManagement';
import SystemProfile from './pages/SystemProfile';
import EmassStatusPage from './pages/EmassStatus';
import SystemLayout from './components/layout/SystemLayout';
import OrgSettingsPage from './pages/settings/OrgSettingsPage';
import OnboardingShell from './features/onboarding/OnboardingShell';
import TenantWizard from './features/onboarding/TenantWizard';
import CspWizard from './features/csp-onboarding/CspWizard';
import CspInheritedComponentsPage from './features/csp-inherited-components/CspInheritedComponentsPage';
import OrgCapabilityLibraryPage from './pages/OrgCapabilityLibraryPage';
import OrgCapabilityDetailPage from './pages/OrgCapabilityDetailPage';
import ImportedDocumentsView from './features/admin/imported-documents/ImportedDocumentsView';
import TemplatesAdminPage from './pages/TemplatesAdminPage';
import AzureSettingsPage from './pages/AzureSettingsPage';
import AuditLogPage from './pages/AuditLogPage';
import AdminMigrationPage from './pages/AdminMigrationPage';
import KnowledgeBaseManagementPage from './pages/KnowledgeBaseManagementPage';
import RequireAuth from './features/auth/RequireAuth';
import SystemAliasRedirect from './features/workspaces/SystemAliasRedirect';
import WorkspaceMembershipRoute from './features/workspaces/WorkspaceMembershipRoute';

export default function ApplicationRoutes() {
  return (
          <Routes>
          {/* All other routes require authentication; RequireAuth triggers
              loginRedirect with the deep-link as `state` when unauthenticated. */}
          <Route index element={<RequireAuth><PortfolioRoute /></RequireAuth>} />
          <Route path="systems" element={<RequireAuth><SystemsRoute /></RequireAuth>} />
          <Route path="narrative-library/*" element={<RequireAuth><StandaloneNarrativeLibrary /></RequireAuth>} />
          {/* fix(#522): /systems/new must be registered BEFORE /systems/:id so React
              Router does not match it as id="new". SystemsNewRoute redirects to
              /systems with state.openWizard=true, which opens the intake wizard. */}
          <Route path="systems/new" element={<RequireAuth><SystemsNewRoute /></RequireAuth>} />
          <Route path="systems/:id" element={<RequireAuth><SystemLayout /></RequireAuth>}>
            <Route index element={<SystemDetail />} />
            <Route path="boundaries" element={<BoundaryManagement />} />
            <Route path="legal" element={<LegalRegulatory />} />
            <Route path="documents" element={<Documents />} />
            <Route path="conmon" element={<ConMon />} />
            <Route path="emass/status" element={<EmassStatusPage />} />
            <Route path="narratives/*" element={<NarrativeWorkspace />} />
            <Route path="deviations" element={<DeviationsPage />} />
            <Route path="assessments" element={<Assessments />} />
            <Route path="remediation" element={<Remediation />} />
            <Route path="evidence" element={<EvidenceRepository />} />
            <Route path="components" element={<ComponentInventory />} />
            <Route path="poam" element={<PoamManagement />} />
            <Route path="capability-coverage" element={<CapabilityCoverage />} />
            <Route path="inheritance" element={<ControlInheritance />} />
            <Route path="inheritance/subscriptions" element={<CapabilityResponsibilityReview />} />
            {/* Alias: Oracle E2E tests and direct links use /control-inheritance → redirect to /inheritance */}
            <Route path="control-inheritance" element={<SystemAliasRedirect />} />
            <Route path="baseline" element={<BaselineManagement />} />
            {/* Alias: Oracle E2E tests and direct links use /categorization → redirect to /baseline */}
            <Route path="categorization" element={<SystemAliasRedirect />} />
            {/* Wave 9 #438 — URL slug aliases for blank-page fixes.
                Oracle QA sweep and sidebar nav use these slugs; the canonical
                routes use shorter/different names. Redirect to the real route.
                Uses SystemAliasRedirect (not Navigate with ../…) so absolute paths
                work correctly in React Router v7 on direct URL load (#464,#467,#462,#460). */}
            {/* Sidebar nav: capability-coverage, but direct links use /capabilities */}
            <Route path="capabilities" element={<SystemAliasRedirect />} />
            {/* Sidebar nav: profile/MissionAndPurpose, but direct links use /mission-purpose */}
            <Route path="mission-purpose" element={<SystemAliasRedirect />} />
            {/* Sidebar nav: profile/UsersAndAccess, but direct links use /users-access */}
            <Route path="users-access" element={<SystemAliasRedirect />} />
            {/* Sidebar nav: profile/EnvironmentAndDeployment, but direct links use /environment */}
            <Route path="environment" element={<SystemAliasRedirect />} />
            {/* Sidebar nav: profile/DataTypes, but direct links use /data-types */}
            <Route path="data-types" element={<SystemAliasRedirect />} />
            {/* Sidebar nav: profile/PortsProtocolsAndServices, but direct links use /ports-protocols */}
            <Route path="ports-protocols" element={<SystemAliasRedirect />} />
            {/* Sidebar nav: profile/LeveragedAuthorizations, but direct links use /leveraged-auth */}
            <Route path="leveraged-auth" element={<SystemAliasRedirect />} />
            {/* Sidebar nav: legal, but direct links use /legal-regulatory */}
            <Route path="legal-regulatory" element={<SystemAliasRedirect />} />
            <Route path="profile/:sectionType" element={<SystemProfile />} />
            {/* Epic #121 / Task #146 — Authorization phase page */}
            <Route path="authorize" element={<AuthorizationPage />} />
            {/* Epic #121 / Task #147 — Roles management page */}
            <Route path="roles" element={<RolesManagementPage />} />
          </Route>
          <Route path="capabilities" element={<RequireAuth><CapabilitiesRoute /></RequireAuth>} />
          <Route path="components" element={<RequireAuth><ComponentsRoute /></RequireAuth>} />
          <Route path="onboarding" element={<RequireAuth><OnboardingShell /></RequireAuth>} />
          <Route path="onboarding/tenant" element={<RequireAuth><TenantWizard /></RequireAuth>} />
          <Route path="onboarding/csp" element={<RequireAuth><CspWizard /></RequireAuth>} />
          {/* Feature 048 follow-up: the cross-tenant CSP dashboard now
              resolves at `/` via PortfolioRoute for CSP-Admins. The standalone
              `/csp-dashboard` route has been retired. */}
          <Route path="csp/inherited-components" element={<RequireAuth><CspInheritedComponentsPage /></RequireAuth>} />
          {/* UF-CSP-01: Org-user capability library (spec-070) */}
          <Route path="capability-library" element={<RequireAuth><OrgCapabilityLibraryPage /></RequireAuth>} />
          <Route path="capability-library/:capabilityId" element={<RequireAuth><OrgCapabilityDetailPage /></RequireAuth>} />
          <Route path="admin/imported-documents" element={<RequireAuth><ImportedDocumentsView /></RequireAuth>} />
          <Route path="admin/templates" element={<RequireAuth><TemplatesAdminPage /></RequireAuth>} />
          {/* Wave 6 GAP-007: retired /csp-dashboard redirect */}
          <Route path="csp-dashboard" element={<PortfolioAliasRedirect />} />
          {/* Wave 6 GAP-008: /portfolio named route */}
          <Route path="portfolio" element={<RequireAuth><PortfolioRoute /></RequireAuth>} />
          {/* Wave 6 GAP-016: Audit Log */}
          <Route path="audit" element={<RequireAuth><AuditLogPage /></RequireAuth>} />
          {/* Wave 6 GAP-017: Admin Migration */}
          <Route path="admin/migration" element={<RequireAuth><AdminMigrationPage /></RequireAuth>} />
          {/* Epic #134: NIST Knowledge Base */}
          <Route path="admin/knowledge-base" element={<RequireAuth><KnowledgeBaseManagementPage /></RequireAuth>} />
          <Route path="controls" element={<RequireAuth><ControlsRoute /></RequireAuth>} />
          {/* Epic #208 / Task #250 — Org settings page */}
          <Route path="settings/org" element={<RequireAuth><OrgSettingsPage /></RequireAuth>} />
          {/* Epic #215 / Task #293 — Azure subscription settings page */}
          <Route path="settings/azure-subscriptions" element={<RequireAuth><AzureSettingsPage /></RequireAuth>} />
          <Route path="controls/overrides" element={<RequireAuth><OverrideReviewPage /></RequireAuth>} />
          <Route path="settings/memberships" element={<WorkspaceMembershipRoute />} />
          <Route path="organizations/:organizationId/memberships" element={<WorkspaceMembershipRoute />} />
          <Route path="*" element={<main className="p-6"><h1>Page not found</h1></main>} />
        </Routes>
  );
}

function PortfolioAliasRedirect() {
  const location = useLocation();
  return <Navigate to={{ pathname: '/', search: location.search, hash: location.hash }} state={location.state} replace />;
}