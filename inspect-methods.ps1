$dll = (Get-ChildItem -Recurse -Filter "CmlLib.Core.dll" "$env:USERPROFILE\.nuget\packages\cmllib.core").FullName | Select-Object -First 1
$asm = [System.Reflection.Assembly]::LoadFrom($dll)
$launcherType = $asm.GetType("CmlLib.Core.MinecraftLauncher")
Write-Host "--- MinecraftLauncher Methods ---"
$launcherType.GetMethods() | Where-Object { $_.IsPublic -and $_.DeclaringType -eq $launcherType } | ForEach-Object {
    $params = ($_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
    "$($_.ReturnType.Name) $($_.Name)($params)"
}
