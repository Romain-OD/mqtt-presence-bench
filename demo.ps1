# Runs one MQ02 scenario with the agent and the sensor in this console.
#   ./demo.ps1 kill | cut | quit | trap
param([ValidateSet('kill', 'cut', 'quit', 'trap')] [string] $Scenario = 'cut')

$app = "$PSScriptRoot/src/LastWillDemo/bin/Release/net10.0/LastWillDemo.exe"
if (-not (Test-Path $app)) { dotnet build "$PSScriptRoot/src/LastWillDemo" -c Release -v q --nologo | Out-Null }

& $app reset
if ($Scenario -eq 'trap') {
    & $app sensor --no-will --then kill --after 4
    Start-Sleep 3
    & $app agent --for 3
    return
}

$seconds = @{ kill = 10; cut = 26; quit = 10 }[$Scenario]
$agent = Start-Process $app -ArgumentList 'agent', '--for', $seconds -NoNewWindow -PassThru
Start-Sleep 1
& $app sensor --then $Scenario --after 6 --for ($seconds - 2)
$agent.WaitForExit()
