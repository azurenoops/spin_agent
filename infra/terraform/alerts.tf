# =============================================================================
# alerts.tf — BUG-21 Azure Monitor Alert Rules + Action Groups
#
# Implements the 4 scheduled-query alert rules and 2 action groups defined in
# Thor artifact e04c38facf234170 (infrastructure_reliability-20260815-152724.md).
#
# Work item: 91d6e43d5d284a49
# Issue:     https://github.com/azurenoops/spin_agent/issues/694 (BUG-21)
#
# Failure modes covered:
#   FM-1  BUG21-LogFatal-BootFail        — Log.Fatal from ValidateDevAuthBypassConfig
#   FM-2  BUG21-BypassActive-Production  — [BUG-21] Dev auth bypass ACTIVE in prod logs
#   FM-3  BUG21-AuthFailureSpike         — >10 HTTP 401/403 in a 5-minute window
#   FM-4  BUG21-ContainerRestartStorm    — >3 container restarts in 10 minutes
#
# KQL variant:
#   Application Insights is NOT provisioned in this Terraform root module.
#   Rules 1 and 2 therefore use the ContainerAppConsoleLogs_CL variant of their
#   queries. Switch to AppTraces variants once App Insights is wired in.
#
# Deploy order (each via -target to allow per-step approval):
#   1. azurerm_monitor_action_group.security_critical
#   2. azurerm_monitor_action_group.security_warning
#   3. azurerm_monitor_scheduled_query_rules_alert_v2.bug21_bypass_active
#   4. azurerm_monitor_scheduled_query_rules_alert_v2.bug21_log_fatal
#   5. azurerm_monitor_scheduled_query_rules_alert_v2.bug21_auth_failure_spike
#   6. azurerm_monitor_scheduled_query_rules_alert_v2.bug21_container_restart_storm
#
# Cost estimate: ~$13/month (4 × $0.10/rule/day + SMS notifications).
# Rollback:      terraform destroy -target=<resource>  OR  disable in Portal.
# =============================================================================

# ---------------------------------------------------------------------------
# Action Group — ag-security-critical
# Targets: PagerDuty webhook, on-call SMS, team email, Teams channel webhook
# Used by: BUG21-LogFatal-BootFail, BUG21-BypassActive-Production
# ---------------------------------------------------------------------------
locals {
  # Build webhook receiver maps so that dynamic for_each iterates over a
  # map(object) whose value attributes match the block's schema exactly.
  # This satisfies azurerm provider schema validation on Terraform 1.7.x.
  security_critical_webhook_receivers = merge(
    var.pagerduty_webhook_url != "" ? {
      pagerduty = {
        name                    = "pagerduty"
        service_uri             = var.pagerduty_webhook_url
        use_common_alert_schema = true
      }
    } : {},
    var.teams_webhook_url != "" ? {
      teams = {
        name                    = "teams"
        service_uri             = var.teams_webhook_url
        use_common_alert_schema = true
      }
    } : {}
  )

  security_warning_webhook_receivers = merge(
    var.teams_webhook_url != "" ? {
      teams = {
        name                    = "teams"
        service_uri             = var.teams_webhook_url
        use_common_alert_schema = true
      }
    } : {}
  )

  # email_receivers: map(string) keyed by receiver name, value = address.
  # Using map(string) satisfies Terraform 1.7.x dynamic for_each validation.
  security_critical_email_receivers = var.alert_email_address != "" ? {
    "team-email" = var.alert_email_address
  } : {}

  security_warning_email_receivers = var.alert_email_address != "" ? {
    "team-email" = var.alert_email_address
  } : {}
}

resource "azurerm_monitor_action_group" "security_critical" {
  name                = "ag-security-critical"
  resource_group_name = module.rg.resource_group_name
  short_name          = "sec-crit"
  enabled             = true

  tags = var.tags

  # PagerDuty webhook + Teams webhook — iterate over typed objects
  dynamic "webhook_receiver" {
    for_each = local.security_critical_webhook_receivers
    content {
      name                    = webhook_receiver.value.name
      service_uri             = webhook_receiver.value.service_uri
      use_common_alert_schema = webhook_receiver.value.use_common_alert_schema
    }
  }

  # Team email distribution list
  dynamic "email_receiver" {
    for_each = local.security_critical_email_receivers
    content {
      name                    = email_receiver.key
      email_address           = email_receiver.value
      use_common_alert_schema = true
    }
  }

  depends_on = [module.rg]
}

