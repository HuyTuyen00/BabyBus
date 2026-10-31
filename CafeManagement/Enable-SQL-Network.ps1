# PowerShell script to enable TCP/IP protocol for SQL Server 2022 (MSSQLSERVER)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  BAT GIAO THUC TCP/IP CUA SQL SERVER DE MAY PHU KET NOI " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "WARNING: Dang tu dong khoi chay lai duoi quyen Administrator..." -ForegroundColor Yellow
    Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

try {
    Write-Host "[1/3] Dang bat giao thuc TCP/IP cho SQL Server..." -ForegroundColor Green
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQLServer\SuperSocketNetLib\Tcp' -Name 'Enabled' -Value 1 -ErrorAction SilentlyContinue

    Write-Host "[2/3] Dang cau hinh Cong TCP 1433..." -ForegroundColor Green
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQLServer\SuperSocketNetLib\Tcp\IPAll' -Name 'TcpPort' -Value '1433' -ErrorAction SilentlyContinue
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQLServer\SuperSocketNetLib\Tcp\IPAll' -Name 'TcpDynamicPorts' -Value '' -ErrorAction SilentlyContinue

    Write-Host "[3/3] Dang khoi dong lai dich vu SQL Server (MSSQLSERVER)..." -ForegroundColor Green
    Restart-Service MSSQLSERVER -Force

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " DA BAT TCP/IP VA CONG 1433 CHO SQL SERVER THANH CONG!" -ForegroundColor Green
    Write-Host " Gio day May 2 co the ket noi den SQL Server tren May 1" -ForegroundColor Yellow
    Write-Host " ngay ca khi khong mo phan mem CafeManagement tren May 1!" -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Green
}
catch {
    Write-Host "Loi: $_" -ForegroundColor Red
}

Read-Host "Bam Enter de thoat..."
