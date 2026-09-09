param(
  [Parameter(Mandatory=$true)][string]$ServerUrl,
  [Parameter(Mandatory=$true)][string]$AgentKey
)

$ErrorActionPreference='Stop'

$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=[Security.Principal.WindowsPrincipal]$identity
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
  throw 'Ejecute el instalador como Administrador.'
}

$base='C:\Program Files\ITSQMET\ExamenSeguro'
$data='C:\ProgramData\ITSQMET\ExamenSeguro'
$agentDir="$base\Agent"
$serviceDir="$base\Service"
$companionDir="$base\BrowserCompanion"
New-Item -ItemType Directory -Force -Path $base,$data,$agentDir,$serviceDir,$companionDir | Out-Null

Copy-Item "$PSScriptRoot\..\Agent\*" $agentDir -Recurse -Force
Copy-Item "$PSScriptRoot\..\Service\*" $serviceDir -Recurse -Force
if(Test-Path "$PSScriptRoot\..\browser-companion"){
  Copy-Item "$PSScriptRoot\..\browser-companion\*" $companionDir -Recurse -Force
}

$cfgPath="$data\client.json"
$deviceCode=$null
if(Test-Path $cfgPath){
  try{$deviceCode=(Get-Content $cfgPath -Raw|ConvertFrom-Json).deviceCode}catch{}
}
if(-not $deviceCode){$deviceCode=[guid]::NewGuid().ToString()}

@{
  serverUrl=$ServerUrl.TrimEnd('/')+'/'
  agentKey=$AgentKey
  deviceCode=$deviceCode
  pollSeconds=3
}|ConvertTo-Json|Set-Content -Encoding UTF8 $cfgPath

# Resuelve el grupo integrado Users/Usuarios por SID para funcionar en Windows en cualquier idioma.
$usersSid=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
$usersAccount=$usersSid.Translate([Security.Principal.NTAccount]).Value

& netsh http delete urlacl url=http://127.0.0.1:43119/ 2>$null | Out-Null
& netsh http add urlacl url=http://127.0.0.1:43119/ user="$usersAccount" | Out-Null

$serviceExe="$serviceDir\Itsqmet.ExamService.exe"
& sc.exe stop ITSQMETExamService 2>$null | Out-Null
& sc.exe delete ITSQMETExamService 2>$null | Out-Null
Start-Sleep -Milliseconds 800
& sc.exe create ITSQMETExamService binPath= "`"$serviceExe`"" start= auto DisplayName= "ITSQMET Exam Service" | Out-Null
& sc.exe start ITSQMETExamService | Out-Null

$agentExe="$agentDir\Itsqmet.ExamAgent.exe"
$action=New-ScheduledTaskAction -Execute $agentExe
$trigger=New-ScheduledTaskTrigger -AtLogOn
$taskPrincipal=New-ScheduledTaskPrincipal -GroupId $usersAccount -RunLevel Limited
Register-ScheduledTask -TaskName 'ITSQMET Exam Agent' -Action $action -Trigger $trigger -Principal $taskPrincipal -Force | Out-Null
Start-ScheduledTask -TaskName 'ITSQMET Exam Agent'

Write-Host "ITSQMET Examen Seguro instalado. Código de equipo: $deviceCode"
Write-Host 'La supervisión permanece inactiva hasta que el Administrador habilite una sesión previamente informada.'
Write-Host "Complementos de navegador copiados en: $companionDir"
