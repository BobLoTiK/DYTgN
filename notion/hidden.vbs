Set fso = CreateObject("Scripting.FileSystemObject")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
parent = fso.GetParentFolderName(dir)
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = dir

' binaries live in ..\bin (repo layout) or next to this script (legacy layout)
binDir = parent & "\bin"
If Not fso.FileExists(binDir & "\sing-box.exe") Then binDir = dir
If Not fso.FileExists(binDir & "\xray.exe") Then binDir = dir

If fso.FileExists(dir & "\sb-config.json") Then
  sh.Run """" & binDir & "\sing-box.exe"" run -c """ & dir & "\sb-config.json""", 0, False
Else
  sh.Run """" & binDir & "\xray.exe"" run -c """ & dir & "\config.json""", 0, False
End If
