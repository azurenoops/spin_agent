# =============================================================================
# variables.tf — All input variables for the ATO Copilot Terraform root module
# =============================================================================

# ---------------------------------------------------------------------------
# Global / org identity
# ---------------------------------------------------------------------------

variable "org_name" {
  description = "Organization short-name used in resource naming (azurenoops overlay convention). Example: 'anoa'."
  type        = string
  default     = "anoa"
}

variable "workload_name" {
  description = "Workload short-name used in resource naming. Example: 'ato-cop'."
  type        = string
  default     = "ato-cop"
}

variable "environment" {
  description = "The Terraform backend environment: 'public' (Azure commercial) or 'usgovernment' (Azure Government / MAG)."
  type        = string
  default     = "public"

  validation {
    condition     = contains(["public", "usgovernment"], var.environment)
    error_message = "environment must be 'public' or 'usgovernment'."
  }
}

variable "deploy_environment" {
  description = "Workload lifecycle stage used in resource naming and tier selection: 'dev', 'staging', or 'prod'."
  type        = string

  validation {
    condition     = contains(["dev", "staging", "prod"], var.deploy_environment)
    error_message = "deploy_environment must be 'dev', 'staging', or 'prod'."
  }
}

variable "location" {
  description = "Azure region for all resources. Use 'eastus' or 'centralus' for dev; 'usgovvirginia' or 'usgovarizona' for production DoD."
  type        = string
}

variable "tags" {
  description = "Map of tags applied to all resources."
  type        = map(string)
  default     = {}
}

# ---------------------------------------------------------------------------
# Key Vault
# ---------------------------------------------------------------------------

variable "kv_admin_object_ids" {
  description = "List of Azure AD object IDs (users, groups, SPNs) that receive the Key Vault Administrator role. Used by deployer pipelines. Example: [data.azurerm_client_config.current.object_id]."
  type        = list(string)
  default     = []
}

# ---------------------------------------------------------------------------
# Azure Container Registry
# ---------------------------------------------------------------------------

variable "acr_sku" {
  description = "Container Registry SKU. 'Basic' for dev, 'Standard' for staging/prod."
  type        = string
  default     = "Standard"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.acr_sku)
    error_message = "acr_sku must be Basic, Standard, or Premium."
  }
}

# ---------------------------------------------------------------------------
# Azure SQL
# ---------------------------------------------------------------------------

variable "sql_admin_login" {
  description = "SQL Server administrator login name."
  type        = string
  default     = "sqladmin"
}

variable "sql_admin_password" {
  description = "SQL Server administrator password. Use a Key Vault reference or injected secret in CI — never hardcode."
  type        = string
  sensitive   = true
}

variable "sql_sku_name" {
  description = "SQL Database SKU. Example: 'GP_S_Gen5_2' (General Purpose serverless) or 'S2' (Standard DTU-based)."
  type        = string
  default     = "S2"
}

# ---------------------------------------------------------------------------
# Container App
# ---------------------------------------------------------------------------

variable "container_image" {
  description = "Container image reference including full ACR path and tag. Example: 'cratocopilotdev.azurecr.io/ato-copilot-mcp:1.2.3'."
  type        = string
  default     = "mcr.microsoft.com/dotnet/aspnet:9.0"
}

variable "min_replicas" {
  description = "Minimum Container App replicas. Set 0 for scale-to-zero (dev). Set 1+ for production to avoid cold starts."
  type        = number
  default     = 0

  validation {
    condition     = var.min_replicas >= 0
    error_message = "min_replicas must be >= 0."
  }
}

variable "max_replicas" {
  description = "Maximum Container App replicas."
  type        = number
  default     = 2

  validation {
    condition     = var.max_replicas >= 1
    error_message = "max_replicas must be >= 1."
  }
}

variable "container_cpu" {
  description = "CPU cores allocated per container replica. Must be a value supported by ACA (0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0)."
  type        = number
  default     = 0.5
}

variable "container_memory" {
  description = "Memory allocated per container replica. Must match a valid ACA memory size (e.g. '1Gi', '2Gi')."
  type        = string
  default     = "1Gi"
}

# ---------------------------------------------------------------------------
# Azure OpenAI (optional)
# ---------------------------------------------------------------------------

variable "deploy_openai" {
  description = "Set to true to provision an Azure OpenAI resource in this deployment. Disabled by default."
  type        = bool
  default     = false
}

variable "openai_model_deployment_name" {
  description = "Azure OpenAI model deployment name."
  type        = string
  default     = "gpt-4o"
}

variable "openai_model_name" {
  description = "Azure OpenAI model name as registered in the service. Example: 'gpt-4o'."
  type        = string
  default     = "gpt-4o"
}

variable "openai_model_version" {
  description = "Azure OpenAI model version string."
  type        = string
  default     = "2024-05-13"
}

variable "openai_model_capacity" {
  description = "Tokens-per-minute capacity units (TPM × 1000) for the model deployment."
  type        = number
  default     = 30
}

# ---------------------------------------------------------------------------
# M365 Bot Channel (optional)
# ---------------------------------------------------------------------------

variable "deploy_bot_channel" {
  description = "Set to true to provision Azure Bot Service + Teams channel. Requires bot_app_id."
  type        = bool
  default     = false
}

variable "bot_app_id" {
  description = "Microsoft App (Bot) ID — the Application (client) ID from Azure AD app registration. Required when deploy_bot_channel = true."
  type        = string
  default     = ""
}

variable "bot_display_name" {
  description = "Display name shown in Teams for the bot."
  type        = string
  default     = "ATO Copilot"
}
