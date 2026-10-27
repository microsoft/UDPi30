// Subscription-scope governance baseline, deployed through udpi30's
// azure_deployments escape hatch (scope: subscription).
//
// Defender for Cloud plans and policy assignments are subscription-level ARM
// resources, so they cannot be created by the generated resource-group templates.
targetScope = 'subscription'

@description('Tag every resource must carry')
param requiredTagName string = 'data-owner'

@description('Turn on the paid Defender for Cloud plans below')
param enableDefender bool = false

// Built-in policy "Require a tag on resources". Audit-only (DoNotEnforce) so the
// assignment reports non-compliance without blocking deployments.
resource requireTag 'Microsoft.Authorization/policyAssignments@2022-06-01' = {
  name: 'require-${requiredTagName}-tag'
  properties: {
    displayName: 'Require a ${requiredTagName} tag on resources'
    policyDefinitionId: tenantResourceId('Microsoft.Authorization/policyDefinitions', '871b6d14-10aa-478d-b590-94f262ecfa99')
    enforcementMode: 'DoNotEnforce'
    parameters: {
      tagName: {
        value: requiredTagName
      }
    }
  }
}

var defenderPlans = [
  {
    name: 'VirtualMachines'
    subPlan: 'P2'
  }
  {
    name: 'StorageAccounts'
    subPlan: 'DefenderForStorageV2'
  }
  {
    name: 'SqlServers'
    subPlan: ''
  }
  {
    name: 'KeyVaults'
    subPlan: ''
  }
  {
    name: 'Arm'
    subPlan: ''
  }
]

// Pricing changes are not safe to run in parallel.
@batchSize(1)
resource defender 'Microsoft.Security/pricings@2024-01-01' = [for plan in defenderPlans: if (enableDefender) {
  name: plan.name
  properties: {
    pricingTier: 'Standard'
    subPlan: empty(plan.subPlan) ? null : plan.subPlan
  }
}]
