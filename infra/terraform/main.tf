# =============================================================================
# ATO Copilot — Terraform Root Module
#
# Wires azurenoops overlay modules for all core Azure resources.
# Native azurerm resources are used for ACA (no overlay exists).
#
# Deploy:
#   terraform init
#   terraform plan -var-file="environments/dev.tfvars"
#   terraform apply -var-file="environments/dev.tfvars"
#
# Dry-run:
#   terraform plan -var-file="environments/dev.tfvars"
# =============================================================================

terraform {
  required_version = ">= 1.3"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.22"
    }
    azurenoopsutils = {
      source  = "azurenoops/azurenoopsutils"
      version = "~> 1.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.5"
    }
  }

  # Remote state backend — configure before first apply.
  # See docs/deployment.md § Terraform Deployment for setup instructions.
  # backend "azurerm" {
  #   resource_group_name  = "<state-rg>"
  #   storage_account_name = "<state-sa>"
  #   container_name       = "tfstate"
  #   key                  = "ato-copilot.tfstate"
  # }
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy    = var.deploy_environment == "prod" ? false : true
      recover_soft_deleted_key_vaults = true
    }
  }
  environment = var.environment # "public" or "usgovernment"
}

# ---------------------------------------------------------------------------
# Region lookup — converts long name → short CLI name used by overlay naming
# ---------------------------------------------------------------------------
module "mod_location" {
  source  = "azurenoops/overlays-azregions-lookup/azurerm"
  version = "~> 1.0"

  azure_region = var.location
}

# ---------------------------------------------------------------------------
# Resource Group
# ---------------------------------------------------------------------------
module "rg" {
  source  = "azurenoops/overlays-resource-group/azurerm"
  version = "~> 1.0"

  org_name                = var.org_name
  environment             = var.environment
  workload_name           = var.workload_name
  location                = module.mod_location.location_short
  use_location_short_name = true

  add_tags = var.tags
}

# ---------------------------------------------------------------------------
# Key Vault
# ---------------------------------------------------------------------------
module "kv" {
  source  = "azurenoops/overlays-key-vault/azurerm"
  version = "~> 2.0"

  org_name           = var.org_name
  environment        = var.environment
  workload_name      = var.workload_name
  deploy_environment = var.deploy_environment
  location           = module.mod_location.location_cli

  existing_resource_group_name = module.rg.resource_group_name

  sku_name                   = "standard"
  rbac_authorization_enabled = true
  enable_purge_protection    = var.deploy_environment == "prod" ? true : false
  soft_delete_retention_days = var.deploy_environment == "prod" ? 90 : 7

  # Network — allow Azure services; tighten to private endpoint in prod
  network_acls = {
    bypass         = "AzureServices"
    default_action = "Allow"
    ip_rules       = []
  }

  # Objects that receive full admin access (e.g., the deployer SPN)
  admin_objects_ids = var.kv_admin_object_ids

  add_tags = var.tags

  depends_on = [module.rg]
}

# ---------------------------------------------------------------------------
# Azure Container Registry
# ---------------------------------------------------------------------------
module "acr" {
  source  = "azurenoops/overlays-container-registry/azurerm"
  version = "~> 2.0"

  org_name           = var.org_name
  environment        = var.environment
  workload_name      = var.workload_name
  deploy_environment = var.deploy_environment
  location           = module.mod_location.location_cli

  existing_resource_group_name = module.rg.resource_group_name

  sku           = var.acr_sku
  admin_enabled = false # Managed Identity pull — no admin credentials

  add_tags = var.tags

  depends_on = [module.rg]
}

# ---------------------------------------------------------------------------
# Azure SQL (azsql overlay)
# ---------------------------------------------------------------------------
module "sql" {
  source  = "azurenoops/overlays-azsql/azurerm"
  version = "~> 2.0"

  org_name           = var.org_name
  environment        = var.environment
  workload_name      = var.workload_name
  deploy_environment = var.deploy_environment
  location           = module.mod_location.location_cli

