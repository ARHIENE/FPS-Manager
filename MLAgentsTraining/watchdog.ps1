# unity-cli 브릿지는 Play 모드로 무거운 5v5 시뮬레이션이 돌 때 자주(수분 단위로) 일시적으로 응답이
# 느려지는데, 이건 정상 범위이고 학습(mlagents-learn, 포트 5004) 자체는 멀쩡히 계속 진행됨(실측 확인).
# 예전 버전은 "브릿지 핑 실패 N회"를 기준으로 삼아서 이 정상적인 지연을 "죽었다"고 오판, 5~6분마다
# 불필요하게 Unity를 강제 종료해 학습을 오히려 방해하고, 재시작할 때마다 "씬 백업 복구" 팝업까지
# 띄워서 사람이 매번 클릭해줘야 하는 악순환을 만들었음(실측 확인, 2026-08-31).
# 그래서 기준을 "학습 로그(stdout)가 실제로 멈췄는지"로 바꾼다 - 이게 진짜로 훈련이 죽었는지 보여주는
# 신호이고, 브릿지가 잠깐 느린 것과는 무관하다.
# 사용법: powershell -File watchdog.ps1 -RunId combat_v6
param(
    [string]$RunId = "",
    [string]$ProjectPath = "E:\Git\Fps Manager",
    [string]$UnityExe = "E:\unity\6000.5.8f1\Editor\Unity.exe",
    [string]$UnityCliExe = "$env:USERPROFILE\.local\bin\unity-cli.exe",
    [int]$UnityCliPort = 16400,
    [int]$CheckIntervalSec = 30,
    [int]$PingTimeoutMs = 25000,
    [int]$StaleThresholdSec = 240,
    [int]$RecoveryTimeoutSec = 240,
    [int]$MaxSteps = 4000000
)

$env:UNITY_CLI_PORT = "$UnityCliPort"
$env:UNITY_PROJECT_ROOT = $ProjectPath
$trainerDir = Join-Path $ProjectPath "MLAgentsTraining"
$logFile = Join-Path $trainerDir "watchdog.log"
$stdoutLog = Join-Path $trainerDir "${RunId}_stdout.log"

function Write-Log($msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg
    Write-Host $line
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

function Test-BridgeAlive {
    # System.Diagnostics.Process를 CreateNoWindow로 직접 써서 하드 타임아웃으로 강제 종료 -
    # Start-Process -NoNewWindow는 콘솔 없는 부모(hidden watchdog) 밑에서 행에 걸리는 게 실측으로 확인됨.
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $UnityCliExe
        $psi.Arguments = "system ping --timeout-ms $PingTimeoutMs"
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true

        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $psi
        $proc.Start() | Out-Null
        $proc.BeginOutputReadLine()
        $proc.BeginErrorReadLine()

        $exited = $proc.WaitForExit($PingTimeoutMs + 3000)
        if (-not $exited) {
            try { $proc.Kill() } catch {}
            return $false
        }
        return $proc.ExitCode -eq 0
    } catch {
        return $false
    }
}

function Test-TrainerAlive {
    $procs = Get-CimInstance Win32_Process -Filter "name='mlagents-learn.exe'" -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        if ($p.CommandLine -like "*--run-id=$RunId*" -or $p.CommandLine -like "*--run-id $RunId*") { return $true }
    }
    return $false
}

function Get-LastStep {
    # "CombatAgent. Step: 60000. ..." 형태 줄에서 가장 마지막 스텝 수를 뽑는다.
    # 주의: "Copied results...CombatAgent.onnx" 로그는 정상 완주 때도, 크래시로 죽을 때도 똑같이 찍힌다
    # (실측 확인 - UnityTimeOutException 크래시 직후에도 마지막 체크포인트를 export+copy함).
    # 그래서 그 문구만으로는 "완주"와 "크래시"를 구분 못 하고, 반드시 스텝 수를 max_steps와 비교해야 한다.
    if (-not (Test-Path $stdoutLog)) { return 0 }
    $matches = Select-String -Path $stdoutLog -Pattern "CombatAgent\. Step: (\d+)\." -ErrorAction SilentlyContinue
    if (-not $matches) { return 0 }
    $last = $matches[-1]
    return [int]$last.Matches[0].Groups[1].Value
}

