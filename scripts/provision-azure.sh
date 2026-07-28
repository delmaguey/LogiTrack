#!/usr/bin/env bash
set -euo pipefail

# Provisions the Azure resources needed to run LogiTrack on App Service,
# and prints the GitHub Actions secrets you need to set afterward.
#
# Requires: az CLI logged in (`az login`), with the subscription you want
# already selected (`az account set --subscription <name-or-id>`).
#
# Usage: ./scripts/provision-azure.sh

# ---- Configuration ----------------------------------------------------
RESOURCE_GROUP="logitrack-rg"
LOCATION="mexicocentral"          # closest to Mexico City
APP_SERVICE_PLAN="logitrack-plan"
SKU="F1"                      
WEBAPP_NAME="logitrack-api"
RUNTIME="DOTNETCORE:9.0"
# ------------------------------------------------------------------------

echo "==> Creating resource group '${RESOURCE_GROUP}' in ${LOCATION}"
az group create \
  --name "${RESOURCE_GROUP}" \
  --location "${LOCATION}" \
  --output none

echo "==> Creating App Service plan '${APP_SERVICE_PLAN}' (${SKU}, Linux)"
az appservice plan create \
  --name "${APP_SERVICE_PLAN}" \
  --resource-group "${RESOURCE_GROUP}" \
  --location "${LOCATION}" \
  --sku "${SKU}" \
  --is-linux \
  --output none

echo "==> Creating Web App '${WEBAPP_NAME}' (${RUNTIME})"
az webapp create \
  --name "${WEBAPP_NAME}" \
  --resource-group "${RESOURCE_GROUP}" \
  --plan "${APP_SERVICE_PLAN}" \
  --runtime "${RUNTIME}" \
  --output none

echo "==> Enforcing HTTPS-only"
az webapp update \
  --name "${WEBAPP_NAME}" \
  --resource-group "${RESOURCE_GROUP}" \
  --https-only true \
  --output none

# az webapp config set \
#   --name "${WEBAPP_NAME}" \
#   --resource-group "${RESOURCE_GROUP}" \
#   --always-on true \
#   --output none

echo "==> Setting baseline application settings (JWT values still need to be set separately)"
az webapp config appsettings set \
  --name "${WEBAPP_NAME}" \
  --resource-group "${RESOURCE_GROUP}" \
  --settings ASPNETCORE_ENVIRONMENT=Production \
  --output none

WEBAPP_ID=$(az webapp show \
  --name "${WEBAPP_NAME}" \
  --resource-group "${RESOURCE_GROUP}" \
  --query id -o tsv)

echo "==> Creating a service principal scoped to this Web App (for GitHub Actions AZURE_CREDENTIALS)"
SP_JSON=$(az ad sp create-for-rbac \
  --name "github-actions-${WEBAPP_NAME}" \
  --role contributor \
  --scopes "${WEBAPP_ID}" \
  --sdk-auth)

echo ""
echo "============================================================"
echo " Provisioning complete."
echo ""
echo " Web App URL:        https://${WEBAPP_NAME}.azurewebsites.net"
echo ""
echo " GitHub repo secrets to set (Settings > Secrets and variables > Actions):"
echo ""
echo " AZURE_CREDENTIALS ="
echo "${SP_JSON}"
echo ""
echo " JWT_KEY      = <generate one, e.g.: openssl rand -base64 64>"
echo " JWT_ISSUER   = LogiTrack"
echo " JWT_AUDIENCE = LogiTrackUsers"
echo ""
echo " Also update AZURE_WEBAPP_NAME in .github/workflows/deploy.yml to: ${WEBAPP_NAME}"
echo "============================================================"
