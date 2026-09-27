# Prompt the user for the migration name
$migrationName = Read-Host -Prompt "Enter the migration name"

# migrations add only builds the model and never connects, but the design-time factory refuses
# without a connection string (#523). When none is set, lend it a placeholder that points nowhere
# for this one command, and take it back afterwards.
$hadConnection = Test-Path Env:Database__World__ConnectionString
if (-not $hadConnection) { $env:Database__World__ConnectionString = "Host=127.0.0.1;Port=1;Database=design_time_only" }

$command = "dotnet ef migrations add $migrationName --context WorldDbContext --output-dir Migrations --startup-project ../../../src/Server/Avalon.Api"

try { Invoke-Expression $command }
finally { if (-not $hadConnection) { Remove-Item Env:Database__World__ConnectionString } }
