# SevaSetu PowerShell Server
param(
    [int]$Port = 5000
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$dataDir = Join-Path $rootDir "backend\data"

if (!(Test-Path $dataDir)) {
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
}

$listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Any, $Port)
$listener.Start()

Write-Host "=================================================" -ForegroundColor Green
Write-Host "   SevaSetu Social Impact Network — Backend API   " -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green
Write-Host "Server running at: http://localhost:$Port/" -ForegroundColor Yellow
Write-Host "Root Directory: $rootDir" -ForegroundColor Gray
Write-Host "Press Ctrl+C to terminate server." -ForegroundColor Gray
Write-Host "-------------------------------------------------" -ForegroundColor Gray

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        $stream = $client.GetStream()
        $buffer = New-Object byte[] 16384
        $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
        if ($bytesRead -gt 0) {
            $raw = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $bytesRead)
            $firstLine = ($raw -split "`r`n")[0]
            $tokens = $firstLine -split " "
            $method = $tokens[0].ToUpper()
            $url = $tokens[1]

            $path = $url
            if ($url.Contains("?")) {
                $path = $url.Substring(0, $url.IndexOf("?"))
            }

            $body = ""
            $sep = $raw.IndexOf("`r`n`r`n")
            if ($sep -ge 0) {
                $body = $raw.Substring($sep + 4)
            }

            $now = Get-Date -Format "HH:mm:ss"
            Write-Host "[$now] $method $path" -ForegroundColor Cyan

            if ($method -eq "OPTIONS") {
                $resp = "HTTP/1.1 200 OK`r`nAccess-Control-Allow-Origin: *`r`nAccess-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS`r`nAccess-Control-Allow-Headers: Content-Type`r`nContent-Length: 0`r`nConnection: close`r`n`r`n"
                $bytes = [System.Text.Encoding]::ASCII.GetBytes($resp)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            elseif ($path -eq "/api/health") {
                $json = '{"status":"online","service":"SevaSetu API","version":"1.0.0"}'
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
                $hdr = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                $stream.Write($hbytes, 0, $hbytes.Length)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            elseif ($path -eq "/api/stats") {
                $statsFile = Join-Path $dataDir "stats.json"
                $content = if (Test-Path $statsFile) { Get-Content $statsFile -Raw } else { "{}" }
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
                $hdr = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                $stream.Write($hbytes, 0, $hbytes.Length)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            elseif ($path -eq "/api/organisations") {
                $orgsFile = Join-Path $dataDir "organisations.json"
                $content = if (Test-Path $orgsFile) { Get-Content $orgsFile -Raw } else { "[]" }
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
                $hdr = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                $stream.Write($hbytes, 0, $hbytes.Length)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            elseif ($path -eq "/api/donations" -and $method -eq "GET") {
                $donFile = Join-Path $dataDir "donations.json"
                $content = if (Test-Path $donFile) { Get-Content $donFile -Raw } else { "[]" }
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
                $hdr = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                $stream.Write($hbytes, 0, $hbytes.Length)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            elseif ($path -eq "/api/donations" -and $method -eq "POST") {
                $donFile = Join-Path $dataDir "donations.json"
                $list = if (Test-Path $donFile) { Get-Content $donFile -Raw | ConvertFrom-Json } else { @() }
                $item = $body | ConvertFrom-Json
                $rid = Get-Random -Minimum 1000 -Maximum 9999
                $trackingId = "TRK-SEVA-2026-$rid"
                $item | Add-Member -NotePropertyName "id" -NotePropertyValue $trackingId -Force
                $item | Add-Member -NotePropertyName "receiptId" -NotePropertyValue "REC-$rid" -Force
                $item | Add-Member -NotePropertyName "otp" -NotePropertyValue (Get-Random -Minimum 1000 -Maximum 9999) -Force
                $item | Add-Member -NotePropertyName "currentStage" -NotePropertyValue 1 -Force
                $item | Add-Member -NotePropertyName "status" -NotePropertyValue "Request Created" -Force
                $item | Add-Member -NotePropertyName "createdAt" -NotePropertyValue (Get-Date -Format "o") -Force
                
                $list += $item
                $list | ConvertTo-Json -Depth 10 | Set-Content $donFile -Encoding UTF8

                $respJson = @{ success = $true; trackingId = $trackingId; otp = $item.otp; donation = $item } | ConvertTo-Json
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($respJson)
                $hdr = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                $stream.Write($hbytes, 0, $hbytes.Length)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            elseif ($path -eq "/api/food-rescues") {
                $rescueFile = Join-Path $dataDir "food_rescues.json"
                if ($method -eq "POST") {
                    $list = if (Test-Path $rescueFile) { Get-Content $rescueFile -Raw | ConvertFrom-Json } else { @() }
                    $item = $body | ConvertFrom-Json
                    $rid = Get-Random -Minimum 1000 -Maximum 9999
                    $item | Add-Member -NotePropertyName "id" -NotePropertyValue "RESC-2026-$rid" -Force
                    $item | Add-Member -NotePropertyName "safetyStatus" -NotePropertyValue "Approved for Rescue" -Force
                    $item | Add-Member -NotePropertyName "status" -NotePropertyValue "Dispatched" -Force
                    $item | Add-Member -NotePropertyName "createdAt" -NotePropertyValue (Get-Date -Format "o") -Force
                    $list += $item
                    $list | ConvertTo-Json -Depth 10 | Set-Content $rescueFile -Encoding UTF8
                    $respJson = @{ success = $true; id = $item.id; rescue = $item } | ConvertTo-Json
                } else {
                    $respJson = if (Test-Path $rescueFile) { Get-Content $rescueFile -Raw } else { "[]" }
                }
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($respJson)
                $hdr = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                $stream.Write($hbytes, 0, $hbytes.Length)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            elseif ($path -match "^/api/(donations|tracking)/([^/]+)/advance$" -and $method -eq "POST") {
                $targetId = $matches[2]
                $donFile = Join-Path $dataDir "donations.json"
                $newStage = 1
                if (Test-Path $donFile) {
                    $list = Get-Content $donFile -Raw | ConvertFrom-Json
                    foreach ($d in $list) {
                        if ($d.id -like "*$targetId*" -or $d.receiptId -like "*$targetId*") {
                            $curr = [int]$d.currentStage
                            $next = ($curr % 8) + 1
                            $d.currentStage = $next
                            $newStage = $next
                            $stages = @("Request Created", "Pickup Confirmed", "Delivery Partner Assigned", "Items Collected", "On the Way", "Reached Destination", "Received by Organisation", "Donation Successfully Completed")
                            $d.status = $stages[$next - 1]
                            break
                        }
                    }
                    $list | ConvertTo-Json -Depth 10 | Set-Content $donFile -Encoding UTF8
                }
                $respJson = @{ success = $true; message = "Stage advanced"; id = $targetId; currentStage = $newStage } | ConvertTo-Json
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($respJson)
                $hdr = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                $stream.Write($hbytes, 0, $hbytes.Length)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            elseif ($path -match "^/api/(donations|tracking|proof)/([^/]+)$") {
                $targetId = $matches[2]
                $found = $null
                $donFile = Join-Path $dataDir "donations.json"
                if (Test-Path $donFile) {
                    $list = Get-Content $donFile -Raw | ConvertFrom-Json
                    foreach ($d in $list) {
                        if ($d.id -like "*$targetId*" -or $d.receiptId -like "*$targetId*") {
                            $found = $d
                            break
                        }
                    }
                }
                if ($null -eq $found) {
                    $rescueFile = Join-Path $dataDir "food_rescues.json"
                    if (Test-Path $rescueFile) {
                        $list = Get-Content $rescueFile -Raw | ConvertFrom-Json
                        foreach ($r in $list) {
                            if ($r.id -like "*$targetId*" -or $r.orderId -like "*$targetId*") {
                                $found = $r
                                break
                            }
                        }
                    }
                }

                if ($null -ne $found) {
                    $respJson = $found | ConvertTo-Json -Depth 10
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($respJson)
                    $hdr = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                    $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                    $stream.Write($hbytes, 0, $hbytes.Length)
                    $stream.Write($bytes, 0, $bytes.Length)
                } else {
                    $respJson = '{"error":"Entity not found"}'
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($respJson)
                    $hdr = "HTTP/1.1 404 Not Found`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                    $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                    $stream.Write($hbytes, 0, $hbytes.Length)
                    $stream.Write($bytes, 0, $bytes.Length)
                }
            }
            else {
                # Static file serving
                $reqFile = $path.TrimStart('/')
                if ([string]::IsNullOrWhiteSpace($reqFile)) { $reqFile = "index.html" }
                $localPath = Join-Path $rootDir ($reqFile -replace '/', '\')

                if (Test-Path $localPath -PathType Leaf) {
                    $fileBytes = [System.IO.File]::ReadAllBytes($localPath)
                    $ext = [System.IO.Path]::GetExtension($localPath).ToLower()
                    $mime = switch ($ext) {
                        ".html" { "text/html; charset=utf-8" }
                        ".css"  { "text/css; charset=utf-8" }
                        ".js"   { "application/javascript; charset=utf-8" }
                        ".json" { "application/json; charset=utf-8" }
                        ".png"  { "image/png" }
                        ".jpg"  { "image/jpeg" }
                        ".jpeg" { "image/jpeg" }
                        ".svg"  { "image/svg+xml" }
                        default { "application/octet-stream" }
                    }
                    $hdr = "HTTP/1.1 200 OK`r`nContent-Type: $mime`r`nContent-Length: $($fileBytes.Length)`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
                    $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                    $stream.Write($hbytes, 0, $hbytes.Length)
                    $stream.Write($fileBytes, 0, $fileBytes.Length)
                }
                else {
                    $notFound = "<h1>404 Not Found</h1>"
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($notFound)
                    $hdr = "HTTP/1.1 404 Not Found`r`nContent-Type: text/html`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n"
                    $hbytes = [System.Text.Encoding]::ASCII.GetBytes($hdr)
                    $stream.Write($hbytes, 0, $hbytes.Length)
                    $stream.Write($bytes, 0, $bytes.Length)
                }
            }
            $stream.Flush()
        }
        $client.Close()
    }
}
finally {
    $listener.Stop()
}
