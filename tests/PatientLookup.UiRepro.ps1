param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [ValidateSet('forward','reverse')][string]$Direction = 'forward',
    [ValidateSet('focus','enter')][string]$Trigger = 'focus'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class FocusWindow { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd); }'
$root = Join-Path $env:TEMP ('clinic-lookup-ui-repro-' + [guid]::NewGuid().ToString('N'))
$config = Join-Path $env:LOCALAPPDATA 'ClinicCameraCapture-MVP\settings.json'
$backup = Join-Path $root 'settings.original'
$hadSettings = Test-Path -LiteralPath $config
$p = $null
$restored = $false
New-Item -ItemType Directory -Path $root -Force | Out-Null
try {
    if (Get-Process ClinicCameraCapture -ErrorAction SilentlyContinue) {
        throw 'Close other ClinicCameraCapture instances before this isolated UI test.'
    }
    if ($hadSettings) { Copy-Item -LiteralPath $config -Destination $backup }

    # Fabricate three strictly synthetic ASCII rows; no production DBF path is ever touched.
    $fixture = Join-Path $root 'PD011M1.dbf'
    $recordLength = 43
    $headerLength = 32 + 32 * 3 + 1 + 263
    $bytes = New-Object byte[] ($headerLength + 2 * $recordLength)
    $bytes[0] = 0x30
    [BitConverter]::GetBytes([uint32]2).CopyTo($bytes, 4)
    [BitConverter]::GetBytes([uint16]$headerLength).CopyTo($bytes, 8)
    [BitConverter]::GetBytes([uint16]$recordLength).CopyTo($bytes, 10)
    $defs = @(@('NUM',6,1),@('ID',12,7),@('NAME',24,19))
    for($i=0; $i -lt 3; $i++) {
        $offset = 32 + $i * 32
        [Text.Encoding]::ASCII.GetBytes($defs[$i][0]).CopyTo($bytes, $offset)
        $bytes[$offset+11] = 0x43
        [BitConverter]::GetBytes([uint32]$defs[$i][2]).CopyTo($bytes, $offset+12)
        $bytes[$offset+16] = [byte]$defs[$i][1]
    }
    $bytes[32+32*3] = 0x0D
    for($i=0; $i -lt 2; $i++) {
        $start = $headerLength + $i*$recordLength
        for($n=0; $n -lt $recordLength; $n++) {$bytes[$start+$n]=0x20}
        $num = if($i -eq 0){'000090'}else{'000091'}
        $id = if($i -eq 0){'TESTID0001'}else{'TESTID0002'}
        $name = if($i -eq 0){'SYNTHETIC ONE'}else{'SYNTHETIC TWO'}
        [Text.Encoding]::ASCII.GetBytes($num).CopyTo($bytes, $start+1)
        [Text.Encoding]::ASCII.GetBytes($id).CopyTo($bytes, $start+7)
        [Text.Encoding]::ASCII.GetBytes($name).CopyTo($bytes, $start+19)
    }
    [IO.File]::WriteAllBytes($fixture, $bytes)
    New-Item -ItemType Directory -Force -Path (Split-Path $config) | Out-Null
    $prefs = [ordered]@{
        CameraId='not-a-real-camera'; Format='png'; Shortcut='F9'; Mode='photo'; FullScreen=$false
        OutputFolder=(Join-Path $root 'captures'); PatientDbfPath=$fixture
    }
    [IO.File]::WriteAllText($config, (ConvertTo-Json $prefs -Compress), [Text.UTF8Encoding]::new($false))
    $p=Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe) -PassThru
    Start-Sleep -Seconds 4
    $p.Refresh()
    if($p.HasExited) {throw 'Test WinUI process exited.'}
    $window=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    function Find([string]$id) {
        $condition=New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)
        $node=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
        if($null -eq $node) {throw "Missing UI control: $id"}
        return $node
    }
    function Value($node) {
        $pattern=$node.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        return $pattern.Current.Value
    }
    $number=Find 'PatientNumberBox'
    $personId=Find 'PatientIdBox'
    $name=Find 'PatientNameBox'
    if($Direction -eq 'forward') {
        $number.SetFocus()
        Start-Sleep -Milliseconds 200
        $number.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('90')
        if ($Trigger -eq 'focus') { $personId.SetFocus() }
        else {
            [void][FocusWindow]::SetForegroundWindow($p.MainWindowHandle)
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        }
    } else {
        $personId.SetFocus()
        Start-Sleep -Milliseconds 200
        $personId.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('TESTID0001')
        if ($Trigger -eq 'focus') { $number.SetFocus() }
        else {
            [void][FocusWindow]::SetForegroundWindow($p.MainWindowHandle)
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        }
    }
    Start-Sleep -Seconds 3
    $status=Find 'ResultBar'
    $resultTitle=$window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Title')))
    $successCaption = -join @([char]0x60A3,[char]0x8005,[char]0x67E5,[char]0x8A62,[char]0x6210,[char]0x529F)
    $reportedSuccess=($null -ne $resultTitle -and $resultTitle.Current.Name -eq $successCaption)
    $numberText=Value $number
    $idText=Value $personId
    $nameText=Value $name
    $correct=($numberText -eq '000090' -and $idText -eq 'TESTID0001' -and $nameText -eq 'SYNTHETIC ONE')
    [pscustomobject]@{
        Direction=$Direction
        Trigger=$Trigger
        ReportedSuccess=$reportedSuccess
        NumberMatches=$numberText -eq '000090'
        IdMatches=$idText -eq 'TESTID0001'
        NameMatches=$nameText -eq 'SYNTHETIC ONE'
        NumberEmpty=[string]::IsNullOrEmpty($numberText)
        IdEmpty=[string]::IsNullOrEmpty($idText)
        NameEmpty=[string]::IsNullOrEmpty($nameText)
        Result=$(if($correct){'PASS'}else{'FAIL'})
    }|Format-List
    # A genuine user edit must still invalidate the previously resolved patient.
    if ($correct) {
        if ($Direction -eq 'forward') {
            $number.SetFocus()
            $number.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('91')
            Start-Sleep -Seconds 1
            $editCleared = ((Value $number) -eq '91' -and
                [string]::IsNullOrEmpty((Value $personId)) -and
                [string]::IsNullOrEmpty((Value $name)))
        } else {
            $personId.SetFocus()
            $personId.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('TESTID0002')
            Start-Sleep -Seconds 1
            $editCleared = ((Value $personId) -eq 'TESTID0002' -and
                [string]::IsNullOrEmpty((Value $number)) -and
                [string]::IsNullOrEmpty((Value $name)))
        }
        Write-Host ("RealUserEditInvalidatesPreviousPatient: " + $editCleared)
        if (-not $editCleared) {
            Write-Host ("EDIT_CHECK_STATE numberLength=" + (Value $number).Length +
                " idLength=" + (Value $personId).Length +
                " nameLength=" + (Value $name).Length)
        }
        $correct = $correct -and $editCleared
    }
    if(-not $correct) {exit 1}
}
finally {
    if($p -and -not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        try{$p.WaitForExit(5000)|Out-Null}catch{}
    }
    if($hadSettings) {Copy-Item -LiteralPath $backup -Destination $config -Force}
    else {Remove-Item -LiteralPath $config -ErrorAction SilentlyContinue}
    if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}
}
