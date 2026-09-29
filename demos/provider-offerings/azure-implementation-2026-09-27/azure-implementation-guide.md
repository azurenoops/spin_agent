# Azure IL5 shared services: Azure implementation guide

SYNTHETIC DEMONSTRATION ONLY. These are explicitly authored Azure implementation choices, not live resources, operational evidence, license entitlements or government authorizations. Validate regional and Azure Government feature availability before real deployment.

## Status and provenance

Implementation supplement dated September 27, 2026. These Azure product choices are explicitly authored for the provider demonstration; they were not extracted from the older generic source package. The original source edition 1.2 and its immutable publication history remain intact. This supplement does not itself publish a release or accept mission responsibilities.

Use this guide as the current product-mapping document alongside the baseline responsibility matrix, assessment summary and corrective plan. In the customer bundle, those earlier documents are under baseline/ so they are not mistaken for revised Azure-specific sources.

This enriches the Azure demonstration only. The application remains multi-cloud capable. Microsoft 365 stays a separate Microsoft SaaS offering, not an Azure subscription or resource allocation.

## Azure service components

### Azure Monitor Logs (Log Analytics)

SYNTHETIC DEMONSTRATION ONLY. Provider-operated Log Analytics workspace for enrolled Azure resource and workload audit events, diagnostic settings, queries and ingestion checks. The scenario target is 90 days of searchable events; actual table plans, configuration and licensing require validation. Mission teams still enable and review their application events.

### Azure Blob Storage

SYNTHETIC DEMONSTRATION ONLY. Provider-managed storage account and blob containers for exported audit records and versioned shared-configuration evidence. The scenario target is 365 days of archived audit evidence; retention, immutability, access controls and retrieval tests must be configured and validated. This is not a claim of automatic backup of every Azure resource.

### Azure Firewall

SYNTHETIC DEMONSTRATION ONLY. Azure Firewall and reviewed firewall policies inspect permitted shared-hub traffic and record rule changes and diagnostic events. Workload ingress and customer-owned network security groups remain mission responsibilities.

### Azure Virtual Network

SYNTHETIC DEMONSTRATION ONLY. Provider hub virtual network, subnets, peering and user-defined routes establish the documented shared routing boundary. Network paths and private service connectivity require explicit design, deployment and testing; naming this component does not grant cloud access.

### Microsoft Entra ID (Privileged Identity Management)

SYNTHETIC DEMONSTRATION ONLY. Microsoft Entra ID operator identities, MFA policies, role assignments and Privileged Identity Management activation procedures support time-limited provider administration. Required licenses and cloud-specific support must be verified. Mission account approval, access reviews and leaver processing remain customer-owned.

### Azure Policy

SYNTHETIC DEMONSTRATION ONLY. Provider policy definitions, initiatives, assignments and reviewed exemptions express shared configuration requirements and report noncompliance. Policy evaluation is not proof that every resource is correctly configured, and remediation is an explicitly approved operation. Versioned configuration evidence is retained separately in Azure Blob Storage.

### Azure Key Vault

SYNTHETIC DEMONSTRATION ONLY. Provider key vault retains encryption keys and controlled key-management configuration, including the customer-managed key selected for the Azure Backup design. Key rotation, recovery protection and least-privilege access require validation. Azure Key Vault manages keys; Azure Backup performs workload backup and restore.

### Microsoft Sentinel

SYNTHETIC DEMONSTRATION ONLY. Provider analytics rules and incident workspaces correlate enrolled security signals and retain triage records for the shared-service boundary. Connector coverage and Azure Government feature availability must be validated. Human responders retain authority for containment and mission escalation.

### Azure Logic Apps

SYNTHETIC DEMONSTRATION ONLY. Reviewed incident-notification workflows support the provider escalation process. Connector availability, permissions and delivery results require verification. Automated notifications do not make mission incident declarations, approve containment or issue authorization decisions.

### Microsoft Defender for Cloud

SYNTHETIC DEMONSTRATION ONLY. Provider security recommendations and supported vulnerability-assessment integrations identify shared-infrastructure exposure for review. Plans, assessed resource types, scan freshness and cloud availability must be verified. A recommendation or score is not a completed independent assessment.

### Azure Update Manager

SYNTHETIC DEMONSTRATION ONLY. Provider-managed machine update assessment, maintenance configuration and installation-result review support patching of eligible shared-infrastructure hosts. Supported machine types, operating systems and cloud availability require validation; mission application dependency patching remains customer-owned.

### Azure Backup (Recovery Services vault)

SYNTHETIC DEMONSTRATION ONLY. Azure Backup policies, protected items, recovery points and restore jobs protect supported provider-managed workloads in a Recovery Services vault. The design uses a customer-managed encryption key held in Azure Key Vault, with explicit vault identity permissions. Daily recovery-point monitoring and quarterly restore exercises target a 24-hour RPO and eight-hour RTO; targets are not verified results.

## Capability implementation and unchanged customer duties

### Audit collection

