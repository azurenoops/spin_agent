/**
 * ATO Copilot M365 Extension — Config Validation Tests (T061-03)
 *
 * Tests validateEnv() fail-fast behavior for required and conditional vars.
 * Each test manipulates process.env directly and restores it in afterEach.
 *
 * Run: npm test  (mocha picks this up via .mocharc.json glob)
 */

import * as assert from "assert";
import { validateEnv, ConfigurationError } from "../src/config";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Minimum valid env required for validateEnv() to succeed. */
const VALID_ENV: Record<string, string> = {
  ATO_API_URL: "https://ato-api.example.com",
  BOT_ID: "a1b2c3d4-e5f6-4789-abcd-ef0123456789",
  BOT_PASSWORD: "s3cr3t!",
};

function withEnv(overrides: Record<string, string | undefined>, fn: () => void): void {
  const saved: Record<string, string | undefined> = {};
  // Clear all ATO-related keys first so tests start clean
  const relevantKeys = [
    "ATO_API_URL",
    "BOT_ID",
    "BOT_PASSWORD",
    "PORT",
    "AUTH_TEAMS_SSO_MODE",
    "AUTH_TEAMS_SSO_CONNECTION_NAME",
    "IDENTITY_STORE_BACKEND",
    "AZURE_STORAGE_CONNECTION_STRING",
    "REDIS_URL",
    "IDENTITY_STORE_ENCRYPTION_KEY",
    "SSE_TIMEOUT_MS",
  ];
  for (const key of relevantKeys) {
    saved[key] = process.env[key];
    delete process.env[key];
  }
  // Apply the overrides (undefined = keep deleted)
  for (const [k, v] of Object.entries(overrides)) {
    if (v === undefined) {
      delete process.env[k];
    } else {
      process.env[k] = v;
    }
  }
  try {
    fn();
  } finally {
    // Restore original env
    for (const key of relevantKeys) {
      if (saved[key] === undefined) {
        delete process.env[key];
      } else {
        process.env[key] = saved[key];
      }
    }
  }
}

