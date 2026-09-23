// @vitest-environment node
import { describe, expect, it } from 'vitest';
import configuration from '../../../nginx.conf?raw';

describe('Docker workspace operations proxy', () => {
  it('forwards the complete workspace API prefix without altering identity or paths', () => {
    // Arrange
    const declaration = 'location ^~ /api/workspaces/ {';

    // Act
    const start = configuration.indexOf(declaration);
    const end = configuration.indexOf('\n    }', start);
    const block = start < 0 || end < start ? '' : configuration.slice(start, end);

    // Assert
    expect(start).toBeGreaterThanOrEqual(0);
    expect(block).toContain('proxy_pass ${MCP_BASE_URL};');
    expect(block).toContain('proxy_http_version 1.1;');
    expect(block).toContain('proxy_set_header Host $proxy_host;');
    expect(block).not.toMatch(/proxy_set_header\s+(?:Authorization|Cookie|X-Workspace-\S+)/i);
    expect(block).not.toMatch(/\b(?:rewrite|try_files|limit_except)\b/);
  });
});