# ---------------------------------------------------------------------------
# Action Group — ag-security-warning
# Targets: team email, Teams channel webhook
# Used by: BUG21-AuthFailureSpike, BUG21-ContainerRestartStorm
# ---------------------------------------------------------------------------
resource "azurerm_monitor_action_group" "security_warning" {
  name                = "ag-security-warning"
  resource_group_name = module.rg.resource_group_name
  short_name          = "sec-warn"
  enabled             = true

  tags = var.tags

  dynamic "webhook_receiver" {
    for_each = local.security_warning_webhook_receivers
    content {
      name                    = webhook_receiver.value.name
      service_uri             = webhook_receiver.value.service_uri
      use_common_alert_schema = webhook_receiver.value.use_common_alert_schema
    }
  }

  dynamic "email_receiver" {
    for_each = local.security_warning_email_receivers
    content {
      name                    = email_receiver.key
      email_address           = email_receiver.value
      use_common_alert_schema = true
    }
  }

  depends_on = [module.rg]
}

# ---------------------------------------------------------------------------
# Alert Rule 2 — BUG21-BypassActive-Production (CRITICAL — no auto-resolve)
#
# Fires if "[BUG-21] Dev auth bypass ACTIVE" appears in production console logs.
# This string should NEVER appear outside a local dev environment. Its presence
# in production means authentication is disabled for all requests.
#
# Deployed before Rule 1 (LogFatal) because FM-2 is the higher-risk scenario:
# the app is serving unauthenticated traffic with no boot failure to alert ops.
#
# Auto-resolve: DISABLED. Requires manual acknowledgment.
# ---------------------------------------------------------------------------
resource "azurerm_monitor_scheduled_query_rules_alert_v2" "bug21_bypass_active" {
  name                = "BUG21-BypassActive-Production"
  resource_group_name = module.rg.resource_group_name
  location            = var.location

  description  = "CRITICAL: [BUG-21] Dev auth bypass ACTIVE detected in production container logs. Authentication is disabled. Requires immediate manual response — alert does NOT auto-resolve."
  display_name = "BUG-21 — Dev Auth Bypass ACTIVE in Production"
  enabled      = true
  severity     = 0 # Critical

  # Evaluation window and frequency
  window_duration      = "PT5M"
  evaluation_frequency = "PT5M"

  # No auto-resolve — this is an active exploit condition.
  # Operator must manually resolve in Azure Monitor after remediation is confirmed.
  auto_mitigation_enabled = false

  scopes = [azurerm_log_analytics_workspace.law.id]

  # ContainerAppConsoleLogs_CL variant (App Insights not active).
  # Switch to AppTraces variant once App Insights SDK is wired in:
  #   AppTraces
  #   | where TimeGenerated > ago(5m)
  #   | where Message has "[BUG-21] Dev auth bypass ACTIVE"
  #       or Message has "ALLOW_DEV_AUTH_BYPASS=true" # BUG-21
  #   | summarize count() by bin(TimeGenerated, 5m)
  #   | where count_ >= 1
  criteria {
    query = <<-KQL
      ContainerAppConsoleLogs_CL
      | where TimeGenerated > ago(5m)
      | where Log_s has "[BUG-21] Dev auth bypass ACTIVE"
          or (Log_s has "ALLOW_DEV_AUTH_BYPASS" and Log_s !has "Development" and Log_s !has "Blocked") # BUG-21
      | summarize count() by bin(TimeGenerated, 5m)
      | where count_ >= 1
    KQL

    time_aggregation_method = "Count"
    threshold               = 1
    operator                = "GreaterThanOrEqual"

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.security_critical.id]
  }

  tags = var.tags

  depends_on = [
    azurerm_log_analytics_workspace.law,
    azurerm_monitor_action_group.security_critical,
  ]
}

# ---------------------------------------------------------------------------
# Alert Rule 1 — BUG21-LogFatal-BootFail (CRITICAL — auto-resolve 1h)
#
# Fires when the ValidateDevAuthBypassConfig() boot-fail guard triggers.
# The container will fail its health check and not serve traffic — but ops
# may not be notified without this alert. At least one healthy revision will
# continue serving traffic if running in Single revision mode with prior rev.
#
# Auto-resolve: YES (1 hour). The log event is a one-time startup emission;
# if the bad revision is deactivated, no further events will appear.
# ---------------------------------------------------------------------------
resource "azurerm_monitor_scheduled_query_rules_alert_v2" "bug21_log_fatal" {
  name                = "BUG21-LogFatal-BootFail"
  resource_group_name = module.rg.resource_group_name
  location            = var.location

  description  = "CRITICAL (BUG-21): Log.Fatal from ValidateDevAuthBypassConfig detected. A Container App revision was deployed with the dev-auth-bypass flag set in a non-Development environment. The container will not serve traffic. Deactivate the bad revision immediately."
  display_name = "BUG-21 — Log.Fatal Boot Guard Triggered"
  enabled      = true
  severity     = 0 # Critical

  window_duration         = "PT5M"
  evaluation_frequency    = "PT5M"
  auto_mitigation_enabled = true

  scopes = [azurerm_log_analytics_workspace.law.id]

  # ContainerAppConsoleLogs_CL variant (App Insights not active).
  # Switch to AppTraces variant once App Insights SDK is wired in:
  #   union AppTraces, ContainerAppConsoleLogs_CL
  #   | where TimeGenerated > ago(5m)
  #   | where Message has "ALLOW_DEV_AUTH_BYPASS" and SeverityLevel == 4 # BUG-21
  #   | summarize count() by bin(TimeGenerated, 5m)
  #   | where count_ >= 1
  criteria {
    query = <<-KQL
      ContainerAppConsoleLogs_CL
      | where TimeGenerated > ago(5m)
      | where Log_s has "ValidateDevAuthBypassConfig"
          or (Log_s has "Log.Fatal" and Log_s has "ALLOW_DEV_AUTH_BYPASS") # BUG-21
          or (Log_s has "[FTL]" and Log_s has "ALLOW_DEV_AUTH_BYPASS") # BUG-21
      | where Log_s !has "Development"
      | summarize count() by bin(TimeGenerated, 5m)
      | where count_ >= 1
    KQL

    time_aggregation_method = "Count"
    threshold               = 1
    operator                = "GreaterThanOrEqual"

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.security_critical.id]
  }

  tags = var.tags

  depends_on = [
    azurerm_log_analytics_workspace.law,
    azurerm_monitor_action_group.security_critical,
  ]
}

