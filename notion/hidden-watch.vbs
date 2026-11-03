' Runs notion\watch.ps1 completely hidden: wscript has no console window,
' and PowerShell is started with window style 0, so nothing flashes on screen.
Set fso = CreateObject("Scripting.FileSystemObject")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = dir
cmd = "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & dir & "\watch.ps1"""
sh.Run cmd, 0, False
