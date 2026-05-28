import axios, { AxiosInstance, AxiosError } from "axios";
import * as vscode from "vscode";
import * as http from "http";
import * as https from "https";
import { URL } from "url";

/**
 * Tool execution record from MCP response (FR-002).
 */
export interface ToolExecution {
  toolName: string;
  success: boolean;
  executionTimeMs: number;
  resultSummary?: string;
}

/**
 * Structured error detail (FR-007, Constitution VII).
 */
export interface ErrorDetail {
  errorCode: string;
  message: string;
  suggestion?: string;
}

/**
 * Request payload for POST /mcp/chat
 */
export interface McpChatRequest {
  conversationId: string;
  message: string;
  conversationHistory: Array<{
    role: "user" | "assistant";
    content: string;
  }>;
  context: {
    source: "vscode-copilot";
    platform: "VSCode";
    targetAgent?: string;
    metadata: {
      routingHint?: string;
      fileName?: string;
      language?: string;
      analysisType?: string;
      /** Caller's home tenant id (Guid). Set by the status bar provider (T141). */
      tenantId?: string;
      /** Optional impersonated tenant id (Guid). CSP-Admin only. */
      impersonatedTenantId?: string;
    };
  };
  /** Action identifier for button-initiated requests (FR-014b) */
  action?: string;
  /** Contextual data for actions (FR-014b) */
  actionContext?: Record<string, unknown>;
}

/**
 * Enriched response from MCP Server (FR-001 through FR-007, FR-026).
 */
export interface McpChatResponse {
  success?: boolean;
  response: string;
  conversationId?: string;
  agentUsed?: string;
  intentType?: string;
  processingTimeMs?: number;
  templates?: Array<{
    name: string;
    type: string;
    content: string;
    language: string;
  }>;
  toolsExecuted?: ToolExecution[];
  errors?: ErrorDetail[];
  suggestions?: string[];
  /** Structured suggested actions with title and prompt (FR-023) */
  suggestedActions?: Array<{ title: string; prompt: string }>;
  requiresFollowUp?: boolean;
  followUpPrompt?: string;
  missingFields?: string[];
  data?: Record<string, unknown>;
}

/**
 * Health check response
 */
export interface HealthResponse {
  status: string;
  timestamp?: string;
}

/**
 * Mapped error for user-facing messages
 */
export interface McpError {
  message: string;
  actionButton: string;
  code: string;
}

/**
 * HTTP client service for communicating with the MCP Server.
 * Handles request construction, error mapping, and health checks.
 */
export class McpClient {
  private client: AxiosInstance;
  private outputChannel?: vscode.OutputChannel;
  private tenantContextProvider?: () =>
    | { tenantId: string; impersonatedTenantId?: string }
    | null;
  /**
   * Feature 051 (T110) — async bearer-token provider. When set, every
   * outbound request fetches a fresh token from the per-tenant
   * SecretStorage (and, on a miss, prompts the user to sign in via the
   * device-code flow). When unset, the legacy `apiKey` setting is used
   * so the extension still functions in pre-051 development setups.
   */
  private tokenProvider?: () => Promise<string | undefined>;

  constructor(outputChannel?: vscode.OutputChannel) {
    this.outputChannel = outputChannel;
    this.client = this.createClient();
  }

  /**
   * Register a callback that supplies the current tenant scope (home tenant
   * id and optional impersonated tenant id) on every outbound MCP request.
   * Used by the status bar (T141) to forward the user's current tenant
   * context to the server so MCP tools see the correct identity (FR-024).
   */
  public setTenantContextProvider(
    provider: () => { tenantId: string; impersonatedTenantId?: string } | null
  ): void {
    this.tenantContextProvider = provider;
  }

  /**
   * Feature 051 (T110) — register the per-request bearer-token provider.
   * Pass a closure that calls `getActiveTenantToken(context, statusBar)`.
   * Once set, the legacy `ato-copilot.apiKey` setting is ignored.
   */
  public setTokenProvider(provider: () => Promise<string | undefined>): void {
    this.tokenProvider = provider;
    this.client = this.createClient();
  }

  private attachTenantContext(request: McpChatRequest): McpChatRequest {
    const tenant = this.tenantContextProvider?.();
    if (!tenant) return request;
    const merged: McpChatRequest = {
      ...request,
      context: {
        ...request.context,
        metadata: {
          ...request.context.metadata,
          tenantId: tenant.tenantId,
          ...(tenant.impersonatedTenantId
            ? { impersonatedTenantId: tenant.impersonatedTenantId }
            : {}),
        },
      },
    };
    return merged;
  }

  private getConfig() {
    const config = vscode.workspace.getConfiguration("ato-copilot");
    return {
      apiUrl: config.get<string>("apiUrl", "http://localhost:3001"),
      apiKey: config.get<string>("apiKey", ""),
      timeout: config.get<number>("timeout", 30000),
      enableLogging: config.get<boolean>("enableLogging", false),
    };
  }

