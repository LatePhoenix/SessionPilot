#Requires -Version 7.0

# Samples one process from Get-Process and prints min, mean, and max.
# Writes nothing to disk.

[CmdletBinding()]
param(
    [string] $ProcessName = 'SessionPilot.App',

    [Parameter(Mandatory = $true)]
    [double] $Minutes,

    [int] $IntervalSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Failure {
    param([string] $Message)
    Write-Output $Message
    exit 1
}

if ([string]::IsNullOrWhiteSpace($ProcessName)) {
    Write-Failure 'ProcessName is empty.'
}

if ($Minutes -le 0) {
    Write-Failure 'Minutes must be greater than zero.'
}

if ($IntervalSeconds -le 0) {
    Write-Failure 'IntervalSeconds must be greater than zero.'
}

$found = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
if ($found.Count -eq 0) {
    Write-Failure "$ProcessName is not running."
}

if ($found.Count -gt 1) {
    Write-Failure "$ProcessName matches more than one process."
}

$processId = $found[0].Id
$found[0].Dispose()
$durationSeconds = $Minutes * 60.0
$targets = [System.Collections.Generic.List[double]]::new()
for ($t = 0.0; $t -lt $durationSeconds; $t += $IntervalSeconds) {
    $targets.Add($t)
}

$lastTarget = $targets[$targets.Count - 1]
if (($durationSeconds - $lastTarget) -gt 0.0001) {
    $targets.Add($durationSeconds)
}

function Format-Fixed {
    param([double] $Value)
    return $Value.ToString('0.###', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Cpu {
    param([double] $Value)
    return $Value.ToString('0.00', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Read-Sample {
    param(
        [int] $Id,
        [double] $ElapsedSeconds
    )

    $current = $null
    $result = $null
    try {
        $live = @(Get-Process -Id $Id -ErrorAction SilentlyContinue)
        if ($live.Count -eq 1) {
            $current = $live[0]
            if (-not $current.HasExited) {
                $current.Refresh()
                $result = [pscustomobject]@{
                    ElapsedSeconds      = $ElapsedSeconds
                    TotalProcessorTime  = $current.TotalProcessorTime.TotalSeconds
                    WorkingSet64        = [double] $current.WorkingSet64
                    PrivateMemorySize64 = [double] $current.PrivateMemorySize64
                    HandleCount         = [double] $current.HandleCount
                    ThreadCount         = [double] $current.Threads.Count
                }
            }
        }
    }
    catch {
        $result = $null
    }
    finally {
        if ($null -ne $current) {
            $current.Dispose()
        }
    }

    return $result
}

function Get-Stats {
    param([double[]] $Values)
    $min = $Values[0]
    $max = $Values[0]
    $sum = 0.0
    foreach ($value in $Values) {
        if ($value -lt $min) { $min = $value }
        if ($value -gt $max) { $max = $value }
        $sum += $value
    }

    return [pscustomobject]@{
        Min  = $min
        Mean = $sum / $Values.Count
        Max  = $max
    }
}

$clock = [System.Diagnostics.Stopwatch]::StartNew()
$samples = [System.Collections.Generic.List[object]]::new()
foreach ($target in $targets) {
    $remainingMs = ($target - $clock.Elapsed.TotalSeconds) * 1000.0
    if ($remainingMs -gt 2) {
        Start-Sleep -Milliseconds ([int][Math]::Round($remainingMs))
    }

    $sample = Read-Sample -Id $processId -ElapsedSeconds $clock.Elapsed.TotalSeconds
    if ($null -eq $sample) {
        Write-Failure "$ProcessName is not running."
    }

    $samples.Add($sample)
    Write-Output ("sample {0} at {1} s" -f $samples.Count, (Format-Fixed $sample.ElapsedSeconds))
}

$first = $samples[0]
$last = $samples[$samples.Count - 1]
$runWall = $last.ElapsedSeconds - $first.ElapsedSeconds
$runCpu = $last.TotalProcessorTime - $first.TotalProcessorTime
if ($runWall -le 0) {
    Write-Failure 'The run was too short to measure CPU use.'
}

$runPercent = 100.0 * $runCpu / $runWall
$intervalMin = [double]::PositiveInfinity
$intervalMax = [double]::NegativeInfinity
for ($i = 1; $i -lt $samples.Count; $i++) {
    $previous = $samples[$i - 1]
    $current = $samples[$i]
    $wall = $current.ElapsedSeconds - $previous.ElapsedSeconds
    if ($wall -le 0) {
        continue
    }

    $percent = 100.0 * ($current.TotalProcessorTime - $previous.TotalProcessorTime) / $wall
    if ($percent -lt $intervalMin) { $intervalMin = $percent }
    if ($percent -gt $intervalMax) { $intervalMax = $percent }
}

$rows = @(
    @{ Name = 'TotalProcessorTime (s)'; Values = @($samples | ForEach-Object { $_.TotalProcessorTime }) }
    @{ Name = 'WorkingSet64 (bytes)'; Values = @($samples | ForEach-Object { $_.WorkingSet64 }) }
    @{ Name = 'PrivateMemorySize64 (bytes)'; Values = @($samples | ForEach-Object { $_.PrivateMemorySize64 }) }
    @{ Name = 'HandleCount'; Values = @($samples | ForEach-Object { $_.HandleCount }) }
    @{ Name = 'Threads'; Values = @($samples | ForEach-Object { $_.ThreadCount }) }
)

Write-Output ''
Write-Output "Process: $ProcessName"
Write-Output "Samples: $($samples.Count)"
Write-Output "Run seconds: $(Format-Fixed $runWall)"
Write-Output "Interval seconds: $IntervalSeconds"
Write-Output ''
Write-Output ('{0,-32} {1,16} {2,16} {3,16}' -f 'Value', 'Min', 'Mean', 'Max')
foreach ($row in $rows) {
    $stats = Get-Stats -Values ([double[]] $row.Values)
    Write-Output ('{0,-32} {1,16} {2,16} {3,16}' -f $row.Name, (Format-Fixed $stats.Min), (Format-Fixed $stats.Mean), (Format-Fixed $stats.Max))
}

Write-Output ''
Write-Output "CPU use, percent of one logical core over the run: $(Format-Cpu $runPercent)"
if ($intervalMax -eq [double]::NegativeInfinity) {
    Write-Output 'CPU use per interval, percent of one logical core: not available.'
}
else {
    Write-Output "CPU use per interval, percent of one logical core: min $(Format-Cpu $intervalMin)  max $(Format-Cpu $intervalMax)"
}
