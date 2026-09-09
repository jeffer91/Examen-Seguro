param(
  [Parameter(Mandatory=$true)][string]$ServerUrl,
  [Parameter(Mandatory=$true)][string]$AgentKey
)
$ErrorActionPreference='Stop'
if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Ejecute el instalador como Administrador.'}
$base='C:\Program Files\ITSQMET\ExamenSeguro';$data='C:\ProgramData\ITSQMET\ExamenSeguro';New-Item -ItemType Directory -Force -Path $base,$data | Out-Null
Copy-Item "$PSScriptRoot\..\Agent\*" "$base\Agent" -Recurse -Force
Copy-Item "$PSScriptRoot\..\Service\*" "$base\Service" -Recurse -Force
$cfgPath="$data\client.json";$deviceCode=$null;if(Test-Path $cfgPath){try{$deviceCode=(Get-Content $cfgPath -Raw|ConvertFrom-Json).deviceCode}catch{}}
if(-not $deviceCode){$deviceCode=[guid]::NewGuid().ToString()}
@{serverUrl=$ServerUrl.TrimEnd('/')+'/';agentKey=$AgentKey;deviceCode=$deviceCode;pollSeconds=3}|ConvertTo-Json|Set-Content -Encoding UTF8 $cfgPath
& netsh http delete urlacl url=http://127.0.0.1:43119/ 2>$null | Out-Null
& netsh http add urlacl url=http://127.0.0.1:43119/ user=Users | Out-Null
$serviceExe="$base\Service\Itsqmet.ExamService.exe";& sc.exe stop ITSQMETExamService 2>$null | Out-Null;& sc.exe delete ITSQMETExamService 2>$null | Out-Null;Start-Sleep -Milliseconds 500;& sc.exe create ITSQMETExamService binPath= "`"$serviceExe`"" start= auto DisplayName= "ITSQMET Exam Service" | Out-Null;& sc.exe start ITSQMETExamService | Out-Null
$agentExe="$base\Agent\Itsqmet.ExamAgent.exe";$action=New-ScheduledTaskAction -Execute $agentExe;$trigger=New-ScheduledTaskTrigger -AtLogOn;$principal=New-ScheduledTaskPrincipal -GroupId 'BUILTIN\Users' -RunLevel Limited;Register-ScheduledTask -TaskName 'ITSQMET Exam Agent' -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName 'ITSQMET Exam Agent'
Write-Host "ITSQMET Examen Seguro instalado. Código de equipo: $deviceCode"
Write-Host 'La supervisión permanece inactiva hasta que el Administrador arme una sesión autorizada.'
