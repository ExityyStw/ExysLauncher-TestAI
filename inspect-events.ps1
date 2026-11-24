$dll = (Get-ChildItem -Recurse -Filter "CmlLib.Core.dll" "$env:USERPROFILE\.nuget\packages\cmllib.core").FullName | Select-Object -First 1
$asm = [System.Reflection.Assembly]::LoadFrom($dll)
$launcherType = $asm.GetType("CmlLib.Core.MinecraftLauncher")
$ev = $launcherType.GetEvents()
foreach ($e in $ev) {
    Write-Host "$($e.EventHandlerType.FullName) $($e.Name)"
}
