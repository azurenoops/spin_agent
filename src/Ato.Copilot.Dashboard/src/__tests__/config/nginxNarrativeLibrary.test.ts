// @vitest-environment node
import { describe, expect, it } from 'vitest';
import configuration from '../../../nginx.conf?raw';

describe('Docker organization narrative library proxy', () => {
  it.each([
    ['root', 'location = /api/narrative-library'],
    ['children', 'location ^~ /api/narrative-library/'],
  ])('proxies the %s without changing authentication or workspace context', (_name, location) => {
    // Arrange
    const declaration = `${location} {`;

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

  it('allows the existing 5 MiB upload plus multipart overhead', () => {
    // Arrange
    const declaration = 'location ^~ /api/narrative-library/ {';

    // Act
    const start = configuration.indexOf(declaration);
    const end = configuration.indexOf('\n    }', start);
    const block = start < 0 || end < start ? '' : configuration.slice(start, end);
    const size = block.match(/client_max_body_size\s+(\d+)m;/)?.[1];

    // Assert
    expect(size).toBeDefined();
    expect(Number(size) * 1024 * 1024).toBeGreaterThanOrEqual(5 * 1024 * 1024 + 65536);
    expect(Number(size)).toBe(6);
  });
});
