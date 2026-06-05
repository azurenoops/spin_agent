# =============================================================================
# environments/prod.tfvars — Production template (DoD / Azure Government)
# Replace every <replace> value before deploying.
# NEVER commit this file with real passwords or secrets.
#
# Deploy:
#   export TF_VAR_sql_admin_password="$(az keyvault secret show ...)"
#   terraform apply -var-file="environments/prod.tfvars"
# =============================================================================

org_name           = "anoa"
workload_name      = "ato-cop"
environment        = "usgovernment" # Azure Government / MAG
deploy_environment = "prod"
location           = "usgovvirginia"

tags = {
  project     = "ato-copilot"
  environment = "prod"
  managed_by  = "terraform"
  owner       = "<replace>" # Team / individual owner for cost tracking
  cost_center = "<replace>" # Finance cost center code
}

# Key Vault
# Object IDs of principals that need KV admin (deployer SPN, security team)
kv_admin_object_ids = [
  # "<replace>"  # az ad sp show --display-name "<deployer-spn>" --query id -o tsv
]

# SQL — Standard S4 in prod; password injected via TF_VAR_sql_admin_password
sql_admin_login = "sqladmin"
sql_sku_name    = "S4"

# Container Registry — Standard SKU in prod
acr_sku = "Standard"

# Container App — no scale-to-zero in prod
# Replace with ACR image: cratocopilotprod.azurecr.io/ato-copilot-mcp:<tag>
container_image  = "<replace>"
min_replicas     = 1
max_replicas     = 5
container_cpu    = 1.0
container_memory = "2Gi"

# Azure OpenAI — enable for production AI-assisted compliance features
deploy_openai                = true
openai_model_deployment_name = "gpt-4o"
openai_model_name            = "gpt-4o"
openai_model_version         = "2024-05-13"
openai_model_capacity        = 30

# M365 Bot Channel — enable after Teams app registration
deploy_bot_channel = true
bot_app_id         = "<replace>" # Azure AD → App registrations → Application (client) ID
bot_display_name   = "ATO Copilot"
