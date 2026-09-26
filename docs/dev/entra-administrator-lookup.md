# Entra administrator lookup

Organization setup uses read-only Microsoft Graph user lookup. Selection populates the existing enrollment request; organization creation, membership and Administrator assignment still require the existing confirmation and authorization checks. A lookup is not proof of the selected person's current sign-in or a persisted identity attestation.

Connection configuration is server-owned under `EntraDirectory:Connections`. Each entry supplies `Id`, `Name`, `AdminDirectoryTenantId` (the CSP operator's authenticated tid), `DirectoryTenantId` (directory being searched), `Cloud` (`Public`, `Government`, or `DoD`), `ClientId`, and `ClientSecret`. Supply secrets via a deployment secret store or environment, never source control. The app requires Microsoft Graph application `User.Read.All` with tenant administrator consent. Create the app in the correct sovereign cloud. No Azure resources or directory permissions are created by this change.

Only authenticated CSP administrators in the provider workspace, outside support impersonation, may read connections or search. Connections belonging to another operator directory are inaccessible. Search accepts 2–100 characters, escapes OData literals, returns at most 20 entries and asks the user to refine when more exist. Endpoints are fixed to the selected cloud; redirects are disabled. No continuation URLs or tokens are returned. Unconfigured connections and upstream access/availability failures are explicit; search does not silently fall back to manual mode.

API: `GET /api/csp/directory/connections`; `GET /api/csp/directory/users?connectionId=...&query=...`. Responses use the workspace `data` envelope. Search is read-only and response caching is disabled.

References: [List users](https://learn.microsoft.com/en-us/graph/api/user-list?view=graph-rest-1.0), [national cloud deployments](https://learn.microsoft.com/en-us/graph/deployments).

## Deployment configuration

Example environment variable names (replace placeholders through your secret manager):

```text
EntraDirectory__Connections__0__Id=provider-dod
EntraDirectory__Connections__0__Name=Provider Entra directory
EntraDirectory__Connections__0__AdminDirectoryTenantId=<CSP administrator sign-in tenant GUID>
EntraDirectory__Connections__0__DirectoryTenantId=<directory to search GUID>
EntraDirectory__Connections__0__Cloud=DoD
EntraDirectory__Connections__0__ClientId=<app registration GUID>
EntraDirectory__Connections__0__ClientSecret=<secret reference resolved by deployment>
```

The client credential belongs to the target directory. Cross-directory lookup requires an explicit connection assigned to the operator's directory and consent in the target directory; typing another tenant ID in the browser cannot enable it. Configuration listing indicates credentials are present, not that consent or connectivity has been verified. The first search reports those failures. Rotate the secret through the deployment secret store.

## Local acceptance

1. Open Organizations → Add organization. Continue to Initial administrator.
2. Choose a configured directory, search by a name/email prefix, and select a result. Verify populated identity and review without any organization write.
3. Confirm creation only when the selected identity is correct. Verify normal membership and Administrator provisioning.
4. Exercise no results, missing connection, expired credentials/consent, and a changed query before a response arrives.
5. Verify manual entry, deferred enrollment, keyboard navigation, and phone layout.

Browser previews use synthetic identities and mocked directory responses. Live Entra consent and lookup must be tested in the deployment after configuration; no live tenant has been connected by this change.

Validation (September 24, 2026): nine directory service tests, three real-pipeline HTTP authorization tests, 29 Dashboard tests, Dashboard type checking, and eight desktop/mobile Chromium checks passed. Graph responses in automated tests are synthetic; live Entra access is not verified. Existing repository compiler warnings remain. The spec context generator ran with `SPECIFY_FEATURE=078-role-aware-workspaces`; its existing macOS grep option warnings remain outside this change.
