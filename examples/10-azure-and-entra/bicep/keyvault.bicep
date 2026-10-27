// Generic Azure resource deployed through udpi30's azure_deployments escape
// hatch. Any Bicep template works here — this one provisions a Key Vault.
@description('Name of the Key Vault')
param vaultName string

@description('Location for the vault')
param location string = resourceGroup().location

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
  }
}

output vaultUri string = vault.properties.vaultUri
