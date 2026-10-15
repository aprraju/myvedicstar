param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [int] $Port = 5289,
    [switch] $EnableUserPersistence
)
$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
# Never validate an unrelated local API that is already listening on the requested port.
$portProbe = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $Port)
try { $portProbe.Start() }
catch { throw "Smoke-test port $Port is already in use. Select an unused port with -Port." }
finally { $portProbe.Stop() }
foreach ($file in 'MyVedicStar.API.dll', 'MyVedicDay.Api.dll', 'GeoNames.dll', 'apis/day/appsettings.json', 'apis/star/appsettings.json') {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $file))) { throw "Combined publish is missing $file" }
}
foreach ($file in 'appsettings.Development.json', 'apis/day/appsettings.Development.json', 'apis/star/appsettings.Development.json', 'MyVedicStar.Authorize.Api.dll', 'MyVedicStar.Host.dll') {
    if (Test-Path -LiteralPath (Join-Path $publishPath $file)) { throw "Development settings were published: $file" }
}
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = 'dotnet'
$start.Arguments = 'MyVedicStar.API.dll --urls http://127.0.0.1:' + $Port + ' --TransportSecurity:RedirectHttpToHttps false'
$start.WorkingDirectory = $publishPath
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Production'
$start.EnvironmentVariables['Caching__L2__Enabled'] = 'false'
$start.EnvironmentVariables['EmailSettings__Enabled'] = 'false'
$start.EnvironmentVariables['FeatureFlags__UserPersistence'] = $EnableUserPersistence.IsPresent.ToString().ToLowerInvariant()
if ($EnableUserPersistence) {
    if ([string]::IsNullOrWhiteSpace($env:MYVEDICSTAR_TEST_POSTGRES)) { throw 'Persistence smoke requires MYVEDICSTAR_TEST_POSTGRES.' }
    $start.EnvironmentVariables['Database__UseManagedIdentity'] = 'false'
    $start.EnvironmentVariables['Database__ConnectionString'] = $env:MYVEDICSTAR_TEST_POSTGRES
    $start.EnvironmentVariables['Database__ApplyMigrationsOnStartup'] = 'false'
}
$start.EnvironmentVariables['APPLICATIONINSIGHTS_CONNECTION_STRING'] = ''
$start.EnvironmentVariables['Logging__EventLog__LogLevel__Default'] = 'None'
$process = New-Object System.Diagnostics.Process
$process.StartInfo = $start
try {
    if (-not $process.Start()) { throw 'Published host did not start.' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $baseUrl = "http://127.0.0.1:$Port"
    $ready = $false
    for ($attempt = 0; $attempt -lt 30 -and -not $ready; $attempt++) {
        if ($process.HasExited) { throw "Published host exited early: $($stderrTask.GetAwaiter().GetResult())" }
        try { $ready = (Invoke-WebRequest -Uri "$baseUrl/health/ready" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw 'Published host did not become ready.' }
    if ((Invoke-WebRequest -Uri "$baseUrl/health/live" -UseBasicParsing -TimeoutSec 5).StatusCode -ne 200) { throw 'Published host liveness check failed.' }
    foreach ($request in @(@{ Path = '/openapi/v1.json'; Method = 'GET' }, @{ Path = '/swagger/index.html'; Method = 'GET' }, @{ Path = '/api/v1/panchang/today'; Method = 'GET' }, @{ Path = '/api/v1/locations/resolve'; Method = 'GET' }, @{ Path = '/api/v1/locations/search?text=Austin'; Method = 'GET' }, @{ Path = '/api/v1/locations/postal?text=78701'; Method = 'GET' }, @{ Path = '/api/v1/locations/reverse?latitude=30&longitude=-97'; Method = 'GET' }, @{ Path = '/api/v1/locations/timezone?latitude=30&longitude=-97'; Method = 'GET' }, @{ Path = '/api/v1/locations/countries'; Method = 'GET' }, @{ Path = '/api/v1/locations/regions?country=US'; Method = 'GET' }, @{ Path = '/api/v1/locations/address-cities?text=Austin&country=US'; Method = 'GET' }, @{ Path = '/api/v1/settings'; Method = 'GET' }, @{ Path = '/api/v1/settings/locations'; Method = 'GET' }, @{ Path = '/api/v1/auth/profile'; Method = 'GET' }, @{ Path = '/api/v1/auth/userinfo'; Method = 'POST' }, @{ Path = '/api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867'; Method = 'GET' })) {
        $status = 0
        try { $status = [int](Invoke-WebRequest -Uri ($baseUrl + $request.Path) -Method $request.Method -UseBasicParsing -TimeoutSec 10).StatusCode }
        catch { if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode } else { throw } }
        if ($status -ne 401) { throw "Published protected route $($request.Path) returned $status instead of 401." }
    }
    # Public email must route without a token; disabled SMTP prevents real messages during smoke checks.
    $emailStatus = 0
    try {
        $emailStatus = [int](Invoke-WebRequest -Uri ($baseUrl + '/api/v1/email/send') -Method POST -ContentType 'application/json' -Body '{"email":"smoke@example.com","subject":"Smoke check","message":"No message should be sent.","type":"ContactUs","reason":"smoke"}' -UseBasicParsing -TimeoutSec 10).StatusCode
    }
    catch { if ($_.Exception.Response) { $emailStatus = [int]$_.Exception.Response.StatusCode } else { throw } }
    if ($emailStatus -ne 503) { throw "Published public email route returned $emailStatus instead of disabled-delivery 503." }
    Write-Host 'Published API smoke checks passed: unified assembly, preserved routes, readiness, shared authentication and public email routing.'
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
