import App from './App';

// Smoke test: verify the App module exports a valid React component.
// Full render is intentionally skipped here — App requires a live SignalR hub
// connection (/hubs/chat) that is unavailable in Jest's jsdom environment.
// Integration/E2E tests cover the full mounted lifecycle.
test('App module exports a function component', () => {
  expect(typeof App).toBe('function');
});
