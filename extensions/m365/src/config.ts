/**
 * ATO Copilot M365 Extension — Startup Environment Validation (FR-048, T061-03)
 *
 * validateEnv() must be called before app.listen(). It collects ALL missing/invalid
 * required variables into an array, prints each one, then throws ConfigurationError.
 * Callers (index.ts) catch it and call process.exit(1).
 *
 * Spec: specs/061-m365-production-readiness/contracts/env-schema.md
 */

export class ConfigurationError extends Error {
  constructor(public readonly missingVars: string[]) {
    super(
      "Bot startup failed — " +
        missingVars.length +
        " missing or invalid environment variable(s):\n" +
        missingVars.map((v) => "  - " + v).join("\n"),
    );
    this.name = "ConfigurationError";
  }
}

/** Valid modes for AUTH_TEAMS_SSO_MODE. */
const SSO_MODES = ["Disabled", "Optional", "Required"] as const;
type SsoMode = (typeof SSO_MODES)[number];

/** Valid backends for IDENTITY_STORE_BACKEND. */
const STORE_BACKENDS = ["memory", "azure-table", "redis"] as const;

/**
 * Validates all required and conditional environment variables.
 *
 * Throws {@link ConfigurationError} with the full list of failures.
 * Call in index.ts before app.listen():
 *   try { validateEnv(); } catch (e) { console.error(e.message); process.exit(1); }
 */
