# Prompt the user for the migration name
$migrationName = Read-Host -Prompt "Enter the migration name"

# Define the command
$command = "dotnet ef migrations add $migrationName --context AuthDbContext --output-dir Migrations --startup-project ../../../Tools/Avalon.Tools.Migrations -- --Database:Auth:ConnectionString ""Server=localhost;Database=auth;Uid=root;Pwd=123;ConvertZeroDatetime=True;AllowZeroDateTime=True;"""

# Execute the command
Invoke-Expression $command