  private createClient(): AxiosInstance {
    const config = this.getConfig();
    const headers: Record<string, string> = {
      "Content-Type": "application/json",
    };

    // Legacy fallback — only used when the Feature 051 token provider has
    // NOT been wired up (e.g. early-boot health check, or developer-only
    // mode without device-code sign-in).
    if (!this.tokenProvider && config.apiKey) {
      headers["Authorization"] = `Bearer ${config.apiKey}`;
    }

    const instance = axios.create({
      baseURL: config.apiUrl,
      timeout: config.timeout,
      headers,
    });

    // Feature 051 (T110) — inject the per-request bearer when a provider
    // is registered. The provider triggers an interactive sign-in if no
    // token is cached, so this MUST NOT run during the silent health check
    // path; callers gate that themselves via `getActiveTenantToken`.
    if (this.tokenProvider) {
      const provider = this.tokenProvider;
      instance.interceptors.request.use(async (req) => {
        try {
          const token = await provider();
          if (token) {
            req.headers = req.headers ?? {};
            (req.headers as Record<string, string>)[
              "Authorization"
            ] = `Bearer ${token}`;
          }
        } catch (err) {
          // Surface to the channel but let the request go through without
          // the header so the server can return a structured 401 the
          // caller knows how to map.
          this.logError(
            `Token provider failed: ${err instanceof Error ? err.message : String(err)}`,
          );
        }
        return req;
      });
    }

    return instance;
  }

  /**
   * Refresh the HTTP client with current settings.
   * Call when settings may have changed.
   */
  public refreshClient(): void {
    this.client = this.createClient();
  }

  private log(message: string): void {
    const config = this.getConfig();
    if (config.enableLogging && this.outputChannel) {
      this.outputChannel.appendLine(
        `[${new Date().toISOString()}] ${message}`
      );
    }
  }

  /**
   * Log error-level messages (FR-028). Always logged regardless of enableLogging.
   */
  private logError(message: string): void {
    if (this.outputChannel) {
      this.outputChannel.appendLine(
        `[${new Date().toISOString()}] [ERROR] ${message}`
      );
    }
  }

  /**
   * Send a chat request to the MCP Server.
   * Logs info-level details (FR-027) and error-level errors (FR-028).
   */
  public async sendMessage(request: McpChatRequest): Promise<McpChatResponse> {
    const startTime = Date.now();
    this.log(
      `POST /mcp/chat — conversationId=${request.conversationId} messageLen=${request.message.length}`
    );

    const enriched = this.attachTenantContext(request);

    try {
      const response = await this.client.post<McpChatResponse>(
        "/mcp/chat",
        enriched
      );
      const elapsed = Date.now() - startTime;
      this.log(
        `Response: ${response.status} — intentType=${response.data.intentType ?? "n/a"} agentUsed=${response.data.agentUsed ?? "n/a"} responseTimeMs=${elapsed}`
      );

      // Log tool execution details (FR-029)
      if (response.data.toolsExecuted?.length) {
        const toolNames = response.data.toolsExecuted
          .map((t) => t.toolName)
          .join(", ");
        this.log(
          `Tools executed: [${toolNames}] — intentType=${response.data.intentType ?? "n/a"}`
        );
      }

      // Log errors at error level (FR-028)
      if (response.data.errors?.length) {
        for (const err of response.data.errors) {
          this.logError(
            `Error: ${err.errorCode} — ${err.message}${err.suggestion ? ` (suggestion: ${err.suggestion})` : ""} — conversationId=${request.conversationId}`
          );
        }
      }

      return response.data;
    } catch (error) {
      const elapsed = Date.now() - startTime;
      this.logError(
        `Request failed after ${elapsed}ms — conversationId=${request.conversationId}`
      );
      throw this.mapError(error);
    }
  }

