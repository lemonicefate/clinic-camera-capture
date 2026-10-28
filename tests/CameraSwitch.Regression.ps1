$ErrorActionPreference = 'Stop'
# Source-contract regression for a symptom seen only on the remote two-camera PC.
# It is intentionally red against the current implementation.
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\MainPage.xaml.cs'))
$problems = New-Object System.Collections.Generic.List[string]
$enabled = [regex]::Match($source, 'CameraPicker\.IsEnabled\s*=\s*(?<expr>[^;]+);')
if (-not $enabled.Success) { $problems.Add('Cannot locate camera selector enabled state') }
elseif ($enabled.Groups['expr'].Value -match '_camera\s+is\s+null') {
    $problems.Add('Selector disabled whenever a camera is connected')
}
$handler = [regex]::Match(
    $source,
    'private\s+(?:async\s+)?void\s+CameraPicker_SelectionChanged\s*\([^{]+\)\s*\{(?<body>[\s\S]*?)\n    \}\s*\n',
    'Singleline')
if (-not $handler.Success) { $problems.Add('Camera selection changed event not found') }
elseif ($handler.Groups['body'].Value -notmatch 'SwitchCameraAsync|RequestCameraSwitchAsync') {
    $problems.Add('Selection change does not request any active camera switch')
}
if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Output ('FAIL: ' + $problem) }
    exit 1
}
Write-Output 'PASS: connected camera remains selectable and selection requests switching'
