param(
    [string]$ResultsDirectory
)

$ErrorActionPreference = "Stop"
$repository = Split-Path $PSScriptRoot -Parent
$ownedResults = [string]::IsNullOrWhiteSpace($ResultsDirectory)
if ($ownedResults) {
    $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ("srvsurvey-upload-scenarios-" + [guid]::NewGuid().ToString("N"))
}

try {
    if ($ownedResults) {
        foreach ($project in @("SrvSurvey.Core.Tests", "SrvSurvey.Desktop.Tests")) {
            & dotnet test (Join-Path $repository "tests/$project/$project.csproj") `
                --configuration Release --no-restore `
                --filter "Category=JournalUploadScenario" `
                --logger trx --results-directory $ResultsDirectory
            if ($LASTEXITCODE -ne 0) {
                throw "Journal upload scenarios failed for $project."
            }
        }
    }

    $results = @(
        foreach ($report in Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse) {
            [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
            $document.SelectNodes("//*[local-name()='UnitTestResult']")
        }
    )
    if ($results.Count -eq 0) {
        throw "No executed journal upload scenario results were found."
    }

    $required = [System.Collections.Generic.List[string]]::new()
    foreach ($provider in @("EDDN", "Inara", "EDSM", "EDAstro", "VoxStellar")) {
        foreach ($scenario in @(
            "FailedLiveEvidenceSurvivesRestart",
            "CapturedEvidenceSurvivesUnexpectedProcessTermination",
            "AcknowledgedSourceRereadIsNotPublishedAgain"
        )) {
            $required.Add("*.JournalUploadScenarioTests.$scenario*provider: `"$provider`"*")
        }
    }
    foreach ($provider in @("EDDN", "Inara", "EDSM", "EDAstro", "VoxStellar")) {
        foreach ($sharedFolder in @("False", "True")) {
            $required.Add("*.MultiCommanderJournalUploadTests.InterleavedCommandersUseOnlyTheirOwnJournalAndCompanions*provider: `"$provider`"*sharedFolder: $sharedFolder*")
        }
        $required.Add("*.MultiCommanderJournalUploadTests.TwoCommanderRetainedUploadsStayWithTheirOwnersAfterRestart*provider: `"$provider`"*")
    }
    foreach ($provider in @("EDAstro", "VoxStellar")) {
        $required.Add("*.JournalUploadScenarioTests.CaptureStorageFailureDoesNotStopJournalProcessing*provider: `"$provider`"*")
    }
    foreach ($provider in @("Inara", "EDSM")) {
        $required.Add("*.JournalUploadScenarioTests.FailedLiveEvidenceSurvivesRestart*provider: `"$provider`"*missingCredentials: True*")
        $required.Add("*.JournalUploadScenarioTests.ExplicitCredentialRemovalRetiresReportsAfterTemporaryCredentialFailure*provider: `"$provider`"*")
        $required.Add("*.JournalUploadScenarioTests.ForeignCommanderBoundaryCannotRelabelFollowingEvents*provider: `"$provider`"*")
    }
    foreach ($optedOut in @("False", "True")) {
        $required.Add("*.JournalUploadScenarioTests.LegacyEddnQueueDrainsWithoutRelabelingTheCommander*otherProfileOptedOut: $optedOut*")
    }
    foreach ($suspended in @("False", "True")) {
        $required.Add("*.JournalUploadScenarioTests.SelectedEddnAccountRecoversWithoutJournalActivity*suspended: $suspended*")
    }
    foreach ($capacity in @(1, 2)) {
        $required.Add("*.EddnOutboxTests.ReceiptCompactionLeavesSlackForSubsequentCompletions*capacityMultiple: $capacity*")
    }
    foreach ($scenario in @(
        "InaraPublisherTests.EventLevelTransientStatusIsRetriedAndRedirectIsRejected",
        "InaraPublisherTests.RetryAfterExtendsAutomaticRetryWindow",
        "InaraPublisherTests.SessionSwitchUsesOnlyTheMatchingCommanderAndApiKey",
        "InaraPublisherTests.CargoEnrichmentRequiresAFreshSnapshot",
        "InaraPublisherTests.LockerEnrichmentRequiresMatchingFreshEvidence",
        "VoxStellarPublisherTests.ForeignCommanderStopsTheCapturedBatch",
        "EdsmPublisherTests.ApiStatus402RetriesOnlyUnknownCatalogEvent",
        "EdsmPublisherTests.InvalidDiscardListFailsClosedAndUsesBoundedRetryCadence",
        "EdAstroPublisherTests.WireEnvelopeMatchesEdDiscoveryWithDisclosedPublicFieldsAndNoCredentials",
        "EdAstroPublisherTests.ConsentRevocationCancelsActiveDeliveryWhilePausePreservesQueuedData",
        "JournalUploadScenarioTests.SuccessfulWebhookHeadersRetireReportEvenIfUnusedBodyIsBroken",
        "JournalUploadScenarioTests.EddnRoutingRequiresIdentityAndStopsAfterDisposal",
        "JournalUploadOutboxTests.TornAndMalformedRecordsDoNotDiscardOtherUploads",
        "JournalUploadOutboxTests.FailedAcknowledgementCheckpointDoesNotRequeueAcceptedPayloadInMemory",
        "JournalUploadOutboxTests.RevocationInvalidatesOtherLeasesAndUnselectedOwners",
        "JournalUploadOutboxTests.CompactionPreservesReceiptEvictionOrderAcrossRestart",
        "ColonizationDeliveryRecoveryTests.LostCreditAcknowledgementRequiresVerificationAndPreservesItsRequirementTarget",
        "ColonizationDeliveryRecoveryTests.RecoveryMustBeDurableBeforeCreditCanBeSent",
        "ColonizationDeliveryRecoveryTests.LostAcknowledgedPhaseSaveCannotDoubleCreditAfterRestart",
        "RavenColonialClientTests.AcknowledgedContributionDoesNotWaitForResponseBody",
        "CanonnHumanSiteClientTests.PublishStationUsesLegacyPayloadAndEndpointContract",
        "CanonnHumanSiteClientTests.PublishStationRejectsIncompleteDataBeforeSending"
    )) {
        $required.Add("*.$scenario*")
    }

    foreach ($pattern in $required) {
        $matching = @($results | Where-Object { $_.testName -like $pattern })
        if ($matching.Count -eq 0 -or @($matching | Where-Object { $_.outcome -ne "Passed" }).Count -gt 0) {
            throw "A mandatory journal upload scenario is missing, skipped, or failed: $pattern"
        }
    }
    Write-Output "Journal upload scenario matrix passed: $($required.Count) mandatory cases, including crash recovery for all five journal senders."
}
finally {
    if ($ownedResults -and (Test-Path -LiteralPath $ResultsDirectory)) {
        Remove-Item -LiteralPath $ResultsDirectory -Recurse -Force
    }
}
