/**
 * Feature 051 T117 — sign-in Adaptive Card for the OAuthPrompt fallback
 * path per contracts/m365-bot.md § 3.3. Rendered when
 * `AuthDispatcher.resolveToken()` returns `null`.
 *
 * Card shape: Adaptive Card v1.5 with a single `Action.Submit` carrying
 * `msteams.type=signin` data so Teams launches the OAuth consent URL in
 * the user's browser. The card body is intentionally short so it does
 * not push the conversation off-screen on mobile clients.
 */
export interface SignInCardOptions {
  /**
   * The OAuth consent URL the Teams client should open. Built by the
   * caller from the configured `connectionName` and the bot's
   * `serviceUrl`. When unset the card still renders but the
   * `msteams.value` is an empty string — useful for dev / preview.
   */
  oauthUrl?: string;
}

/**
 * Render the sign-in Adaptive Card. Matches contracts/m365-bot.md § 3.3
 * exactly. Returned as an `unknown`-keyed record to match the existing
 * card-builder convention in `extensions/m365/src/cards/`.
 */
export function buildSignInCard(options: SignInCardOptions = {}): Record<string, unknown> {
  return {
    type: "AdaptiveCard",
    version: "1.5",
    body: [
      {
        type: "TextBlock",
        text: "Sign in to ATO Copilot",
        weight: "Bolder",
        size: "Medium",
      },
      {
        type: "TextBlock",
        text: "Connect your Microsoft account so I can answer questions about your ATO packages.",
        wrap: true,
      },
    ],
    actions: [
      {
        type: "Action.Submit",
        title: "Sign In",
        data: {
          msteams: {
            type: "signin",
            value: options.oauthUrl ?? "",
          },
        },
      },
    ],
  };
}

/**
 * Render the sign-out confirmation card emitted after the bot processes
 * a "sign out" intent (T118 / contracts/m365-bot.md § 5).
 */
export function buildSignOutConfirmationCard(): Record<string, unknown> {
  return {
    type: "AdaptiveCard",
    version: "1.5",
    body: [
      {
        type: "TextBlock",
        text: "Signed out",
        weight: "Bolder",
        size: "Medium",
      },
      {
        type: "TextBlock",
        text: "Your Microsoft account is no longer linked to ATO Copilot in this Teams tenant. Mention me again to sign back in.",
        wrap: true,
      },
    ],
  };
}