  existing_resource_group_name = module.rg.resource_group_name

  administrator_login    = var.sql_admin_login
  administrator_password = var.sql_admin_password
  tls_minimum_version    = "1.2"

  databases = [
    {
      name        = "AtoCopilot"
      max_size_gb = var.deploy_environment == "prod" ? 50 : 5
      sku_name    = var.sql_sku_name
    }
  ]

  # Allow Azure services (Container App egress IPs added post-deploy)
  enable_firewall_rules         = true
  public_network_access_enabled = true
  firewall_rules = [
    {
      name             = "AllowAzureServices"
      start_ip_address = "0.0.0.0"
      end_ip_address   = "0.0.0.0"
    }
  ]

  add_tags = var.tags

  depends_on = [module.rg]
}

# ---------------------------------------------------------------------------
# Store SQL connection string as a Key Vault secret
# ---------------------------------------------------------------------------
resource "azurerm_key_vault_secret" "sql_connection_string" {
  name  = "sql-connection-string"
  value = "Server=tcp:${module.sql.primary_sql_server_fqdn},1433;Database=AtoCopilot;User ID=${var.sql_admin_login};Password=${var.sql_admin_password};Encrypt=True;TrustServerCertificate=False;"

  key_vault_id = module.kv.key_vault_id

  depends_on = [module.kv, module.sql]
}

# ---------------------------------------------------------------------------
# Azure OpenAI (optional)
# ---------------------------------------------------------------------------
module "openai" {
  count   = var.deploy_openai ? 1 : 0
  source  = "azurenoops/overlays-openai-cognitive-account/azurerm"
  version = "~> 1.0"

  org_name           = var.org_name
  environment        = var.environment
  workload_name      = "${var.workload_name}-oai"
  deploy_environment = var.deploy_environment
  location           = module.mod_location.location_cli

  existing_resource_group_name = module.rg.resource_group_name
  custom_subdomain_name        = "oai-${var.org_name}-${var.workload_name}-${var.deploy_environment}"

  sku_name = "S0"

  deployments = [
    {
      name = var.openai_model_deployment_name
      model = {
        name    = var.openai_model_name
        version = var.openai_model_version
      }
      scale = {
        type     = "Standard"
        capacity = var.openai_model_capacity
      }
      rai_policy_name = ""
    }
  ]

  add_tags = var.tags

  depends_on = [module.rg]
}

# Store OpenAI API key in Key Vault (only when OpenAI is deployed)
resource "azurerm_key_vault_secret" "openai_api_key" {
  count = var.deploy_openai ? 1 : 0

  name         = "azure-openai-api-key"
  value        = module.openai[0].primary_access_key
  key_vault_id = module.kv.key_vault_id

  depends_on = [module.kv, module.openai]
}

# ---------------------------------------------------------------------------
# Log Analytics Workspace (required by Container Apps Environment)
# ---------------------------------------------------------------------------
resource "azurerm_log_analytics_workspace" "law" {
  name                = "law-${var.org_name}-${var.workload_name}-${var.deploy_environment}"
  location            = var.location
  resource_group_name = module.rg.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = var.deploy_environment == "prod" ? 90 : 30

  tags = var.tags

  depends_on = [module.rg]
}

# ---------------------------------------------------------------------------
# Container Apps Environment (native azurerm — no overlay exists)
# ---------------------------------------------------------------------------
resource "azurerm_container_app_environment" "cae" {
  name                       = "cae-${var.org_name}-${var.workload_name}-${var.deploy_environment}"
  location                   = var.location
  resource_group_name        = module.rg.resource_group_name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.law.id

  tags = var.tags

  depends_on = [azurerm_log_analytics_workspace.law]
}