# ---------------------------------------------------------------------------
# Alert Rule 3 — BUG21-AuthFailureSpike (HIGH — auto-resolve 30m)
#
# Detects exploitation probe attempts: a burst of HTTP 401/403 responses that
# could indicate credential stuffing or an attacker probing for the bypass.
# Requires AppRequests table — only available when Application Insights is wired in.
# Disabled until App Insights is active; flip enabled = true at that point.
# ---------------------------------------------------------------------------
resource "azurerm_monitor_scheduled_query_rules_alert_v2" "bug21_auth_failure_spike" {
  name                = "BUG21-AuthFailureSpike"
  resource_group_name = module.rg.resource_group_name
  location            = var.location

  description  = "HIGH: More than 10 HTTP 401/403 responses in a 5-minute window. May indicate an exploitation probe or credential-stuffing attack against the MCP API."
  display_name = "BUG-21 — Auth Failure Spike (401/403 burst)"
  enabled      = false # Enable after Application Insights is wired in (AppRequests table required)
  severity     = 1     # Error

  window_duration         = "PT5M"
  evaluation_frequency    = "PT5M"
  auto_mitigation_enabled = true

  scopes = [azurerm_log_analytics_workspace.law.id]

  criteria {
    query = <<-KQL
      AppRequests
      | where TimeGenerated > ago(5m)
      | where ResultCode in ("401", "403")
      | summarize FailureCount = count() by bin(TimeGenerated, 5m)
      | where FailureCount > 10
    KQL

    time_aggregation_method = "Count"
    threshold               = 10
    operator                = "GreaterThan"

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.security_warning.id]
  }

  tags = var.tags

  depends_on = [
    azurerm_log_analytics_workspace.law,
    azurerm_monitor_action_group.security_warning,
  ]
}

# ---------------------------------------------------------------------------
# Alert Rule 4 — BUG21-ContainerRestartStorm (HIGH — auto-resolve 1h)
#
# Rapid container restart cycling is a secondary signal of the Log.Fatal
# boot-fail: the app exits on fatal error and ACA retries the revision.
# More than 3 restarts in 10 minutes = startup guard firing repeatedly.
# ---------------------------------------------------------------------------
resource "azurerm_monitor_scheduled_query_rules_alert_v2" "bug21_container_restart_storm" {
  name                = "BUG21-ContainerRestartStorm"
  resource_group_name = module.rg.resource_group_name
  location            = var.location

  description  = "HIGH: More than 3 container restart events in a 10-minute window for the ato-copilot Container App. May indicate the Log.Fatal boot guard is firing repeatedly due to a bad env var injection."
  display_name = "BUG-21 — Container Restart Storm"
  enabled      = true
  severity     = 1 # Error

  window_duration         = "PT10M"
  evaluation_frequency    = "PT5M"
  auto_mitigation_enabled = true

  scopes = [azurerm_log_analytics_workspace.law.id]

  criteria {
    query = <<-KQL
      ContainerAppSystemLogs_CL
      | where TimeGenerated > ago(10m)
      | where Reason_s == "BackOff" or Reason_s == "OOMKilling" or Type_s == "Warning"
      | where ObjectRef_name_s has "ato-copilot"
      | summarize RestartCount = count() by bin(TimeGenerated, 10m)
      | where RestartCount > 3
    KQL

    time_aggregation_method = "Count"
    threshold               = 3
    operator                = "GreaterThan"

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.security_warning.id]
  }

  tags = var.tags

  depends_on = [
    azurerm_log_analytics_workspace.law,
    azurerm_monitor_action_group.security_warning,
  ]
}
