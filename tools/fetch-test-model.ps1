#Requires -Version 5.1
<#
.SYNOPSIS
    자캐 페르소나 시스템 통합 테스트(Tier 2)용 임베딩 모델 다운로드 스크립트.

.DESCRIPTION
    Hugging Face에서 multilingual-e5-small ONNX 모델과 SentencePiece 토크나이저를
    다운로드하여 표준 위치 (%AppData%\JangJang\Models\multilingual-e5-small\) 에 배치한다.

    첫 실행 시 약 120-150MB를 다운로드한다. 이후 파일이 이미 존재하면 건너뛴다.

    배포 소스: Xenova/multilingual-e5-small (ONNX 전용 포트)
    - onnx/model.onnx         — ONNX 모델 파일
    - sentencepiece.bpe.model — XLM-RoBERTa SentencePiece 토크나이저

.EXAMPLE
    .\tools\fetch-test-model.ps1
    표준 위치에 모델 다운로드

.EXAMPLE
    .\tools\fetch-test-model.ps1 -Force
    파일이 이미 있어도 재다운로드

.NOTES
    다운로드 URL이 변경되거나 404인 경우 $ModelUrl / $TokenizerUrl 변수를 수정.
    대안 repo: intfloat/multilingual-e5-small (원본, 하위 폴더 구조 상이)
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# 다운로드 소스 URL — 필요 시 수정 가능
$ModelUrl     = 'https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model.onnx'
$TokenizerUrl = 'https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/sentencepiece.bpe.model'

# 표준 배치 위치
$AppData   = [Environment]::GetFolderPath('ApplicationData')
$TargetDir = Join-Path $AppData 'JangJang\Models\multilingual-e5-small'

$ModelPath     = Join-Path $TargetDir 'model.onnx'
$TokenizerPath = Join-Path $TargetDir 'sentencepiece.bpe.model'

Write-Host "자캐 임베딩 모델 다운로드 스크립트" -ForegroundColor Cyan
Write-Host "대상 폴더: $TargetDir"
Write-Host ""

if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Write-Host "폴더 생성: $TargetDir" -ForegroundColor Green
}

function Get-FileIfMissing {
    param(
        [string]$Url,
        [string]$Destination,
        [string]$Description
    )

    if ((Test-Path $Destination) -and -not $Force) {
        $sizeMB = [math]::Round((Get-Item $Destination).Length / 1MB, 1)
        Write-Host "[스킵] $Description 이미 존재 ($sizeMB MB)" -ForegroundColor Yellow
        return
    }

    Write-Host "[다운로드] $Description" -ForegroundColor Cyan
    Write-Host "  From: $Url"
    Write-Host "  To:   $Destination"

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        # BITS는 Windows PowerShell 5.1 내장. WebClient/Invoke-WebRequest보다 진행 표시 좋음.
        # 실패 시 Invoke-WebRequest 폴백.
        if (Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue) {
            Start-BitsTransfer -Source $Url -Destination $Destination -DisplayName $Description
        } else {
            Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
        }
    }
    catch {
        Write-Host "[실패] $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "URL이 변경됐을 수 있어요. 스크립트 상단의 `$ModelUrl / `$TokenizerUrl을 확인해주세요." -ForegroundColor Yellow
        throw
    }

    $sw.Stop()
    $sizeMB = [math]::Round((Get-Item $Destination).Length / 1MB, 1)
    Write-Host "[완료] $sizeMB MB ($([math]::Round($sw.Elapsed.TotalSeconds, 1))초)" -ForegroundColor Green
}

Get-FileIfMissing -Url $ModelUrl     -Destination $ModelPath     -Description 'model.onnx'
Get-FileIfMissing -Url $TokenizerUrl -Destination $TokenizerPath -Description 'sentencepiece.bpe.model'

Write-Host ""
Write-Host "모든 파일 준비 완료." -ForegroundColor Green
Write-Host "이제 'dotnet test src/JangJang.Tests' 실행 시 Tier 2 통합 테스트도 돌아갑니다."
