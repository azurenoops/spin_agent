# =============================================================================
# environments/dev.tfvars — Dev / Test defaults
# Deploy:
#   terraform apply -var-file="environments/dev.tfvars"
# =============================================================================

org_name           = "anoa"
workload_name      = "ato-cop"
environment        = "public"   # Azure Commercial
deploy_environment = "dev"
location           = "centralus"

tags = {
  project     = "ato-copilot"
  environment = "dev"
  managed_by  = "terraform"
}

# Key Vault
kv_admin_object_ids = []  # Add deployer SPN object IDs here

# SQL — Basic SKU keeps dev costs low
sql_admin_login = "sqladmin"
sql_sku_name    = "S2"

# Container Registry — Basic SKU in dev
acr_sku = "Basic"

# Container App — scale-to-zero to save cost
container_image = "mcr.microsoft.com/dotnet/aspnet:9.0"
min_replicas    = 0
max_replicas    = 2
container_cpu   = 0.5
container_memory = "1Gi"

# Optional features — off by default in dev
deploy_openai      = false
deploy_bot_channel = false
