/**
 * ATO Copilot M365 Extension — Express Server (FR-037, FR-045, FR-046, FR-048, FR-050)
 *
 * Endpoints:
 * - POST /api/messages — Teams webhook handler with intent-based card routing
 * - GET /health — Health check
 * - GET /openapi.json — OpenAPI 3.0 spec
 * - GET /ai-plugin.json — M365 Copilot plugin descriptor
 *
 * Config validation on startup, graceful shutdown on SIGINT/SIGTERM.
 */

import express, { Request, Response } from "express";
import { ATOApiClient, McpResponse } from "./services/atoApiClient";
import {
  buildErrorCard,
  selectCard,
} from "./cards";
import { AuthDispatcher, type GetUserTokenFn, type TeamsSsoMode } from "./auth/dispatcher";
import { InMemoryIdentityStore } from "./auth/identityStore";
import { buildSignInCard, buildSignOutConfirmationCard } from "./auth/signInCard";

// --- Configuration validation (FR-048) ---

const ATO_API_URL = process.env.ATO_API_URL;
const ATO_API_KEY = process.env.ATO_API_KEY || "";
const PORT = parseInt(process.env.PORT || "3978", 10);
const BOT_ID = process.env.BOT_ID;
const BOT_PASSWORD = process.env.BOT_PASSWORD;

if (!ATO_API_URL) {
  console.error("FATAL: ATO_API_URL environment variable is required.");
  process.exit(1);
}

if (!BOT_ID) {
  console.warn("WARNING: BOT_ID not set — Bot Framework authentication disabled.");
}
if (!BOT_PASSWORD) {
  console.warn("WARNING: BOT_PASSWORD not set — Bot Framework authentication disabled.");
}

// --- Feature 051 Phase 9 — Teams SSO auth dispatcher (FR-021 / FR-022) ---
//
// `AUTH_TEAMS_SSO_MODE` defaults to "Disabled" so the existing bot
// behavior is preserved when the operator has not opted into the new
// auth flow. Setting it to "Required" or "Optional" enables the
// AuthDispatcher gate at the top of POST /api/messages. The server-side
// AuthOptionsValidator (T113) enforces the matching manifest contract.
const TEAMS_SSO_MODE = ((): TeamsSsoMode => {
  const raw = (process.env.AUTH_TEAMS_SSO_MODE ?? "Disabled").trim();
  if (raw === "Required" || raw === "Optional" || raw === "Disabled") return raw;
  console.warn(
    `WARNING: AUTH_TEAMS_SSO_MODE="${raw}" is invalid — defaulting to "Disabled".`,
  );
  return "Disabled";
})();
const TEAMS_SSO_CONNECTION_NAME = process.env.AUTH_TEAMS_SSO_CONNECTION_NAME ?? "";

// In the Express-only deployment we do not have a Bot Framework adapter
// to call `getUserToken` against. The default callback always returns
// null so Optional mode falls back to the OAuthPrompt sign-in card. When
// the bot is later hosted under a real BotFrameworkAdapter, wire it via
// `setGetUserToken(...)` from the adapter setup module.
let getUserToken: GetUserTokenFn = async () => null;
export function setGetUserToken(fn: GetUserTokenFn): void {
  getUserToken = fn;
}

// In-memory store is fine for dev / single-replica. Replace with the
// ConversationStateIdentityStore adapter for production multi-replica
// deployments (TODO in identityStore.ts).
const identityStore = new InMemoryIdentityStore();

const authDispatcher = new AuthDispatcher({
  mode: TEAMS_SSO_MODE,
  connectionName: TEAMS_SSO_CONNECTION_NAME,
  identityStore,
  getUserToken: (ctx, name) => getUserToken(ctx, name),
});

// Sign-out adapter callback. Default is no-op (no Bot Framework
// adapter); a real deployment wires `BotFrameworkAdapter.signOutUser`.
let signOutUser: (teamsTenantId: string, teamsUserId: string, connectionName: string) => Promise<void> =
  async () => {
    /* no-op in Express-only deployment */
  };
export function setSignOutUser(
  fn: (teamsTenantId: string, teamsUserId: string, connectionName: string) => Promise<void>,
): void {
  signOutUser = fn;
}

// --- Initialize services ---

const apiClient = new ATOApiClient(ATO_API_URL, ATO_API_KEY || undefined);
const app = express();
app.use(express.json());

