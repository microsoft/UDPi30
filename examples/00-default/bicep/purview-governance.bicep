// Governance add-ons for an existing Microsoft Purview account, deployed through
// udpi30's azure_deployments escape hatch (resource-group scope).
//
// ARM can express: diagnostic settings, a delete lock, and private endpoints.
// ARM cannot express Purview data-plane objects (collections, scans, glossary,
// classifications, sensitivity-label policies) — see README.md "Purview scope".

@description('Name of the existing Purview account')
param purviewAccountName string

@description('Resource ID of the Log Analytics workspace that receives Purview diagnostics')
param logAnalyticsWorkspaceId string

@description('Subnet for the Purview private endpoints. Leave empty to skip them.')
param privateEndpointSubnetId string = ''

@description('Location for the private endpoints')
param location string = resourceGroup().location

resource purview 'Microsoft.Purview/accounts@2021-12-01' existing = {
  name: purviewAccountName
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'purview-to-log-analytics'
  scope: purview
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

resource deleteLock 'Microsoft.Authorization/locks@2020-05-01' = {
  name: 'purview-cannot-delete'
  scope: purview
  properties: {
    level: 'CanNotDelete'
    notes: 'Protects the Purview account and its catalog from accidental deletion.'
  }
}

// "account" serves the Data Map / catalog APIs; "portal" serves the governance portal.
var endpointGroups = [
  'account'
  'portal'
]

resource endpoints 'Microsoft.Network/privateEndpoints@2023-11-01' = [for group in endpointGroups: if (!empty(privateEndpointSubnetId)) {
  name: 'pe-${purviewAccountName}-${group}'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'purview-${group}'
        properties: {
          privateLinkServiceId: purview.id
          groupIds: [
            group
          ]
        }
      }
    ]
  }
}]

output purviewId string = purview.id
