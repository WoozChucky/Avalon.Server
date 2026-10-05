$ErrorActionPreference = 'Stop'
$chartPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$authValues = Join-Path $PSScriptRoot 'authentication-values.yaml'
$emailValues = Join-Path $PSScriptRoot 'email-values.yaml'
function Render([string[]] $Extra, [string] $ExpectedFailure = '') {
    $renderArguments = @('template', 'email-check', $chartPath, '-f', $authValues,
        '--set', 'existingSecret=test-api-credentials', '--set', 'worlds.1.worldKey=database-world-1-connection-string', '--set', 'cache.host=redis:6379') + $Extra
    $output = (& helm @renderArguments 2>&1 | Out-String)
    $renderExit = $LASTEXITCODE
    if ($ExpectedFailure) {
        if ($renderExit -eq 0 -or !$output.Contains($ExpectedFailure)) { throw "Expected rejection: $ExpectedFailure" }
    } elseif ($renderExit -ne 0) { throw "Helm render failed: $output" }
    return $output
}
function Assert-Rendered([bool] $Condition, [string] $Message) { if (!$Condition) { throw $Message } }
$defaults = Render -Extra @()
Assert-Rendered ($defaults -match 'name: Application__Email__Sender\s+value: "None"') 'Default sender must be None.'
Assert-Rendered (!$defaults.Contains('Application__Email__ResendApiKey')) 'None must need no email key.'
$enabled = Render -Extra @('-f', $emailValues)
foreach ($setting in @('Sender','From','FromName','VerificationSiteOrigin','VerificationCooldownSeconds','MaxVerificationSendsPerAccount','MaxVerificationSendsPerSource')) {
    Assert-Rendered ($enabled.Contains("name: Application__Email__$setting")) "Missing email variable: $setting"
}
Assert-Rendered ($enabled -match 'name: Application__Email__ResendApiKey\s+valueFrom:\s+secretKeyRef:\s+name: "test-email-credentials"\s+key: "resend-api-key"') 'Email key must use the selected secretKeyRef.'
Assert-Rendered (!($enabled -match 'name: Application__Email__ResendApiKey\s+value:')) 'Email key must never be a value literal.'
foreach ($setting in @('from','verificationSiteOrigin','existingSecret','resendApiKeyKey')) {
    $null = Render -Extra @('-f', $emailValues, '--set-string', "email.$setting=") -ExpectedFailure "email.$setting"
}
$null = Render -Extra @('-f', $emailValues, '--set-string', 'email.verificationSiteOrigin=https://example.test/path') -ExpectedFailure 'email.verificationSiteOrigin'
$null = Render -Extra @('-f', $emailValues, '--set-string', 'email.sender=Unknown') -ExpectedFailure 'email.sender'
$null = Render -Extra @('-f', $emailValues, '--set-string', 'email.resendApiKey=forbidden-test-literal') -ExpectedFailure 'email.resendApiKey'
foreach ($setting in @('verificationCooldownSeconds','maxVerificationSendsPerAccount','maxVerificationSendsPerSource')) {
    $null = Render -Extra @('-f', $emailValues, '--set-string', "email.$setting=0") -ExpectedFailure "email.$setting"
}
Write-Output 'Email chart rendering checks passed (default-off, Resend secret reference, invalid settings).'
