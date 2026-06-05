import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const apiProxyTarget = process.env.VITE_API_PROXY_TARGET || 'http://localhost:3002';
const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Epic #207 / Task #235 — FedRAMP build-time simulation exclusion.
//
// Rollup 4 (Vite 6) emits a named chunk for every dynamic import() specifier
// it encounters, even when the branch is statically dead (import.meta.env.DEV
// === false in production). The chunk filename string "SimulationPanel" then
// appears in the entry-bundle's module manifest and triggers the CI audit grep.
//
// Fix: in production builds, alias the SimulationPanel module path to a
// zero-export stub. Rollup sees only the stub — it never creates a
// SimulationPanel chunk, and the string never appears in the bundle.
//
// In development (NODE_ENV !== 'production') the alias is absent, so the real
// SimulationPanel is imported normally and the dev experience is unchanged.
const isProduction = process.env.NODE_ENV === 'production';
const simulationPanelAlias = isProduction
  ? [
      {
        find: /\/features\/auth\/SimulationPanel(\.tsx)?$/,
        replacement: path.resolve(
          __dirname,
          'src/features/auth/SimulationPanel.stub.ts',
        ),
      },
    ]
  : [];

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: simulationPanelAlias,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: apiProxyTarget,
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test-setup.ts',
    css: true,
    exclude: ['e2e/**', 'node_modules/**'],
  },
});