// --- Intent-based card routing (FR-011) ---

function buildCardForResponse(mcpResponse: McpResponse): Record<string, unknown> {
  return selectCard(mcpResponse);
}

/**
 * Feature 051 T118 — match "sign out" / "signout" / "log out" / "logout"
 * as standalone user messages. Matched case-insensitively against the
 * trimmed text only; do NOT match when the phrase appears inside a
 * larger sentence (e.g. "how do I sign out my colleague?").
 */
function isSignOutIntent(messageText: string): boolean {
  const normalized = messageText.trim().toLowerCase().replace(/[!.?]+$/, "");
  return (
    normalized === "sign out" ||
    normalized === "signout" ||
    normalized === "log out" ||
    normalized === "logout"
  );
}

function buildAdaptiveCardResponse(card: Record<string, unknown>): Record<string, unknown> {
  return {
    type: "message",
    attachments: [
      {
        contentType: "application/vnd.microsoft.card.adaptive",
        content: card,
      },
    ],
  };
}

// --- Endpoints ---

// POST /api/messages (FR-037, FR-014b)
app.post("/api/messages", async (req: Request, res: Response) => {
  try {
    const { text, conversation, from, value, channelData } = req.body;

    // Handle Action.Submit payloads (FR-014b)
    const actionPayload = value as
      | { action?: string; message?: string; conversationId?: string; [key: string]: unknown }
      | undefined;

    // Determine the message to send — action payloads, quick-reply, or plain text
    let messageText = text as string | undefined;
    let action: string | undefined;
    let actionContext: Record<string, unknown> | undefined;

    if (actionPayload?.action) {
      action = actionPayload.action;
      actionContext = { ...actionPayload };
      delete actionContext.action;
      messageText = messageText || `Action: ${action}`;
    } else if (actionPayload?.message) {
      messageText = actionPayload.message;
    } else if (actionPayload?.quickReply) {
      messageText = actionPayload.quickReply as string;
    }

    if (!messageText) {
      const errorCard = buildErrorCard({
        errorMessage: "No message text provided.",
        helpText: "Please type a question or command to get started.",
      });
      res.json({
        type: "message",
        attachments: [
          {
            contentType: "application/vnd.microsoft.card.adaptive",
            content: errorCard,
          },
        ],
      });
      return;
    }

    // Feature 051 T117 / T118 — Teams SSO auth gate + sign-out intent.
    //
    // The gate runs ONLY when AUTH_TEAMS_SSO_MODE is "Required" or
    // "Optional" — Disabled (the default) preserves the legacy bot
    // behavior so existing deployments are unaffected (FR-021).
    //
    // teamsTenantId is from `activity.channelData.tenant.id` per
    // contracts/m365-bot.md § 3.1; teamsUserId is `activity.from.id`
    // (FR-022 — identity link is per-(tenant, user) pair).
    const teamsTenantId: string | undefined = channelData?.tenant?.id;
    const teamsUserId: string = from?.id ?? "unknown";

    if (TEAMS_SSO_MODE !== "Disabled") {
      // T118 — sign-out intent. Triggered before any auth check so the
      // user can always clear their identity even when their token has
      // expired. Matches contracts/m365-bot.md § 5.
      if (isSignOutIntent(messageText)) {
        if (teamsTenantId) {
          try {
            await signOutUser(teamsTenantId, teamsUserId, TEAMS_SSO_CONNECTION_NAME);
          } catch (err) {
            console.error("[Auth] adapter.signOutUser failed:", err);
          }
          await identityStore.delete(teamsTenantId, teamsUserId);
        }
        res.json(buildAdaptiveCardResponse(buildSignOutConfirmationCard()));
        return;
      }

      // T117 — auth dispatcher. On null token, render the OAuthPrompt
      // sign-in card per contracts/m365-bot.md § 3.3 and short-circuit.
      const token = await authDispatcher.resolveToken({ teamsTenantId, teamsUserId });
      if (token === null) {
        res.json(buildAdaptiveCardResponse(buildSignInCard()));
        return;
      }
    }

    const conversationId =
      actionPayload?.conversationId as string ||
      conversation?.id ||
      ATOApiClient.generateConversationId();
    const userId = from?.id || "unknown";
    const userName = from?.name;

    const startTime = Date.now();

    const mcpResponse = await apiClient.sendMessage(
      messageText,
      conversationId,
      userId,
      userName,
      action,
      actionContext
    );

    const elapsed = Date.now() - startTime;
    console.info(
      `[MCP] ${mcpResponse.intentType ?? "unknown"} | ${conversationId} | ${elapsed}ms | agent=${mcpResponse.agentUsed ?? "none"}`
    );

    const card = buildCardForResponse(mcpResponse);

    res.json({
      type: "message",
      attachments: [
        {
          contentType: "application/vnd.microsoft.card.adaptive",
          content: card,
        },
      ],
    });
  } catch (error) {
    console.error("Error processing message:", error);
    const errorCard = buildErrorCard({
      errorMessage: "An unexpected error occurred while processing your request.",
      helpText:
        "Please try again. If the problem persists, contact your administrator.",
    });
    res.json({
      type: "message",
      attachments: [
        {
          contentType: "application/vnd.microsoft.card.adaptive",
          content: errorCard,
        },
      ],
    });
  }
});

