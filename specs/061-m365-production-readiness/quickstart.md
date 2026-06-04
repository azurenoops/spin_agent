# Quickstart: M365 Teams Extension (Epic 061)

This guide covers running the M365 Teams bot locally for development and testing.

---

## Prerequisites

- Node.js 20+
- An ATO Copilot API instance (local or staging) — `ATO_API_URL`
- (Optional) Azure Bot Framework Emulator for end-to-end bot testing
- (Optional) ngrok for exposing local bot to Teams

---

## 1. Install Dependencies

```bash
cd extensions/m365
npm ci
```

---

## 2. Configure Environment

Copy the example env file and fill in required values:

```bash
cp .env.example .env
```

Minimum required variables:

```env
ATO_API_URL=http://localhost:5000   # or your staging API URL
BOT_ID=<your-azure-bot-app-id>      # from Azure Bot Service registration
BOT_PASSWORD=<your-azure-bot-password>
PORT=3978
```

If `BOT_ID` / `BOT_PASSWORD` are not available yet, the bot will log a WARNING and start in
degraded mode (suitable for card rendering tests, not end-to-end auth).

> **Note (Post-Epic 061 US3)**: Once T061-03 ships, missing `ATO_API_URL` will cause the bot
> to exit immediately with a clear error. Set it to any value for local testing.

---

## 3. Build and Run

```bash
npm run build
npm start
```

The bot listens on `http://localhost:3978` by default.

Health check:

```bash
curl http://localhost:3978/health
# → {"status":"ok"}
```

---

## 4. Run Tests

```bash
npm test
```

This runs all 17+ mocha tests. Output is printed to stdout. CI uses JUnit reporter; locally the
default spec reporter is used.

To run a single test file:

```bash
npx mocha --require ts-node/register test/dashboardCard.test.ts
```

---

## 5. Connect to Azure Bot Framework Emulator

1. Download [Bot Framework Emulator](https://github.com/microsoft/BotFramework-Emulator).
2. Open the emulator and connect to `http://localhost:3978/api/messages`.
3. If using `BOT_ID` / `BOT_PASSWORD`, enter them in the emulator connection settings.
4. Send a message; the bot should respond with an Adaptive Card or text reply.

---

## 6. Expose Locally to Teams (ngrok)

```bash
ngrok http 3978
```

Copy the HTTPS URL (e.g., `https://abc123.ngrok.io`) and set it as the messaging endpoint in
your Azure Bot Service resource:

```
https://abc123.ngrok.io/api/messages
```

Then install the Teams app package (`ato-copilot-m365.zip`) in Teams for development testing.

---

## 7. Identity Store (Post US2)

For local development, the in-memory backend is used by default:

```env
IDENTITY_STORE_BACKEND=memory
```

To test Azure Table Storage locally:

```bash
# Install Azurite (Azure Storage emulator)
npm install -g azurite
azurite --silent --location /tmp/azurite

# Set env vars
IDENTITY_STORE_BACKEND=azure-table
AZURE_STORAGE_CONNECTION_STRING="UseDevelopmentStorage=true"
IDENTITY_STORE_ENCRYPTION_KEY=$(openssl rand -base64 32)
```

---

## 8. Docker (Post US4)

```bash
# Build
docker build -t ato-copilot-m365 extensions/m365

# Run
docker run -p 3978:3978 \
  -e ATO_API_URL=http://host.docker.internal:5000 \
  -e BOT_ID=<your-bot-id> \
  -e BOT_PASSWORD=<your-bot-password> \
  ato-copilot-m365

# Health check
curl http://localhost:3978/health
```

---

## Common Issues

| Problem | Cause | Fix |
|---|---|---|
| `Error: ATO_API_URL is required` | Missing env var | Set `ATO_API_URL` in `.env` |
| Bot responds with 401 | Invalid `BOT_ID`/`BOT_PASSWORD` | Check Azure Bot registration |
| Teams shows "Something went wrong" | Messaging endpoint not HTTPS | Use ngrok HTTPS URL |
| SSE stream cuts off | Teams 30s proxy timeout | Expected; interim card is shown (US6) |
