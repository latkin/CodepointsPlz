param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('https://twitter.com/\S+/status/[0-9]+')]
    [string] $TweetUrl
)

$errorActionPreference = 'Stop'

$mentionsQueue = Get-AzureRmStorageQueueQueue -storageAccountName codepointsplz `
                                              -resourceGroup CodepointsPlz `
                                              -queueName mentions `
                                              -ea 0
if (-not $mentionsQueue) {
    Connect-AzureRmAccount | Out-Null
    $mentionsQueue = Get-AzureRmStorageQueueQueue -storageAccountName codepointsplz `
                                                  -resourceGroup CodepointsPlz `
                                                  -queueName mentions
    
}

if ($tweetUrl -match 'https://twitter.com/\S+/status/(?<statusid>[0-9]+)') {
    $queueJson = @{ 
        StatusID      = [uint64]$matches['statusid']
        DirectTrigger = $true
    } | ConvertTo-Json

    Write-Host $queueJson
    Read-Host "Press enter to submit as a direct trigger, or Ctrl-C to abort" | Out-Null

    Add-AzureRmStorageQueueMessage -queue $mentionsQueue -message $queueJson
}
