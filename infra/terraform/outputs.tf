# =============================================================================
# outputs.tf — Root module outputs
# =============================================================================

output "resource_group_name" {
  description = "Name of the deployed resource group."
  value       = module.rg.resource_group_name
}

output "container_app_fqdn" {
  description = "Fully qualified domain name of the ATO Copilot Container App (public ingress)."
  value       = azurerm_container_app.mcp.ingress[0].fqdn
}

output "container_app_name" {
  description = "Name of the Container App resource."
  value       = azurerm_container_app.mcp.name
}

output "container_app_principal_id" {
  description = "System-Assigned Managed Identity principal ID of the Container App."
  value       = azurerm_container_app.mcp.identity[0].principal_id
}

output "key_vault_id" {
  description = "Resource ID of the Key Vault."
  value       = module.kv.key_vault_id
}

output "key_vault_uri" {
  description = "URI of the Key Vault (used to construct secret references)."
  value       = module.kv.key_vault_uri
}

output "key_vault_name" {
  description = "Name of the Key Vault."
  value       = module.kv.key_vault_name
}

output "acr_login_server" {
  description = "Login server URL of the Azure Container Registry."
  value       = module.acr.login_server
}

output "acr_name" {
  description = "Name of the Azure Container Registry."
  value       = module.acr.container_registry_name
}

output "sql_server_fqdn" {
  description = "Fully qualified domain name of the Azure SQL Server."
  value       = module.sql.primary_sql_server_fqdn
}

output "openai_endpoint" {
  description = "Azure OpenAI service endpoint URL. Empty string when deploy_openai = false."
  value       = var.deploy_openai ? module.openai[0].endpoint : ""
}

output "bot_service_name" {
  description = "Name of the Azure Bot Service resource. Empty string when deploy_bot_channel = false."
  value       = var.deploy_bot_channel ? azurerm_bot_service_azure_bot.mcp_bot[0].name : ""
}
