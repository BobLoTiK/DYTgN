' silent idempotent startup pass: zapret check + TgWsProxy + notion tunnel/PAC
Set fso = CreateObject("Scripting.FileSystemObject")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = dir
sh.Run "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & dir & "\netsuite.ps1"" -Action ensure", 0, False
