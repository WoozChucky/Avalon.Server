# Prompt the user for the migration name
$migrationName = Read-Host -Prompt "Enter the migration name"

# Define the command
$command = "dotnet ef migrations add $migrationName --context WorldDbContext --output-dir Migrations --startup-project ../../../src/Server/Avalon.Api"

# Execute the command
Invoke-Expression $command
