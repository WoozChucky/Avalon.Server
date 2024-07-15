# Prompt the user for the migration name
$migrationName = Read-Host -Prompt "Enter the migration name"

# Define the command
$command = "dotnet ef migrations add $migrationName --context WorldDbContext --output-dir Migrations --startup-project ../Avalon.Api -- --Database:World:ConnectionString ""Server=localhost;Database=world;Uid=root;Pwd=123;ConvertZeroDatetime=True;AllowZeroDateTime=True;"""

# Execute the command
Invoke-Expression $command