  /**
   * Send a chat request to the MCP Server via SSE streaming endpoint.
   * Reports real-time progress steps and returns the final response.
   *
   * @param request The chat request payload
   * @param onProgress Callback invoked for each progress step from the server
   * @returns The final McpChatResponse
   */
  public async sendMessageWithProgress(
    request: McpChatRequest,
    onProgress?: (step: string) => void,
  ): Promise<McpChatResponse> {
    const startTime = Date.now();
    const config = this.getConfig();
    this.log(
      `POST /mcp/chat/stream — conversationId=${request.conversationId} messageLen=${request.message.length}`
    );

    // Feature 051 (T110) — resolve the bearer ahead of socket creation so
    // it's available synchronously inside the request callback. Falls back
    // to the legacy `apiKey` when no provider is registered.
    let bearer: string | undefined;
    if (this.tokenProvider) {
      try {
        bearer = await this.tokenProvider();
      } catch (err) {
        this.logError(
          `SSE token provider failed: ${err instanceof Error ? err.message : String(err)}`,
        );
      }
    } else if (config.apiKey) {
      bearer = config.apiKey;
    }

    return new Promise<McpChatResponse>((resolve, reject) => {
      const url = new URL(`${config.apiUrl}/mcp/chat/stream`);
      const isHttps = url.protocol === "https:";
      const transport = isHttps ? https : http;

      const body = JSON.stringify(this.attachTenantContext(request));
      // Use a generous timeout for streaming — AI + tool execution can
      // take well over 30 s.  The socket-level timeout resets with every
      // SSE chunk, so this only fires when the connection truly goes idle.
      const streamTimeout = Math.max(config.timeout * 4, 120_000);

      const reqOptions: http.RequestOptions = {
        hostname: url.hostname,
        port: url.port || (isHttps ? 443 : 80),
        path: url.pathname,
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Content-Length": Buffer.byteLength(body),
          ...(bearer ? { Authorization: `Bearer ${bearer}` } : {}),
        },
        timeout: streamTimeout,
      };

      const req = transport.request(reqOptions, (res) => {
        let buffer = "";

        res.on("data", (chunk: Buffer) => {
          buffer += chunk.toString();

          // Process complete SSE events (delimited by double newlines)
          const parts = buffer.split("\n\n");
          buffer = parts.pop() ?? ""; // Keep incomplete part in buffer

          for (const part of parts) {
            const dataLine = part
              .split("\n")
              .find((l) => l.startsWith("data: "));
            if (!dataLine) { continue; }

            const json = dataLine.slice(6).trim();
            if (!json) { continue; }

            try {
              const evt = JSON.parse(json);
              if (evt.type === "progress") {
                const step =
                  typeof evt.step === "string" ? evt.step : JSON.stringify(evt);
                onProgress?.(step);
                this.log(`Progress: ${step}`);
              } else if (evt.type === "result" && evt.data) {
                const elapsed = Date.now() - startTime;
                this.log(
                  `Stream completed in ${elapsed}ms — agentUsed=${evt.data.agentUsed ?? "n/a"}`
                );
                resolve(evt.data as McpChatResponse);
              } else if (evt.type === "error") {
                reject(
                  this.mapError(
                    new Error(evt.error ?? "Unknown streaming error")
                  )
                );
              }
            } catch {
              // Ignore malformed SSE events
            }
          }
        });

        res.on("end", () => {
          // Process any remaining data in buffer
          if (buffer.trim()) {
            const dataLine = buffer
              .split("\n")
              .find((l) => l.startsWith("data: "));
            if (dataLine) {
              try {
                const evt = JSON.parse(dataLine.slice(6).trim());
                if (evt.type === "result" && evt.data) {
                  resolve(evt.data as McpChatResponse);
                  return;
                }
              } catch {
                // Fall through
              }
            }
          }
          // If we never got a result event, reject
          reject(
            this.mapError(new Error("Stream ended without a result"))
          );
        });

        res.on("error", (err) => {
          reject(this.mapError(err));
        });
      });

      req.on("error", (err) => {
        reject(this.mapError(err));
      });

      req.on("timeout", () => {
        req.destroy();
        reject(
          this.mapError(new Error("Request timed out"))
        );
      });

      req.write(body);
      req.end();
    });
  }

  /**
   * Send an action-initiated request to the MCP Server (FR-014b).
   */
  public async sendAction(
    conversationId: string,
    action: string,
    actionContext: Record<string, unknown>,
    message?: string
  ): Promise<McpChatResponse> {
    const request: McpChatRequest = {
      conversationId,
      message: message ?? "",
      conversationHistory: [],
      context: {
        source: "vscode-copilot",
        platform: "VSCode",
        metadata: {},
      },
      action,
      actionContext,
    };
    return this.sendMessage(request);
  }

  /**
   * Check MCP Server health.
   */
  public async checkHealth(): Promise<HealthResponse> {
    this.log("GET /health");

    try {
      const response = await this.client.get<HealthResponse>("/health");
      this.log(`Health: ${response.status}`);
      return response.data;
    } catch (error) {
      throw this.mapError(error);
    }
  }

  /**
   * Map HTTP/network errors to user-facing messages per FR-033.
   */
  public mapError(error: unknown): McpError {
    const config = this.getConfig();

    if (axios.isAxiosError(error)) {
      const axiosError = error as AxiosError;
      const code = axiosError.code ?? "";

      if (code === "ECONNREFUSED") {
        return {
          message: `Cannot connect to ATO Copilot API at ${config.apiUrl}`,
          actionButton: "Configure Connection",
          code: "ECONNREFUSED",
        };
      }

      if (code === "ETIMEDOUT" || code === "ECONNABORTED") {
        return {
          message: "ATO Copilot API request timed out",
          actionButton: "Configure Connection",
          code: "ETIMEDOUT",
        };
      }

      const status = axiosError.response?.status;
      if (status === 401) {
        return {
          message: "ATO Copilot API authentication failed",
          actionButton: "Configure Connection",
          code: "HTTP_401",
        };
      }

      if (status === 500) {
        return {
          message: "ATO Copilot API encountered an error",
          actionButton: "Retry",
          code: "HTTP_500",
        };
      }

      return {
        message: `An unexpected error occurred: ${axiosError.message}`,
        actionButton: "Configure Connection",
        code: "UNKNOWN",
      };
    }

    if (error instanceof Error) {
      return {
        message: `An unexpected error occurred: ${error.message}`,
        actionButton: "Configure Connection",
        code: "UNKNOWN",
      };
    }

    return {
      message: "An unexpected error occurred",
      actionButton: "Configure Connection",
      code: "UNKNOWN",
    };
  }
}
