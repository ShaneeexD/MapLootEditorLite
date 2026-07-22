Add-Type -Path 'C:\SPT\BepInEx\core\Mono.Cecil.dll'
function Get-TypesRecursive($type) { $type; $type.NestedTypes | ForEach-Object { Get-TypesRecursive $_ } }
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\SPT\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll')
$allTypes = $asm.MainModule.Types | ForEach-Object { Get-TypesRecursive $_ }
$wio = $allTypes | Where-Object { $_.Name -eq 'WorldInteractiveObject' } | Select-Object -First 1
if ($wio) {
    Write-Output '--- WorldInteractiveObject fields/properties ---'
    $wio.Fields | ForEach-Object { if ($_.Name -like '*Key*' -or $_.Name -like '*Lock*' -or $_.Name -like '*Door*') { Write-Output $_.FullName } }
    $wio.Properties | ForEach-Object { if ($_.Name -like '*Key*' -or $_.Name -like '*Lock*' -or $_.Name -like '*Door*') { Write-Output $_.FullName } }
}
$lc = $allTypes | Where-Object { $_.Name -eq 'LootableContainer' } | Select-Object -First 1
if ($lc) {
    Write-Output '--- LootableContainer fields/properties ---'
    $lc.Fields | ForEach-Object { if ($_.Name -like '*Key*' -or $_.Name -like '*Lock*' -or $_.Name -like '*Door*' -or $_.Name -like '*Always*' -or $_.Name -like '*Converted*') { Write-Output $_.FullName } }
    $lc.Properties | ForEach-Object { if ($_.Name -like '*Key*' -or $_.Name -like '*Lock*' -or $_.Name -like '*Door*') { Write-Output $_.FullName } }
}
$lockc = $allTypes | Where-Object { $_.Name -eq 'LockableLootContainerComponent' } | Select-Object -First 1
if ($lockc) {
    Write-Output '--- LockableLootContainerComponent methods ---'
    $lockc.Methods | ForEach-Object { Write-Output $_.FullName } | Select-Object -First 20
    Write-Output '--- LockableLootContainerComponent fields ---'
    $lockc.Fields | ForEach-Object { if ($_.Name -like '*Lock*' -or $_.Name -like '*Key*') { Write-Output $_.FullName } } | Select-Object -First 20
}