# ---------------------------------------------------------------------------
# Container App (native azurerm)
# ---------------------------------------------------------------------------
resource "azurerm_container_app" "mcp" {
  name                         = "ca-${var.org_name}-${var.workload_name}-${var.deploy_environment}"
  container_app_environment_id = azurerm_container_app_environment.cae.id
  resource_group_name          = module.rg.resource_group_name
  revision_mode                = "Single"

  tags = var.tags

  identity {
    type = "SystemAssigned"
  }

  registry {
    server   = module.acr.login_server
    identity = "System"
  }

  secret {
    name                = "sql-connection-string"
    key_vault_secret_id = azurerm_key_vault_secret.sql_connection_string.versionless_id
    identity            = "System"
  }

  dynamic "secret" {
    for_each = var.deploy_openai ? [1] : []
    content {
      name                = "azure-openai-api-key"
      key_vault_secret_id = azurerm_key_vault_secret.openai_api_key[0].versionless_id
      identity            = "System"
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "http"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = var.min_replicas
    max_replicas = var.max_replicas

    container {
      name   = "ato-copilot-mcp"
      image  = var.container_image
      cpu    = var.container_cpu
      memory = var.container_memory

      env {
        name        = "ConnectionStrings__DefaultConnection"
        secret_name = "sql-connection-string"
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = var.deploy_environment == "prod" ? "Production" : "Development"
      }

      dynamic "env" {
        for_each = var.deploy_openai ? [1] : []
        content {
          name  = "AzureOpenAI__Endpoint"
          value = module.openai[0].endpoint
        }
      }

      dynamic "env" {
        for_each = var.deploy_openai ? [1] : []
        content {
          name        = "AzureOpenAI__ApiKey"
          secret_name = "azure-openai-api-key"
        }
      }

      liveness_probe {
        path                    = "/health"
        port                    = 8080
        transport               = "HTTP"
        initial_delay           = 15
        interval_seconds        = 30
        failure_count_threshold = 3
      }

      readiness_probe {
        path                    = "/health"
        port                    = 8080
        transport               = "HTTP"
        interval_seconds        = 10
        failure_count_threshold = 3
      }
    }
  }

  depends_on = [
    azurerm_container_app_environment.cae,
    azurerm_key_vault_secret.sql_connection_string,
    module.acr,
  ]
}

# ---------------------------------------------------------------------------
# Grant Key Vault Secrets User role to the Container App's Managed Identity
# ---------------------------------------------------------------------------
resource "azurerm_role_assignment" "ca_kv_secrets_user" {
  scope                = module.kv.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_container_app.mcp.identity[0].principal_id

  depends_on = [azurerm_container_app.mcp, module.kv]
}

# Grant AcrPull to the Container App's Managed Identity
resource "azurerm_role_assignment" "ca_acr_pull" {
  scope                = module.acr.container_registry_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_container_app.mcp.identity[0].principal_id

  depends_on = [azurerm_container_app.mcp, module.acr]
}

# ---------------------------------------------------------------------------
# M365 Bot Channel (optional — wire when deploy_bot_channel = true)
# ---------------------------------------------------------------------------
resource "azurerm_bot_service_azure_bot" "mcp_bot" {
  count = var.deploy_bot_channel ? 1 : 0

  name                = "bot-${var.org_name}-${var.workload_name}-${var.deploy_environment}"
  resource_group_name = module.rg.resource_group_name
  location            = "global"
  sku                 = var.deploy_environment == "prod" ? "S1" : "F0"
  microsoft_app_id    = var.bot_app_id

  display_name                          = var.bot_display_name
  endpoint                              = "https://${azurerm_container_app.mcp.ingress[0].fqdn}/api/messages"
  developer_app_insights_api_key        = null
  developer_app_insights_application_id = null

  tags = var.tags

  depends_on = [azurerm_container_app.mcp]
}

resource "azurerm_bot_channel_ms_teams" "mcp_teams" {
  count = var.deploy_bot_channel ? 1 : 0

  bot_name            = azurerm_bot_service_azure_bot.mcp_bot[0].name
  location            = azurerm_bot_service_azure_bot.mcp_bot[0].location
  resource_group_name = module.rg.resource_group_name

  calling_web_hook = null
  enable_calling   = false

  depends_on = [azurerm_bot_service_azure_bot.mcp_bot]
}
