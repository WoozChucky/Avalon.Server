# Prompt the user for the migration name
$migrationName = Read-Host -Prompt "Enter the migration name"

# Define the command
$command = "dotnet ef migrations add $migrationName --context CharacterDbContext --output-dir Migrations --startup-project ../../../Tools/Avalon.Tools.Migrations -- --Database:Characters:ConnectionString ""Server=localhost;Database=characters;Uid=root;Pwd=123;ConvertZeroDatetime=True;AllowZeroDateTime=True;"""

# Execute the command
Invoke-Expression $command
