param([switch]$ServeStripe)
$ErrorActionPreference='Stop'
$taskId=[Guid]::NewGuid().ToString('N')
$containers=@()
$taskEnvFile=Join-Path ([IO.Path]::GetTempPath()) ('avalon-commerce-'+$taskId+'.env')
$randomBytes=New-Object byte[] 32
$randomGenerator=[Security.Cryptography.RandomNumberGenerator]::Create()
$randomGenerator.GetBytes($randomBytes)
$randomGenerator.Dispose()
$password=-join ($randomBytes | ForEach-Object { $_.ToString('x2') })
try {
  [IO.File]::WriteAllText($taskEnvFile,'POSTGRES_PASSWORD='+$password)
  $postgres=(& docker run --detach --name ('avalon-commerce-postgres-'+$taskId) --label avalon.purpose=commerce-check --env-file $taskEnvFile -p '127.0.0.1::5432' postgres:17-alpine).Trim()
  if ($LASTEXITCODE -ne 0) { throw 'Disposable PostgreSQL creation failed.' }; $containers+=$postgres
  Remove-Item -LiteralPath $taskEnvFile
  $redis=(& docker run --detach --name ('avalon-commerce-redis-'+$taskId) --label avalon.purpose=commerce-check -p '127.0.0.1::6379' redis:7-alpine).Trim()
  if ($LASTEXITCODE -ne 0) { throw 'Disposable Redis creation failed.' }; $containers+=$redis
  $ready=$false
  for ($i=0;$i -lt 60;$i++) { & docker exec $postgres pg_isready -q > $null; if ($LASTEXITCODE -eq 0) { $ready=$true; break }; Start-Sleep -Milliseconds 500 }
  if (!$ready) { throw 'Disposable PostgreSQL did not become ready.' }
  $postgresPort=(& docker port $postgres '5432/tcp').Trim().Split(':')[-1]
  $redisPort=(& docker port $redis '6379/tcp').Trim().Split(':')[-1]
  $env:AVALON_COMMERCE_TEST_POSTGRES="Host=127.0.0.1;Port=$postgresPort;Database=avalon_commerce_test_auth;Username=postgres;Password=$password"
  $env:AVALON_COMMERCE_TEST_REDIS="127.0.0.1:$redisPort"
  $checkProject=Join-Path $PSScriptRoot 'Avalon.Commerce.Check.csproj'
  if ($ServeStripe) { & dotnet run --project $checkProject -- --serve-stripe } else { & dotnet run --project $checkProject }
  $checkExit=$LASTEXITCODE
} catch { Write-Error 'Disposable commerce checks failed. No provider responses or credentials are printed.' -ErrorAction Continue; $checkExit=1 }
finally {
  Remove-Item Env:AVALON_COMMERCE_TEST_POSTGRES,Env:AVALON_COMMERCE_TEST_REDIS -ErrorAction SilentlyContinue
  if (Test-Path -LiteralPath $taskEnvFile) { Remove-Item -LiteralPath $taskEnvFile }
  foreach ($id in $containers) {
    $labels=(& docker inspect --format '{{json .Config.Labels}}' $id 2>$null) | ConvertFrom-Json
    if ($labels.'avalon.purpose' -eq 'commerce-check') { & docker rm --force $id >$null }
  }
}
exit $checkExit