SYNTHETIC DEMONSTRATION ONLY. Collect enrolled Azure administrative and platform audit events using Azure Monitor Logs (Log Analytics), with reviewed exports to Azure Blob Storage. Scenario targets remain 90 days searchable and 365 days archived. Mission teams enable application audit sources and review mission-specific events. Azure product choices are authored in the September 27 implementation supplement.

Implementing components: Azure Monitor Logs (Log Analytics); Azure Blob Storage.

Control references: AU-2, AU-6, AU-11.

Provider duty: SYNTHETIC DEMONSTRATION ONLY. Operate collector health checks every 15 minutes; reconcile ingestion counts daily; protect archived events from routine operator modification. Evidence: AZ-E01: seven-day collector reconciliation and archive retrieval sample.

Shared duty: SYNTHETIC DEMONSTRATION ONLY. Agree event categories and triage escalation with each mission; investigate missing source intervals jointly within one business day. Evidence: AZ-E01: seven-day collector reconciliation and archive retrieval sample.

Customer duty: SYNTHETIC DEMONSTRATION ONLY. Enable workload audit sources, review mission-specific events each business day, and set longer retention when mission policy requires it. Evidence: AZ-E01: seven-day collector reconciliation and archive retrieval sample.

### Network protection

SYNTHETIC DEMONSTRATION ONLY. Azure Firewall and Azure Virtual Network implement the provider's reviewed shared-hub inspection and routing design for enrolled spokes. Mission teams remain responsible for application ingress, workload network security groups and endpoints outside the provider hub. Azure product choices are authored in the September 27 implementation supplement.

Implementing components: Azure Firewall; Azure Virtual Network.

Control references: SC-7.

Provider duty: SYNTHETIC DEMONSTRATION ONLY. Maintain deny-by-default hub rules, review exceptions monthly, and record firewall changes with rollback instructions. Evidence: AZ-E02: approved rule sample, route inventory and denied-flow test.

Shared duty: SYNTHETIC DEMONSTRATION ONLY. Approve traffic purpose and route changes jointly; exercise a denied-traffic test before connecting a new spoke. Evidence: AZ-E02: approved rule sample, route inventory and denied-flow test.

Customer duty: SYNTHETIC DEMONSTRATION ONLY. Manage application ingress, workload NSGs, DNS dependencies and endpoints outside the provider hub. Evidence: AZ-E02: approved rule sample, route inventory and denied-flow test.

### Privileged identity

SYNTHETIC DEMONSTRATION ONLY. Microsoft Entra ID and Privileged Identity Management support provider operator MFA, least-privilege role assignment and time-limited activation. Mission identities remain separately administered. Licensing, configured policy and enforcement require verification; no existing tenant entitlement is asserted.

Implementing components: Microsoft Entra ID (Privileged Identity Management).

Control references: IA-2, AC-2.

Provider duty: SYNTHETIC DEMONSTRATION ONLY. Require MFA for operators; review privileged membership monthly; remove provider leavers within four hours of confirmed notice. Evidence: AZ-E03: synthetic access review, activation record and leaver test.

Shared duty: SYNTHETIC DEMONSTRATION ONLY. Maintain named escalation contacts and reconcile emergency access use after every activation. Evidence: AZ-E03: synthetic access review, activation record and leaver test.

Customer duty: SYNTHETIC DEMONSTRATION ONLY. Approve mission accounts, enforce mission MFA, perform access reviews, and disable customer leavers. Evidence: AZ-E03: synthetic access review, activation record and leaver test.

### Configuration baselines

SYNTHETIC DEMONSTRATION ONLY. Azure Policy expresses reviewed shared-service configuration requirements and Azure Blob Storage retains versioned configuration evidence. Changes and exemptions require approval; mission application code and images are excluded. Evaluation and stored evidence do not establish successful remediation.

Implementing components: Azure Policy; Azure Blob Storage.

Control references: CM-2, CM-3.

Provider duty: SYNTHETIC DEMONSTRATION ONLY. Record baseline version, approver, test result and rollback plan; compare deployed shared settings weekly. Evidence: AZ-E04: baseline revision, drift review and approved change sample.

Shared duty: SYNTHETIC DEMONSTRATION ONLY. Assess mission impact before changing shared defaults; agree maintenance windows and exception expiration. Evidence: AZ-E04: baseline revision, drift review and approved change sample.

Customer duty: SYNTHETIC DEMONSTRATION ONLY. Maintain application baselines and approve workload changes; remediate customer-owned configuration drift. Evidence: AZ-E04: baseline revision, drift review and approved change sample.

### Encryption and key handling

SYNTHETIC DEMONSTRATION ONLY. Azure Key Vault supports provider-owned encryption-key custody, controlled access and rotation procedures. Covered Azure services enforce their own encryption and transport configuration; Key Vault is not itself a transport gateway or a workload backup service. Customer key and certificate duties remain unchanged.

Implementing components: Azure Key Vault.

Control references: SC-12, SC-13.

Provider duty: SYNTHETIC DEMONSTRATION ONLY. Restrict key administration, test rotation every 90 days, record custody, and enforce TLS 1.2 or higher on covered endpoints. Evidence: AZ-E05: synthetic rotation rehearsal and endpoint protocol inventory.

