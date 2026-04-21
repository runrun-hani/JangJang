# publish.ps1 — 빌드 + 압축 + 배포 스크립트
# 사용법: ./publish.ps1

$ProjectPath = "src/JangJang/JangJang.csproj"
$DistPath    = "dist"
$DeployPath  = "H:\내 드라이브\배포용"

# csproj에서 Version 읽기 (AssemblyName 미설정 시 기본값 사용)
[xml]$csproj  = Get-Content $ProjectPath
$AssemblyName = $csproj.Project.PropertyGroup.AssemblyName
if (-not $AssemblyName) { $AssemblyName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath) }
$Version      = $csproj.Project.PropertyGroup.Version

$ExeName  = "$AssemblyName.exe"
$ZipName  = "$AssemblyName.zip"

Write-Host "▶ 빌드 시작: $AssemblyName (v$Version)" -ForegroundColor Cyan

# 1. dotnet publish
dotnet publish $ProjectPath -c Release -r win-x64 --self-contained -o $DistPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ 빌드 실패" -ForegroundColor Red
    exit 1
}

# 2. exe 존재 확인
$ExePath = Join-Path $DistPath $ExeName
if (-not (Test-Path $ExePath)) {
    Write-Host "✗ exe 파일을 찾을 수 없습니다: $ExePath" -ForegroundColor Red
    exit 1
}

# 3. zip으로 압축
$ZipPath = Join-Path $DistPath $ZipName
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path $ExePath -DestinationPath $ZipPath
Write-Host "✓ 압축 완료: $ZipPath" -ForegroundColor Green

# 4. 배포 폴더에 복사
# 기존 파일에 덮어쓰기 후 이름 변경 → Google Drive 공유 링크 유지
if (-not (Test-Path $DeployPath)) { New-Item -ItemType Directory -Path $DeployPath | Out-Null }
$Existing = Get-ChildItem $DeployPath -Filter "$AssemblyName*.zip" | Select-Object -First 1
if ($Existing) {
    Copy-Item $ZipPath $Existing.FullName -Force          # 기존 파일에 내용 덮어쓰기 (파일 ID 유지)
    if ($Existing.Name -ne $ZipName) {
        Rename-Item $Existing.FullName -NewName $ZipName  # 버전명이 바뀐 경우 파일명 변경
    }
    $DeployTarget = Join-Path $DeployPath $ZipName
} else {
    $DeployTarget = Join-Path $DeployPath $ZipName
    Copy-Item $ZipPath $DeployTarget -Force               # 기존 파일 없으면 새로 복사
}
Write-Host "✓ 배포 완료: $DeployTarget" -ForegroundColor Green

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host "  배포 파일: $DeployTarget" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
