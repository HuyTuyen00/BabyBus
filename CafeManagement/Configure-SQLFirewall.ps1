# PowerShell script to open Windows Firewall ports for SQL Server and CafeManagement WebServer

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "    CAFE MANAGEMENT - CAU HINH FIREWALL CHO MANG LAN/WIFI " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "WARNING: Ban can chay PowerShell voi quyen Administrator!" -ForegroundColor Red
}

try {
    # 1. Open SQL Server Port 1433
    Write-Host "[1/2] Dang mo cong SQL Server (TCP Port 1433)..." -ForegroundColor Green
    Remove-NetFirewallRule -DisplayName "SQL Server TCP 1433 (CafeManagement)" -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName "SQL Server TCP 1433 (CafeManagement)" `
                        -Direction Inbound `
                        -Protocol TCP `
                        -LocalPort 1433 `
                        -Action Allow `
                        -Profile Any `
                        -Description "Cho phep ket noi CSDL SQL Server tu may phu qua Wi-Fi" | Out-Null
    Write-Host "  => Thanh cong: Da mo cong 1433!" -ForegroundColor Cyan

    # 2. Open WebServer Port 5000
    Write-Host "[2/2] Dang mo cong WebServer Bep (TCP Port 5000)..." -ForegroundColor Green
    Remove-NetFirewallRule -DisplayName "CafeManagement WebServer TCP 5000" -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName "CafeManagement WebServer TCP 5000" `
                        -Direction Inbound `
                        -Protocol TCP `
                        -LocalPort 5000 `
                        -Action Allow `
                        -Profile Any `
                        -Description "Cho phep man hinh Bep / May phu truy cap WebServer" | Out-Null
    Write-Host "  => Thanh cong: Da mo cong 5000!" -ForegroundColor Cyan

    # Get local IPv4 address
    $ipList = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -like "192.168.*" -or $_.IPAddress -like "10.*" -or $_.IPAddress -like "172.*" }
    $localIp = ($ipList | Select-Object -First 1).IPAddress

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " CAU HINH FIREWALL THANH CONG!" -ForegroundColor Green
    Write-Host " IP May nay tren Wi-Fi: $localIp" -ForegroundColor Yellow
    Write-Host " Nhap IP nay vao man hinh Cai dat tren Laptop phu de ket noi." -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Green
}
catch {
    Write-Host "Loi cau hinh Firewall: $_" -ForegroundColor Red
}