Shared duty: SYNTHETIC DEMONSTRATION ONLY. Document key ownership and coordinate rotation windows for dependent workloads; test trust-chain changes jointly. Evidence: AZ-E05: synthetic rotation rehearsal and endpoint protocol inventory.

Customer duty: SYNTHETIC DEMONSTRATION ONLY. Configure workload encryption and certificates; manage customer keys and classify data before using a covered endpoint. Evidence: AZ-E05: synthetic rotation rehearsal and endpoint protocol inventory.

### Incident coordination

SYNTHETIC DEMONSTRATION ONLY. Microsoft Sentinel incident records and approved Azure Logic Apps notification workflows support provider triage and coordination for the shared-service boundary. Human responders own escalation and containment approval; mission incident declarations and reporting remain customer-owned.

Implementing components: Microsoft Sentinel; Azure Logic Apps.

Control references: IR-4, IR-6.

Provider duty: SYNTHETIC DEMONSTRATION ONLY. Acknowledge severity-one tickets within 30 minutes in the scenario; preserve provider records and issue initial impact notices. Evidence: AZ-E06: tabletop timeline, escalation roster and notification template.

Shared duty: SYNTHETIC DEMONSTRATION ONLY. Exercise incident handoffs quarterly; coordinate containment approval where provider action affects mission service. Evidence: AZ-E06: tabletop timeline, escalation roster and notification template.

Customer duty: SYNTHETIC DEMONSTRATION ONLY. Own mission incident declarations, regulatory reporting, application containment and mission recovery decisions. Evidence: AZ-E06: tabletop timeline, escalation roster and notification template.

### Vulnerability management

SYNTHETIC DEMONSTRATION ONLY. Microsoft Defender for Cloud recommendations and supported vulnerability assessments inform triage; Azure Update Manager supports approved patching of eligible provider-managed machines. Follow-up verification is required. Customer applications, containers and mission hosts remain separately managed.

Implementing components: Microsoft Defender for Cloud; Azure Update Manager.

Control references: RA-5, SI-2.

Provider duty: SYNTHETIC DEMONSTRATION ONLY. Triage critical results within one business day, schedule fixes by risk, and verify remediation with a follow-up scan. Evidence: AZ-E07: synthetic scan sample, patch ticket and clean rescan.

Shared duty: SYNTHETIC DEMONSTRATION ONLY. Agree outage windows and compensating controls for shared infrastructure exceptions; review overdue items weekly. Evidence: AZ-E07: synthetic scan sample, patch ticket and clean rescan.

Customer duty: SYNTHETIC DEMONSTRATION ONLY. Scan and patch customer applications, containers and mission hosts; document mission risk acceptance separately. Evidence: AZ-E07: synthetic scan sample, patch ticket and clean rescan.

### Backup and recovery

SYNTHETIC DEMONSTRATION ONLY. Azure Backup (Recovery Services vault) provides backup policies, recovery points and restore operations for supported provider workloads. Azure Key Vault holds the customer-managed encryption key used by the documented backup design. Monitor daily recovery points and rehearse quarterly restores against the scenario's 24-hour RPO and eight-hour RTO. Key management is distinct from backup/restore, and mission recovery duties remain customer-owned.

Implementing components: Azure Backup (Recovery Services vault); Azure Key Vault.

Control references: CP-9, CP-10.

Provider duty: SYNTHETIC DEMONSTRATION ONLY. Monitor backup jobs daily, protect recovery copies from ordinary operator deletion, and record restore results quarterly. Evidence: AZ-E08: backup reconciliation and shared-configuration restore exercise.

Shared duty: SYNTHETIC DEMONSTRATION ONLY. Agree recovery sequence and validate shared dependencies during a joint recovery exercise. Evidence: AZ-E08: backup reconciliation and shared-configuration restore exercise.

Customer duty: SYNTHETIC DEMONSTRATION ONLY. Back up mission data and applications, define mission RTO/RPO, and validate business transactions after recovery. Evidence: AZ-E08: backup reconciliation and shared-configuration restore exercise.

## Backup and key management are separate

Azure Backup owns backup policy, protected items, recovery points and restore jobs. Azure Key Vault owns the customer-managed encryption key and its access/rotation/recovery configuration. The vault identity requires explicitly configured access to the selected key. A key vault is not a substitute for workload backups. Configuration exports in Azure Blob Storage are likewise not proof that Azure Backup protects every Azure resource.

The scenario's 24-hour RPO and eight-hour RTO remain design targets, not measured recovery results. Keep the existing finding and corrective milestones open until reviewed evidence supports closure.

## Verification before real deployment

Validate region and Azure Government service/feature availability, licensing, supported workload types, network access, identity permissions, encryption compatibility, retention and restore behavior. Neither the service names nor the IL5 demonstration title certify an authorization boundary. No real Azure tenant, subscription, resource or government decision is asserted.

## Public product references

Azure Backup customer-managed encryption keys: https://learn.microsoft.com/en-us/azure/backup/encryption-at-rest-with-cmk

Azure products by region: validate deployment availability separately: https://azure.microsoft.com/en-us/explore/global-infrastructure/products-by-region/
