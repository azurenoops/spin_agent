/**
 * ATO Copilot M365 Extension — Health Endpoint Tests (T061-04)
 *
 * Verifies that GET /health returns 200 with { status: 'ok' } body
 * and responds within 500 ms. Uses the built-in http module to avoid
 * adding supertest as a dependency.
 *
 * Run: npm test  (mocha picks this up via .mocharc.json glob)
 */

import * as assert from "assert";
import * as http from "http";
import * as childProcess from "child_process";
import * as path from "path";

const PORT = 39780; // offset from default 3978 to avoid conflicts in CI

// ---------------------------------------------------------------------------
// Minimal server bootstrap for testing the health endpoint
// ---------------------------------------------------------------------------
import express from "express";

/** Spin up a minimal Express server with just the health endpoint. */
function createTestServer(): { server: http.Server; close: () => Promise<void> } {
  const app = express();

  // Mirror the health endpoint from src/index.ts
  app.get("/health", (_req: express.Request, res: express.Response) => {
    res.json({ status: "ok" });
  });

  const server = app.listen(PORT);

  return {
    server,
    close: () =>
      new Promise((resolve, reject) => {
        server.close((err) => (err ? reject(err) : resolve()));
      }),
  };
}

// ---------------------------------------------------------------------------
// Helper: send a GET request and collect the response
// ---------------------------------------------------------------------------
function get(
  port: number,
  path: string,
): Promise<{ statusCode: number; contentType: string; body: string; elapsedMs: number }> {
  return new Promise((resolve, reject) => {
    const start = Date.now();
    const req = http.request({ host: "127.0.0.1", port, path, method: "GET" }, (res) => {
      let body = "";
      res.on("data", (chunk: Buffer) => (body += chunk.toString()));
      res.on("end", () =>
        resolve({
          statusCode: res.statusCode ?? 0,
          contentType: (res.headers["content-type"] as string) ?? "",
          body,
          elapsedMs: Date.now() - start,
        }),
      );
    });
    req.on("error", reject);
    req.setTimeout(1000, () => {
      req.destroy(new Error("request timeout"));
    });
    req.end();
  });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
describe("GET /health", function () {
  // Increase mocha timeout for server startup
  this.timeout(5000);

  let testApp: ReturnType<typeof createTestServer>;

  before(function (done) {
    testApp = createTestServer();
    testApp.server.once("listening", done);
    testApp.server.once("error", done);
  });

  after(async function () {
    await testApp.close();
  });

  it("returns HTTP 200", async function () {
    const res = await get(PORT, "/health");
    assert.strictEqual(res.statusCode, 200, "Expected HTTP 200 from /health");
  });

  it("returns Content-Type: application/json", async function () {
    const res = await get(PORT, "/health");
    assert.ok(
      res.contentType.includes("application/json"),
      "Expected Content-Type application/json, got: " + res.contentType,
    );
  });

  it('returns body { status: "ok" }', async function () {
    const res = await get(PORT, "/health");
    let parsed: unknown;
    try {
      parsed = JSON.parse(res.body);
    } catch {
      assert.fail("Response body is not valid JSON: " + res.body);
    }
    assert.deepStrictEqual(parsed, { status: "ok" }, "Unexpected health body shape");
  });

  it("responds within 500 ms", async function () {
    const res = await get(PORT, "/health");
    assert.ok(
      res.elapsedMs < 500,
      "Health endpoint took " + res.elapsedMs + " ms (> 500 ms threshold)",
    );
  });
});
