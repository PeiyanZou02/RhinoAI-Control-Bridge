param(
    [string]$ComfyRoot = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) 'ComfyUI'),
    [switch]$ImmediateFrontend,
    [string]$Server = 'http://127.0.0.1:8000'
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'comfyui\rhino_ai_live'
$target = Join-Path ([IO.Path]::GetFullPath($ComfyRoot)) 'custom_nodes\rhino_ai_live'
New-Item -ItemType Directory -Force -Path (Join-Path $target 'web') | Out-Null
Copy-Item -LiteralPath (Join-Path $source '__init__.py') -Destination (Join-Path $target '__init__.py') -Force
Copy-Item -LiteralPath (Join-Path $source 'web\live.js') -Destination (Join-Path $target 'web\live.js') -Force
Copy-Item -LiteralPath (Join-Path $source 'web\sync-core.mjs') -Destination (Join-Path $target 'web\sync-core.mjs') -Force
Copy-Item -LiteralPath (Join-Path $source 'web\main.mjs') -Destination (Join-Path $target 'web\main.mjs') -Force
Write-Output "Installed: $target"
if ($ImmediateFrontend) {
    $frontend = Join-Path $ComfyRoot '.venv\Lib\site-packages\comfyui_frontend_package\static'
    $index = Join-Path $frontend 'index.html'
    # Invoke-WebRequest can throw a NullReferenceException inside the ComfyUI
    # Desktop host even when the server is healthy. HttpClient gives us the
    # raw HTML needed for the same active-frontend safety check.
    Add-Type -AssemblyName System.Net.Http
    $http = [Net.Http.HttpClient]::new()
    try {
        $http.Timeout = [TimeSpan]::FromSeconds(10)
        $remote = $http.GetStringAsync($Server.TrimEnd('/') + '/').GetAwaiter().GetResult()
    } finally { $http.Dispose() }
    if (-not (Test-Path -LiteralPath $index) -or [IO.File]::ReadAllText($index).Trim() -ne $remote.Trim()) { throw 'Local frontend does not match the active server; immediate install stopped.' }
    # ComfyUI discovers web-root extensions on each /extensions request. No server restart needed.
    $liveTarget = Join-Path $frontend 'extensions\rhino_ai_live'
    New-Item -ItemType Directory -Force -Path $liveTarget | Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'web\live.js') -Destination (Join-Path $liveTarget 'live.js') -Force
    Copy-Item -LiteralPath (Join-Path $source 'web\sync-core.mjs') -Destination (Join-Path $liveTarget 'sync-core.mjs') -Force
    Copy-Item -LiteralPath (Join-Path $source 'web\main.mjs') -Destination (Join-Path $liveTarget 'main.mjs') -Force
    Write-Output "Active frontend extension installed: $liveTarget"
    Write-Output 'Save the workflow, then focus the ComfyUI canvas and press F5 (also supported by Comfy Desktop). Verify the Rhino status button and frontend acknowledgement; HTTP 200 alone does not confirm activation.'
} else { Write-Output 'Restart ComfyUI and refresh its page once.' }