function Start-Trainer {
    $mlagentsExe = Join-Path $trainerDir "venv\Scripts\mlagents-learn.exe"
    $stderr = Join-Path $trainerDir "${RunId}_stderr.log"
    Write-Log "mlagents-learn 재시작 (--resume): run-id=$RunId"
    Start-Process -FilePath $mlagentsExe `
        -ArgumentList "trainer_config.yaml", "--run-id=$RunId", "--resume" `
        -WorkingDirectory $trainerDir `
        -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderr `
        -WindowStyle Hidden
}

function Wait-ForTrainerListening {
    param([int]$TimeoutSec = 60)
    $elapsed = 0
    while ($elapsed -lt $TimeoutSec) {
        if ((Test-Path $stdoutLog) -and (Select-String -Path $stdoutLog -Pattern "Listening on port" -Quiet -ErrorAction SilentlyContinue)) {
            return $true
        }
        Start-Sleep -Seconds 3
        $elapsed += 3
    }
    return $false
}

function Wait-ForTrainerConnected {
    param([int]$TimeoutSec = 45)
    $elapsed = 0
    while ($elapsed -lt $TimeoutSec) {
        if ((Test-Path $stdoutLog) -and (Select-String -Path $stdoutLog -Pattern "Connected to Unity environment" -Quiet -ErrorAction SilentlyContinue)) {
            return $true
        }
        Start-Sleep -Seconds 3
        $elapsed += 3
    }
    return $false
}

function Invoke-Recovery {
    param([string]$Reason)
    Write-Log "복구 시작: $Reason"

    Write-Log "Unity.exe 강제 종료"
    taskkill /IM Unity.exe /F 2>&1 | Out-Null
    Start-Sleep -Seconds 5

    # taskkill 자체가 비정상 종료라서, 재실행할 때마다 "씬 백업 발견" 모달 다이얼로그가 떠서
    # 사람이 직접 클릭해줘야만 넘어가는 문제가 있었음(watchdog은 네이티브 다이얼로그를 못 누름 -> 영구 대기).
    # 이 다이얼로그를 유발하는 임시 백업 파일을 재실행 전에 지워서 애초에 안 뜨게 한다.
    $backupDir = Join-Path $ProjectPath "Temp\__Backupscenes"
    if (Test-Path $backupDir) {
        Remove-Item -Path $backupDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Log "씬 백업 복구 다이얼로그 방지: $backupDir 삭제"
    }

    # 트레이너가 죽어있으면 여기서 미리 새로 띄우지 않는다 - Unity가 응답 가능해질 때까지도
    # 한참 걸릴 수 있는데(다이얼로그 등), 트레이너의 자체 연결 타임아웃(기본 ~60초)이 그보다 짧아서
    # 미리 띄우면 Unity가 뜨기도 전에 또 죽어버림(실측 확인). Unity가 완전히 준비된 뒤에 띄운다.

    Write-Log "Unity 에디터 재실행: $UnityExe -projectPath `"$ProjectPath`""
    # Start-Process -ArgumentList에 배열로 넘기면 공백 포함 경로가 별도 인자로 쪼개지는 버그가 있어서
    # (실측: "E:\Git\Fps Manager"가 "E:\Git\Fps"/"Manager"로 분리돼 Unity가 엉뚱한 경로로 뜸)
    # 반드시 하나의 문자열로 합쳐서 넘긴다.
    Start-Process -FilePath $UnityExe -ArgumentList "-projectPath `"$ProjectPath`""

    $elapsed = 0
    $recovered = $false
    while ($elapsed -lt $RecoveryTimeoutSec) {
        Start-Sleep -Seconds 5
        $elapsed += 5
        if (Test-BridgeAlive) { $recovered = $true; break }
    }

    if (-not $recovered) {
        Write-Log "치명적: ${RecoveryTimeoutSec}초 내에 브릿지가 복구되지 않음 - 수동 확인 필요(다이얼로그가 떠 있을 수 있음)"
        return $false
    }

    Write-Log "브릿지 복구됨 (${elapsed}초 소요) - 컴파일 완료 대기"
    Start-Sleep -Seconds 10

    if ($RunId -ne "") {
        if (-not (Test-TrainerAlive)) {
            Start-Trainer
            Wait-ForTrainerListening -TimeoutSec 60 | Out-Null
        }
        Write-Log "Play 모드 재진입 시도"
        & $UnityCliExe tool call play_game --json "{}" *> $null
        if (Wait-ForTrainerConnected -TimeoutSec 45) {
            Write-Log "트레이너 재연결 확인됨"
        } else {
            Write-Log "경고: Play 후 45초 내에 트레이너 재연결 로그를 못 찾음 - 다음 점검에서 다시 확인"
        }
    }

    Write-Log "복구 완료"
    return $true
}