/** Assert validateEnv() throws ConfigurationError mentioning at least one of the given substrings. */
function assertThrowsConfigError(env: Record<string, string | undefined>, ...mentionsAny: string[]): void {
  withEnv(env, () => {
    let thrown: unknown;
    try {
      validateEnv();
    } catch (e) {
      thrown = e;
    }
    assert.ok(thrown instanceof ConfigurationError, "Expected ConfigurationError to be thrown");
    const err = thrown as ConfigurationError;
    if (mentionsAny.length > 0) {
      const matched = mentionsAny.some((s) => err.message.includes(s));
      assert.ok(
        matched,
        "ConfigurationError message did not mention any of [" +
          mentionsAny.join(", ") +
          "]. Got:\n" +
          err.message,
      );
    }
  });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("validateEnv()", function () {
  it("succeeds with all required vars set to valid values", function () {
    withEnv(VALID_ENV, () => {
      assert.doesNotThrow(() => validateEnv(), "validateEnv() should not throw with valid env");
    });
  });

  // -------------------------------------------------------------------------
  // ATO_API_URL
  // -------------------------------------------------------------------------
  describe("ATO_API_URL", function () {
    it("throws when ATO_API_URL is missing", function () {
      assertThrowsConfigError({ ...VALID_ENV, ATO_API_URL: undefined }, "ATO_API_URL");
    });

    it("throws when ATO_API_URL has invalid scheme (ftp://)", function () {
      assertThrowsConfigError(
        { ...VALID_ENV, ATO_API_URL: "ftp://example.com" },
        "ATO_API_URL",
      );
    });

    it("throws when ATO_API_URL is not a URL at all", function () {
      assertThrowsConfigError(
        { ...VALID_ENV, ATO_API_URL: "not-a-url" },
        "ATO_API_URL",
      );
    });

    it("accepts http:// URL", function () {
      withEnv({ ...VALID_ENV, ATO_API_URL: "http://localhost:5000" }, () => {
        assert.doesNotThrow(() => validateEnv());
      });
    });
  });

  // -------------------------------------------------------------------------
  // BOT_ID
  // -------------------------------------------------------------------------
  describe("BOT_ID", function () {
    it("throws when BOT_ID is missing", function () {
      assertThrowsConfigError({ ...VALID_ENV, BOT_ID: undefined }, "BOT_ID");
    });

    it("throws when BOT_ID is not a UUID v4", function () {
      assertThrowsConfigError(
        { ...VALID_ENV, BOT_ID: "not-a-uuid" },
        "BOT_ID",
      );
    });

    it("throws when BOT_ID is UUID v1 (wrong version nibble)", function () {
      assertThrowsConfigError(
        { ...VALID_ENV, BOT_ID: "a1b2c3d4-e5f6-1789-abcd-ef0123456789" },
        "BOT_ID",
      );
    });
  });

  // -------------------------------------------------------------------------
  // BOT_PASSWORD
  // -------------------------------------------------------------------------
  describe("BOT_PASSWORD", function () {
    it("throws when BOT_PASSWORD is missing", function () {
      assertThrowsConfigError({ ...VALID_ENV, BOT_PASSWORD: undefined }, "BOT_PASSWORD");
    });

    it("throws when BOT_PASSWORD is empty string", function () {
      assertThrowsConfigError({ ...VALID_ENV, BOT_PASSWORD: "" }, "BOT_PASSWORD");
    });
  });

  // -------------------------------------------------------------------------
  // PORT
  // -------------------------------------------------------------------------
  describe("PORT", function () {
    it("accepts a valid integer port", function () {
      withEnv({ ...VALID_ENV, PORT: "8080" }, () => {
        assert.doesNotThrow(() => validateEnv());
      });
    });

    it("throws when PORT is non-integer", function () {
      assertThrowsConfigError({ ...VALID_ENV, PORT: "abc" }, "PORT");
    });

    it("throws when PORT is 0", function () {
      assertThrowsConfigError({ ...VALID_ENV, PORT: "0" }, "PORT");
    });

    it("throws when PORT is 65536", function () {
      assertThrowsConfigError({ ...VALID_ENV, PORT: "65536" }, "PORT");
    });
  });

  // -------------------------------------------------------------------------
  // AUTH_TEAMS_SSO_MODE
  // -------------------------------------------------------------------------
  describe("AUTH_TEAMS_SSO_MODE", function () {
    it("throws when value is not an allowed enum", function () {
      assertThrowsConfigError(
        { ...VALID_ENV, AUTH_TEAMS_SSO_MODE: "enabled" },
        "AUTH_TEAMS_SSO_MODE",
      );
    });

    it("throws when SSO_MODE=Required but connection name is missing", function () {
      assertThrowsConfigError(
        {
          ...VALID_ENV,
          AUTH_TEAMS_SSO_MODE: "Required",
          AUTH_TEAMS_SSO_CONNECTION_NAME: undefined,
        },
        "AUTH_TEAMS_SSO_CONNECTION_NAME",
      );
    });

    it("accepts Disabled without connection name", function () {
      withEnv({ ...VALID_ENV, AUTH_TEAMS_SSO_MODE: "Disabled" }, () => {
        assert.doesNotThrow(() => validateEnv());
      });
    });

    it("accepts Required with connection name set", function () {
      withEnv(
        {
          ...VALID_ENV,
          AUTH_TEAMS_SSO_MODE: "Required",
          AUTH_TEAMS_SSO_CONNECTION_NAME: "MyConnection",
        },
        () => {
          assert.doesNotThrow(() => validateEnv());
        },
      );
    });
  });

  // -------------------------------------------------------------------------
  // IDENTITY_STORE_BACKEND
  // -------------------------------------------------------------------------
  describe("IDENTITY_STORE_BACKEND", function () {
    it("throws when backend is invalid enum value", function () {
      assertThrowsConfigError(
        { ...VALID_ENV, IDENTITY_STORE_BACKEND: "postgres" },
        "IDENTITY_STORE_BACKEND",
      );
    });

    it("throws when azure-table set but AZURE_STORAGE_CONNECTION_STRING missing", function () {
      assertThrowsConfigError(
        {
          ...VALID_ENV,
          IDENTITY_STORE_BACKEND: "azure-table",
          IDENTITY_STORE_ENCRYPTION_KEY: Buffer.alloc(32).toString("base64"),
        },
        "AZURE_STORAGE_CONNECTION_STRING",
      );
    });

    it("throws when redis set but REDIS_URL missing", function () {
      assertThrowsConfigError(
        {
          ...VALID_ENV,
          IDENTITY_STORE_BACKEND: "redis",
          IDENTITY_STORE_ENCRYPTION_KEY: Buffer.alloc(32).toString("base64"),
        },
        "REDIS_URL",
      );
    });

    it("throws when REDIS_URL has wrong scheme", function () {
      assertThrowsConfigError(
        {
          ...VALID_ENV,
          IDENTITY_STORE_BACKEND: "redis",
          REDIS_URL: "http://localhost:6379",
          IDENTITY_STORE_ENCRYPTION_KEY: Buffer.alloc(32).toString("base64"),
        },
        "REDIS_URL",
      );
    });

    it("throws when non-memory backend but encryption key missing", function () {
      assertThrowsConfigError(
        {
          ...VALID_ENV,
          IDENTITY_STORE_BACKEND: "azure-table",
          AZURE_STORAGE_CONNECTION_STRING: "DefaultEndpointsProtocol=https;...",
        },
        "IDENTITY_STORE_ENCRYPTION_KEY",
      );
    });

    it("throws when encryption key decodes to wrong byte length", function () {
      assertThrowsConfigError(
        {
          ...VALID_ENV,
          IDENTITY_STORE_BACKEND: "azure-table",
          AZURE_STORAGE_CONNECTION_STRING: "DefaultEndpointsProtocol=https;...",
          IDENTITY_STORE_ENCRYPTION_KEY: Buffer.alloc(16).toString("base64"), // 16 bytes, not 32
        },
        "32 bytes",
      );
    });
  });

  // -------------------------------------------------------------------------
  // Multiple failures reported together
  // -------------------------------------------------------------------------
  it("reports multiple errors in a single ConfigurationError", function () {
    withEnv(
      {
        ATO_API_URL: undefined,
        BOT_ID: undefined,
        BOT_PASSWORD: undefined,
      },
      () => {
        let thrown: unknown;
        try {
          validateEnv();
        } catch (e) {
          thrown = e;
        }
        assert.ok(thrown instanceof ConfigurationError);
        const err = thrown as ConfigurationError;
        assert.ok(err.missingVars.length >= 3, "Expected at least 3 errors, got " + err.missingVars.length);
      },
    );
  });
});
