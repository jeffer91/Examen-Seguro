$ErrorActionPreference='SilentlyContinue'
Stop-ScheduledTask -TaskName 'ITSQMET Exam Agent';Unregister-ScheduledTask -TaskName 'ITSQMET Exam Agent' -Confirm:$false
Get-Process Itsqmet.ExamAgent | Stop-Process -Force
& sc.exe stop ITSQMETExamService | Out-Null;& sc.exe delete ITSQMETExamService | Out-Null
& netsh http delete urlacl url=http://127.0.0.1:43119/ | Out-Null
Remove-Item 'C:\Program Files\ITSQMET\ExamenSeguro' -Recurse -Force
Write-Host 'Aplicación desinstalada. Los datos locales de ProgramData se conservan para auditoría; el administrador puede retirarlos conforme a la política institucional.'
