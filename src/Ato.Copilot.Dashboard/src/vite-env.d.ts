/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string;
  readonly VITE_POLL_INTERVAL_MS: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

// Feature 411: runtime mode override injected by nginx sub_filter.
// nginx sets window.__FORCE_SINGLE_TENANT__ = "true" for org-only deployments;
// it is empty string or absent for CSP/MultiTenant deployments.
interface Window {
  __FORCE_SINGLE_TENANT__?: string;
}