export function validateEnv(): void {
  const errors: string[] = [];

  // -------------------------------------------------------------------------
  // Required: ATO_API_URL — must be a valid http/https URL
  // -------------------------------------------------------------------------
  const atoApiUrl = process.env.ATO_API_URL?.trim();
  if (!atoApiUrl) {
    errors.push("ATO_API_URL is required (http/https URL of the ATO Copilot MCP server)");
  } else {
    try {
      const url = new URL(atoApiUrl);
      if (url.protocol !== "http:" && url.protocol !== "https:") {
        errors.push('ATO_API_URL must use http or https scheme (got "' + url.protocol + '")');
      }
    } catch {
      errors.push('ATO_API_URL is not a valid URL: "' + atoApiUrl + '"');
    }
  }

  // -------------------------------------------------------------------------
  // Required: BOT_ID — must be a UUID v4
  // -------------------------------------------------------------------------
  const botId = process.env.BOT_ID?.trim();
  if (!botId) {
    errors.push("BOT_ID is required (Microsoft App ID from Azure AD app registration)");
  } else {
    const uuidV4Re =
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
    if (!uuidV4Re.test(botId)) {
      errors.push(
        'BOT_ID must be a valid UUID v4 (got "' + botId + '") — check Azure AD app registration',
      );
    }
  }

  // -------------------------------------------------------------------------
  // Required: BOT_PASSWORD — non-empty
  // -------------------------------------------------------------------------
  const botPassword = process.env.BOT_PASSWORD;
  if (!botPassword || botPassword.length === 0) {
    errors.push(
      "BOT_PASSWORD is required — set the Azure AD app client secret",
    );
  }

  // -------------------------------------------------------------------------
  // Optional: PORT — integer in 1–65535
  // -------------------------------------------------------------------------
  const rawPort = process.env.PORT;
  if (rawPort !== undefined) {
    const port = parseInt(rawPort, 10);
    if (isNaN(port) || port < 1 || port > 65535 || rawPort.trim() !== String(port)) {
      errors.push('PORT must be an integer in 1–65535 (got "' + rawPort + '")');
    }
  }

  // -------------------------------------------------------------------------
  // Optional: AUTH_TEAMS_SSO_MODE — enum
  // -------------------------------------------------------------------------
  const rawSsoMode = process.env.AUTH_TEAMS_SSO_MODE?.trim();
  let ssoMode: SsoMode = "Disabled";
  if (rawSsoMode !== undefined) {
    if (!(SSO_MODES as readonly string[]).includes(rawSsoMode)) {
      errors.push(
        "AUTH_TEAMS_SSO_MODE must be one of: " + SSO_MODES.join(", ") + ' (got "' + rawSsoMode + '")',
      );
    } else {
      ssoMode = rawSsoMode as SsoMode;
    }
  }

  // -------------------------------------------------------------------------
  // Conditional: AUTH_TEAMS_SSO_CONNECTION_NAME — required when SSO != Disabled
  // -------------------------------------------------------------------------
  if (ssoMode !== "Disabled" && !process.env.AUTH_TEAMS_SSO_CONNECTION_NAME?.trim()) {
    errors.push(
      'AUTH_TEAMS_SSO_CONNECTION_NAME is required when AUTH_TEAMS_SSO_MODE="' + ssoMode + '"',
    );
  }

  // -------------------------------------------------------------------------
  // Optional: IDENTITY_STORE_BACKEND — enum
  // -------------------------------------------------------------------------
  const rawBackend = process.env.IDENTITY_STORE_BACKEND?.trim();
  let backend = "memory";
  if (rawBackend !== undefined) {
    if (!(STORE_BACKENDS as readonly string[]).includes(rawBackend)) {
      errors.push(
        "IDENTITY_STORE_BACKEND must be one of: " + STORE_BACKENDS.join(", ") + ' (got "' + rawBackend + '")',
      );
    } else {
      backend = rawBackend;
    }
  }

  // -------------------------------------------------------------------------
  // Conditional: AZURE_STORAGE_CONNECTION_STRING — required for azure-table
  // -------------------------------------------------------------------------
  if (backend === "azure-table" && !process.env.AZURE_STORAGE_CONNECTION_STRING?.trim()) {
    errors.push(
      "AZURE_STORAGE_CONNECTION_STRING is required when IDENTITY_STORE_BACKEND=azure-table",
    );
  }

  // -------------------------------------------------------------------------
  // Conditional: REDIS_URL — required for redis
  // -------------------------------------------------------------------------
  if (backend === "redis") {
    const redisUrl = process.env.REDIS_URL?.trim();
    if (!redisUrl) {
      errors.push("REDIS_URL is required when IDENTITY_STORE_BACKEND=redis");
    } else {
      try {
        const url = new URL(redisUrl);
        if (url.protocol !== "redis:" && url.protocol !== "rediss:") {
          errors.push('REDIS_URL must use redis:// or rediss:// scheme (got "' + url.protocol + '")');
        }
      } catch {
        errors.push('REDIS_URL is not a valid URL: "' + redisUrl + '"');
      }
    }
  }

  // -------------------------------------------------------------------------
  // Conditional: IDENTITY_STORE_ENCRYPTION_KEY — required for non-memory backends
  // -------------------------------------------------------------------------
  if (backend !== "memory") {
    const encKey = process.env.IDENTITY_STORE_ENCRYPTION_KEY?.trim();
    if (!encKey) {
      errors.push(
        'IDENTITY_STORE_ENCRYPTION_KEY is required when IDENTITY_STORE_BACKEND="' + backend + '" (base64-encoded 32-byte key)',
      );
    } else {
      try {
        const bytes = Buffer.from(encKey, "base64");
        if (bytes.length !== 32) {
          errors.push(
            "IDENTITY_STORE_ENCRYPTION_KEY must decode to exactly 32 bytes (got " +
              bytes.length +
              " bytes) — generate with: openssl rand -base64 32",
          );
        }
      } catch {
        errors.push("IDENTITY_STORE_ENCRYPTION_KEY is not valid base64");
      }
    }
  }

  // -------------------------------------------------------------------------
  // Optional: SSE_TIMEOUT_MS — integer >= 5000
  // -------------------------------------------------------------------------
  const rawSseTimeout = process.env.SSE_TIMEOUT_MS;
  if (rawSseTimeout !== undefined) {
    const timeout = parseInt(rawSseTimeout, 10);
    if (isNaN(timeout) || timeout < 5000) {
      errors.push('SSE_TIMEOUT_MS must be an integer >= 5000 (got "' + rawSseTimeout + '")');
    }
  }

  // -------------------------------------------------------------------------
  // Throw if any errors collected
  // -------------------------------------------------------------------------
  if (errors.length > 0) {
    throw new ConfigurationError(errors);
  }
}