Write-Log "watchdog 시작 (RunId='$RunId', CheckInterval=${CheckIntervalSec}s, StaleThreshold=${StaleThresholdSec}s)"

if ($RunId -eq "") {
    # RunId 없이 범용으로 쓸 때는 학습 로그 신호가 없으니 예전처럼 브릿지 핑 기준으로 동작한다.
    $failCount = 0
    $failThreshold = 5
    while ($true) {
        if (Test-BridgeAlive) {
            if ($failCount -gt 0) { Write-Log "브릿지 정상 응답 복귀" }
            $failCount = 0
            Start-Sleep -Seconds $CheckIntervalSec
        } else {
            $failCount++
            Write-Log "핑 실패 ($failCount/$failThreshold)"
            if ($failCount -ge $failThreshold) {
                Invoke-Recovery -Reason "브릿지 응답 없음 ($failThreshold 회 연속 실패)" | Out-Null
                $failCount = 0
            } else {
                Start-Sleep -Seconds 5
            }
        }
    }
} else {
    # 파일 mtime만 보면 안 됨 - 트레이너가 Unity 재연결에 실패해서 "Restarting worker..." 재시도를
    # 반복할 때도 그때마다 로그 파일에 뭔가 쓰기 때문에 mtime이 계속 갱신돼서 "안 멈춘 것처럼" 보임
    # (실측 확인: Unity.exe가 완전히 죽어있는데도 트레이너가 재시도 로그만 계속 남겨서 이 체크를 무력화시킴).
    # 그래서 "마지막으로 스텝 숫자가 실제로 바뀐 시각" 기준으로 정체를 판단한다.
    $lastStepValue = Get-LastStep
    $lastStepSeenAt = Get-Date

    while ($true) {
        $trainerAlive = Test-TrainerAlive
        $lastStep = Get-LastStep

        if (-not $trainerAlive) {
            if ($lastStep -ge $MaxSteps) {
                Write-Log "학습 완주 확인(마지막 스텝 $lastStep >= max_steps $MaxSteps, 프로세스 정상 종료) - watchdog 종료"
                break
            }
            Invoke-Recovery -Reason "mlagents-learn(run-id=$RunId) 프로세스가 죽어있음 (마지막 스텝 $lastStep)" | Out-Null
            $lastStepValue = Get-LastStep
            $lastStepSeenAt = Get-Date
            Start-Sleep -Seconds $CheckIntervalSec
            continue
        }

        if ($lastStep -gt $lastStepValue) {
            $lastStepValue = $lastStep
            $lastStepSeenAt = Get-Date
        } else {
            $staleness = (Get-Date) - $lastStepSeenAt
            if ($staleness.TotalSeconds -gt $StaleThresholdSec) {
                Invoke-Recovery -Reason ("학습 스텝이 {0:N0}초간 안 늘어남(마지막 스텝 $lastStepValue, 트레이너 프로세스는 살아있으나 Unity 연결이 끊긴 것으로 추정)" -f $staleness.TotalSeconds) | Out-Null
                $lastStepValue = Get-LastStep
                $lastStepSeenAt = Get-Date
            }
        }

        Start-Sleep -Seconds $CheckIntervalSec
    }
}



