Set fso = CreateObject("Scripting.FileSystemObject")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = dir

' tunnel (sing-box if configured, else xray)
If Not IsProcessRunning("sing-box.exe") And Not IsProcessRunning("xray.exe") Then
  sh.Run "wscript.exe """ & dir & "\hidden.vbs""", 0, False
End If

' local PAC server on 127.0.0.1:10810 (exits by itself if the port is already taken)
sh.Run "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & dir & "\pacsrv.ps1""", 0, False

Function IsProcessRunning(name)
  Set svc = GetObject("winmgmts:\\.\root\cimv2")
  Set list = svc.ExecQuery("Select * from Win32_Process Where Name = '" & name & "'")
  IsProcessRunning = (list.Count > 0)
End Function
