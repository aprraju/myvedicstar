param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [int] $Port = 5289
)
$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
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
$start.EnvironmentVariables['FeatureFlags__UserPersistence'] = 'false'
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
    $document = Invoke-WebRequest -Uri "$baseUrl/openapi/v1.json" -UseBasicParsing -TimeoutSec 30
    $paths = ($document.Content | ConvertFrom-Json).paths.PSObject.Properties.Name
    if ($paths -notcontains '/api/v1/auth/userinfo' -or $paths -notcontains '/api/v1/muhurta/activities') { throw 'Published host is missing an API route.' }
    foreach ($path in '/api/v1/locations/search', '/api/v1/locations/postal', '/api/v1/locations/reverse', '/api/v1/locations/timezone') {
        if ($paths -notcontains $path) { throw "Published API is missing $path" }
    }
    foreach ($request in @(@{ Path = '/api/v1/auth/userinfo'; Method = 'POST' }, @{ Path = '/api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867'; Method = 'GET' })) {
        $status = 0
        try { $status = [int](Invoke-WebRequest -Uri ($baseUrl + $request.Path) -Method $request.Method -UseBasicParsing -TimeoutSec 10).StatusCode }
        catch { if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode } else { throw } }
        if ($status -ne 401) { throw "Published protected route $($request.Path) returned $status instead of 401." }
    }
    Write-Host 'Published API smoke checks passed: unified assembly, preserved routes, readiness and shared authentication.'
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