// GET /health (FR-045)
app.get("/health", (_req: Request, res: Response) => {
  res.json({
    name: "ATO Copilot M365 Extension",
    version: "1.0.0",
    timestamp: new Date().toISOString(),
  });
});

// GET /openapi.json (FR-046)
app.get("/openapi.json", (_req: Request, res: Response) => {
  res.json({
    openapi: "3.0.0",
    info: {
      title: "ATO Copilot M365 Extension",
      version: "1.0.0",
      description:
        "Compliance assessment and remediation for Azure Government via Microsoft Teams.",
    },
    servers: [{ url: `http://localhost:${PORT}` }],
    paths: {
      "/api/messages": {
        post: {
          operationId: "sendMessage",
          summary: "Send a message to ATO Copilot",
          requestBody: {
            required: true,
            content: {
              "application/json": {
                schema: {
                  type: "object",
                  properties: {
                    text: { type: "string", description: "Message text" },
                    conversation: {
                      type: "object",
                      properties: {
                        id: { type: "string" },
                      },
                    },
                    from: {
                      type: "object",
                      properties: {
                        id: { type: "string" },
                        name: { type: "string" },
                      },
                    },
                  },
                  required: ["text"],
                },
              },
            },
          },
          responses: {
            "200": {
              description: "Adaptive Card response",
              content: {
                "application/json": {
                  schema: {
                    type: "object",
                    properties: {
                      type: { type: "string" },
                      attachments: {
                        type: "array",
                        items: {
                          type: "object",
                          properties: {
                            contentType: { type: "string" },
                            content: { type: "object" },
                          },
                        },
                      },
                    },
                  },
                },
              },
            },
          },
        },
      },
      "/health": {
        get: {
          operationId: "healthCheck",
          summary: "Health check",
          responses: {
            "200": {
              description: "Health status",
            },
          },
        },
      },
    },
  });
});

// GET /ai-plugin.json (FR-046)
app.get("/ai-plugin.json", (_req: Request, res: Response) => {
  res.json({
    schema_version: "v1",
    name_for_human: "ATO Copilot",
    name_for_model: "ato_copilot",
    description_for_human:
      "Compliance assessment and remediation for Azure Government",
    description_for_model:
      "Use this plugin to run NIST 800-53 compliance assessments, generate remediation scripts, and manage ATO processes for Azure Government subscriptions.",
    api: {
      type: "openapi",
      url: "/openapi.json",
    },
    logo_url: "/icon.png",
  });
});

// --- Start server ---

const server = app.listen(PORT, () => {
  console.log(`ATO Copilot M365 Extension listening on port ${PORT}`);
  console.log(`Health: http://localhost:${PORT}/health`);
});

// --- Graceful shutdown (FR-050) ---

function gracefulShutdown(signal: string): void {
  console.log(`Received ${signal}, shutting down gracefully...`);
  server.close(() => {
    console.log("Server closed");
    process.exit(0);
  });
  setTimeout(() => process.exit(1), 10_000); // Force exit after 10s
}

process.on("SIGINT", () => gracefulShutdown("SIGINT"));
process.on("SIGTERM", () => gracefulShutdown("SIGTERM"));

export { app, server, buildCardForResponse, authDispatcher, identityStore };
