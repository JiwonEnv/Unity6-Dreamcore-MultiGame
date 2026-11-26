# Runs the single-player self tests and keeps everything each run produced in TestEvidence/run_<time>/:
# one log per scenario (written there directly, so the engine's log rotation cannot delete it), new screenshots,
# and results.md / results.json with the code version, uncommitted changes, command, exit code, seeds and every check.
#
#   powershell -ExecutionPolicy Bypass -File Tools/run_selftests.ps1                  # all scenarios
#   powershell -ExecutionPolicy Bypass -File Tools/run_selftests.ps1 -Only proc_a,abort_empty
#
# Build the editor target first; this script does not build. Exit code 0 only if every scenario passed.
param(
    [string[]]$Only = @(),
    [string]$Engine = "C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64",
    [int]$TimeoutSeconds = 420
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "DreamMovers.uproject"
$Stamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$RunDir = Join-Path $Root "TestEvidence\run_$Stamp"
New-Item -ItemType Directory -Force $RunDir | Out-Null

$Sha = (git -C $Root rev-parse HEAD).Trim()
$Dirty = @(git -C $Root status --porcelain)
$EngineVersion = Get-Content (Join-Path (Split-Path -Parent (Split-Path -Parent $Engine)) "Build\Build.version") -Raw | ConvertFrom-Json

$Game = @{ Exe = "UnrealEditor.exe"; Pre = @('"' + $Project + '"') }
$Cmd = @{ Exe = "UnrealEditor-Cmd.exe"; Pre = @('"' + $Project + '"') }
$Window = "-game -windowed -ResX=1280 -ResY=720 -nosplash -unattended"
$Scenarios = @(
    @{ Name = "seeds200";        Kind = "editor"; Tool = $Cmd;  Args = "-ExecutePythonScript=$Root/Tools/ue/test_proc_level.py -unattended -nop4 -nosplash" },
    @{ Name = "proc_a";          Kind = "dmtest"; Tool = $Game; Args = "/Game/Maps/Lvl_ProcGen?listen $Window -dmproctest -plgseed=535117761" },
    @{ Name = "proc_b";          Kind = "dmtest"; Tool = $Game; Args = "/Game/Maps/Lvl_ProcGen?listen $Window -dmproctest -plgseed=2037773057" },
    @{ Name = "abort_empty";     Kind = "dmtest"; Tool = $Game; Args = "/Game/Maps/Lvl_ProcGen?listen $Window -dmproctest -dmprocexpect=abort -plgtestkit=empty" },
    @{ Name = "abort_impossible"; Kind = "dmtest"; Tool = $Game; Args = "/Game/Maps/Lvl_ProcGen?listen $Window -dmproctest -dmprocexpect=abort -plgtestkit=impossible" },
    @{ Name = "abort_navtimeout"; Kind = "dmtest"; Tool = $Game; Args = "/Game/Maps/Lvl_ProcGen?listen $Window -dmproctest -dmprocexpect=abort -dmnavtimeout=0.01 -plgseed=535117761" },
    @{ Name = "desk_hide";       Kind = "dmtest"; Tool = $Game; Args = "/Game/Maps/Lvl_ProcGen?listen $Window -dmproctest -dmprocexpect=deskhide -plgseed=535117761" },
    @{ Name = "plg_selftest";    Kind = "plg";    Tool = $Game; Args = "/Game/Maps/Lvl_ProcGen?listen $Window -plgselftest -plgseed=535117761" },
    @{ Name = "proto_selftest";  Kind = "proto";  Tool = $Game; Args = "/Game/Maps/Lvl_Proto?listen $Window -dmselftest" }
)
if ($Only.Count -gt 0) { $Scenarios = $Scenarios | Where-Object { $Only -contains $_.Name } }

$Shots = Join-Path $Root "Saved\Screenshots\WindowsEditor"
$Results = @()
foreach ($S in $Scenarios) {
    $Log = Join-Path $RunDir "$($S.Name).log"
    $ArgLine = (($S.Tool.Pre + $S.Args + "-ABSLOG=`"$Log`"") -join " ")
    $Started = Get-Date
    Write-Host "[$($S.Name)] $($S.Tool.Exe) $ArgLine"
    $P = Start-Process -FilePath (Join-Path $Engine $S.Tool.Exe) -ArgumentList $ArgLine -PassThru
    $null = $P.Handle  # keeps the handle so ExitCode is available afterwards
    $TimedOut = -not $P.WaitForExit($TimeoutSeconds * 1000)
    if ($TimedOut) { $P.Kill(); $P.WaitForExit() }
    $Exit = if ($TimedOut) { -1 } else { $P.ExitCode }
    $Duration = [int]((Get-Date) - $Started).TotalSeconds

    $Lines = if (Test-Path $Log) { Get-Content $Log -Encoding UTF8 } else { @() }
    $Checks = @($Lines | Select-String -Pattern '\[(DMTest|SelfTest|PLGSelfTest|PLGTest)\] (PASS|FAIL)' | ForEach-Object { ($_.Line -split '\]\[\s*\d+\]', 2)[-1].Trim() })
    $Fails = @($Checks | Where-Object { $_ -match '\] FAIL' })
    $Seeds = @($Lines | Select-String -Pattern 'seed (-?\d+): \d+ modules' -AllMatches | ForEach-Object { $_.Matches[0].Value } | Select-Object -Unique)
    $Hashes = @($Lines | Select-String -Pattern 'layout hash ([0-9a-f]{8})' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Select-Object -Unique)
    switch ($S.Kind) {
        "editor" { $Passed = ($Lines -match '\[PLGTest\] PASS 200/200 seeds').Count -gt 0 -and $Fails.Count -eq 0 }
        "dmtest" { $Passed = $Exit -eq 0 -and ($Lines -match '\[DMTest\] RESULT PASS').Count -gt 0 -and $Fails.Count -eq 0 }
        "plg"    { $Passed = $Exit -eq 0 -and ($Lines -match '\[PLGSelfTest\] ALL PASSED').Count -gt 0 }
        "proto"  { $Passed = $Exit -eq 0 -and ($Lines -match '\[SelfTest\] DONE: \d+ passed, 0 failed').Count -gt 0 }
    }
    if ($TimedOut) { $Passed = $false }

    $Copied = @()
    if (Test-Path $Shots) {
        Get-ChildItem $Shots -Filter *.png | Where-Object { $_.LastWriteTime -ge $Started } | ForEach-Object {
            $Dest = Join-Path $RunDir "$($S.Name)_$($_.Name)"
            Copy-Item $_.FullName $Dest
            $Copied += (Split-Path -Leaf $Dest)
        }
    }
    $Results += [pscustomobject]@{
        Name = $S.Name; Passed = $Passed; ExitCode = $Exit; TimedOut = $TimedOut; Seconds = $Duration
        Command = "$($S.Tool.Exe) $ArgLine"; Seeds = $Seeds; LayoutHashes = $Hashes; Checks = $Checks; Screenshots = $Copied
        Log = (Split-Path -Leaf $Log)
    }
    Write-Host ("[{0}] {1} (exit {2}, {3} s, {4} checks, {5} failed)" -f $S.Name, $(if ($Passed) { "PASS" } else { "FAIL" }), $Exit, $Duration, $Checks.Count, $Fails.Count)
}

$Summary = [pscustomobject]@{
    Started = $Stamp; Commit = $Sha; UncommittedChanges = $Dirty
    Engine = "$($EngineVersion.MajorVersion).$($EngineVersion.MinorVersion).$($EngineVersion.PatchVersion) CL $($EngineVersion.Changelist)"
    Scenarios = $Results
}
$Summary | ConvertTo-Json -Depth 6 | Out-File (Join-Path $RunDir "results.json") -Encoding utf8

$Md = @("# Self test run $Stamp", "",
    "- Commit: ``$Sha``",
    "- Engine: $($Summary.Engine)",
    "- Uncommitted changes: $(if ($Dirty.Count -eq 0) { 'none' } else { "$($Dirty.Count) path(s), listed at the end" })", "",
    "| Scenario | Result | Exit | Seconds | Seeds | Log |", "|---|---|---|---|---|---|")
foreach ($R in $Results) {
    $Md += "| $($R.Name) | $(if ($R.Passed) { 'PASS' } else { 'FAIL' }) | $($R.ExitCode)$(if ($R.TimedOut) { ' (timeout)' }) | $($R.Seconds) | $(($R.Seeds -join ', ')) | ``$($R.Log)`` |"
}
foreach ($R in $Results) {
    $Md += ""; $Md += "## $($R.Name)"; $Md += ""; $Md += "``$($R.Command)``"; $Md += ""
    if ($R.LayoutHashes.Count -gt 0) { $Md += "Layout hash: $($R.LayoutHashes -join ', ')"; $Md += "" }
    $Md += ($R.Checks | ForEach-Object { "- $_" })
    if ($R.Screenshots.Count -gt 0) { $Md += ""; $Md += "Screenshots: $($R.Screenshots -join ', ')" }
}
if ($Dirty.Count -gt 0) { $Md += ""; $Md += "## Uncommitted changes"; $Md += ""; $Md += ($Dirty | ForEach-Object { "- ``$_``" }) }
$Md | Out-File (Join-Path $RunDir "results.md") -Encoding utf8

Write-Host "Evidence: $RunDir"
if (@($Results | Where-Object { -not $_.Passed }).Count -gt 0) { exit 1 } else { exit 0 }
