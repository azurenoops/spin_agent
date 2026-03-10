# GitHub App + Cloudflare Worker Setup Guide

This guide walks through setting up the Teamwork auto-installer — a GitHub App paired with a Cloudflare Worker that automatically pushes Teamwork framework files to every new repository you create.

For design rationale, see [ADR-006](decisions/006-github-app-worker-design.md).

---

## Prerequisites

- **GitHub account** with permission to create GitHub Apps
- **Cloudflare account** (free tier is sufficient)
- **Node.js 18+** installed
- **Wrangler CLI** — install with `npm install -g wrangler`
- **GitHub CLI** (`gh`) — [install](https://cli.github.com) and authenticate with `gh auth login`

---

## Step 1: Register the GitHub App

1. Go to **https://github.com/settings/apps/new**
2. Fill in the following fields:

   | Field | Value |
   |-------|-------|
   | **App name** | Choose a unique name (e.g., `teamwork-installer-<your-username>`) |
   | **Homepage URL** | `https://github.com/JoshLuedeman/teamwork` |
   | **Webhook URL** | `https://example.com/webhook` (placeholder — you'll update this in Step 5) |
   | **Webhook secret** | Generate one now and save it: `openssl rand -hex 32` |

3. Under **Permissions**, set:
   - **Repository permissions → Contents:** Read & Write
   - **Repository permissions → Metadata:** Read-only

4. Under **Subscribe to events**, check:
   - **Repositories**

5. Under **Where can this GitHub App be installed?**, select:
   - **Only on this account**

6. Click **Create GitHub App**.

---

## Step 2: Generate and Download Private Key

1. On the App settings page, scroll down to **Private keys**.
2. Click **Generate a private key**.
3. Save the downloaded `.pem` file securely — you'll need its contents in Step 6.
4. Note your **App ID** displayed at the top of the settings page.

---

## Step 3: Install the App on Your Account

1. In your App settings, go to the **Install App** tab.
2. Click **Install** next to your account.
3. Choose **All repositories** (recommended — ensures new repos are automatically covered).

> If you select specific repositories instead, the auto-installer will only work for those repos. New repos won't be covered unless you update the installation.

---

## Step 4: Deploy the Cloudflare Worker

```bash
cd workers/github-app
npm install
wrangler login    # if not already authenticated
wrangler deploy
```

Note the deployed URL in the output — it will look like:

```
https://teamwork-installer.<your-subdomain>.workers.dev
```

---

## Step 5: Update Webhook URL

1. Go back to your GitHub App settings page.
2. Update the **Webhook URL** to:
   ```
   https://teamwork-installer.<your-subdomain>.workers.dev/webhook
   ```
   Replace `<your-subdomain>` with your actual Cloudflare Workers subdomain from Step 4.
3. Click **Save changes**.

---

## Step 6: Configure Worker Secrets

Set the three required secrets using the Wrangler CLI. Each command will prompt you to enter the value:

```bash
# Your App ID (numeric, from the App settings page)
wrangler secret put GITHUB_APP_ID

# Your private key (paste the entire PEM file content, including -----BEGIN/END RSA PRIVATE KEY-----)
wrangler secret put GITHUB_APP_PRIVATE_KEY

# The webhook secret you generated in Step 1
wrangler secret put GITHUB_WEBHOOK_SECRET
```

> **Tip:** For the private key, you can pipe the file directly:
> ```bash
> cat path/to/your-app.private-key.pem | wrangler secret put GITHUB_APP_PRIVATE_KEY
> ```

---

## Step 7: Verify End-to-End

1. Create a new test repository on GitHub (e.g., `test-teamwork-auto-install`).
2. Wait up to 30 seconds for the webhook to fire and the Worker to process it.
3. Check the repository — it should have Teamwork framework files committed:
   - `.github/agents/`, `.github/skills/`, `docs/`, `Makefile`, etc.
   - `MEMORY.md`, `CHANGELOG.md`, and a starter `README.md`
4. Verify the commit author shows as your GitHub App.
5. Delete the test repository when done.

---

## Monitoring

### Real-time Worker logs

```bash
wrangler tail
```

Shows live output from `console.log` and `console.error` calls in the Worker.

### Webhook delivery history

1. Go to your GitHub App settings → **Advanced** → **Recent Deliveries**.
2. Each delivery shows the request payload, response status, and response body.
3. Click **Redeliver** on any failed delivery to retry it.

---

## Opting Out

Two mechanisms prevent auto-installation for specific repositories:

| Mechanism | How it works |
|-----------|-------------|
| **`.teamwork-skip` file** | Add a `.teamwork-skip` file to the repository. The Worker checks for this file before pushing framework files. Useful for template repos that include the marker, or to prevent re-installation on manual webhook redelivery. |
| **Forked repositories** | Forks are automatically skipped — they inherit conventions from their upstream repo. |

> **Note:** Since most repos are empty at creation time, the `.teamwork-skip` check is primarily useful for repos created from templates that include the marker file. To remove the framework after installation, delete the files and add `.teamwork-skip` to prevent future re-installation.

---

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| Webhook not received | Webhook URL is incorrect or App is not installed | Verify the URL in App settings matches the Worker URL with `/webhook` path. Confirm the App is installed on your account. |
| `401 Invalid signature` | Webhook secret mismatch | Ensure the secret set via `wrangler secret put GITHUB_WEBHOOK_SECRET` matches the secret in your GitHub App settings exactly. |
| `500 Internal server error` | Worker runtime error | Run `wrangler tail` and redeliver the webhook to see the error. Common causes: malformed private key, incorrect App ID, GitHub API rate limit. |
| Permission denied (403) | App lacks required permissions | Check App settings → Permissions. Contents must be **Read & Write**, Metadata must be **Read-only**. |
| Files not appearing (empty repo) | Ref creation failed | Check `wrangler tail` logs. The Worker handles empty repos by creating the initial ref — verify the App has write access to the repo. |
| Files not appearing (non-empty repo) | Ref update failed | The Worker uses `base_tree` to preserve existing files. Check logs for tree/commit creation errors. Run `teamwork install --force` to repair manually. |
| Wrong framework version | Source ref is outdated | The Worker fetches files from `main` at runtime. Check `wrangler.toml` — the `SOURCE_REF` var controls which branch is used. Redeploy after changing it: `wrangler deploy`. |

---

## Configuration Reference

### Secrets (via `wrangler secret put`)

| Secret | Purpose |
|--------|---------|
| `GITHUB_APP_ID` | GitHub App identifier, used as the `iss` claim in JWTs |
| `GITHUB_APP_PRIVATE_KEY` | RSA private key (PEM format) for signing JWTs |
| `GITHUB_WEBHOOK_SECRET` | Shared secret for HMAC-SHA256 webhook signature verification |

### Environment variables (in `wrangler.toml`)

| Variable | Purpose | Default |
|----------|---------|---------|
| `SOURCE_REPO_OWNER` | Owner of the Teamwork framework source repo | `JoshLuedeman` |
| `SOURCE_REPO_NAME` | Name of the source repo | `teamwork` |
| `SOURCE_REF` | Git ref to fetch framework files from | `main` |

To change the source (e.g., to test from a branch), edit `wrangler.toml` and run `wrangler deploy`.

---

## How It Works (Summary)

1. You create a new repository on GitHub.
2. GitHub sends a `repository.created` webhook to the Cloudflare Worker.
3. The Worker verifies the webhook signature (HMAC-SHA256).
4. The Worker authenticates as the GitHub App (JWT → installation access token).
5. The Worker checks skip conditions (fork, `.teamwork-skip`).
6. The Worker fetches the latest framework files from `JoshLuedeman/teamwork` via the Git Trees API.
7. The Worker pushes all framework files + starter templates as a single atomic commit to the new repo.

All framework files are fetched at runtime from the source repo — no redeployment needed when framework files are updated.
