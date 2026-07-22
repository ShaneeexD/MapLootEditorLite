Add-Type -Path 'C:\SPT\BepInEx\core\Mono.Cecil.dll'
function Get-TypesRecursive($type) { $type; $type.NestedTypes | ForEach-Object { Get-TypesRecursive $_ } }
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\SPT\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll')
$allTypes = $asm.MainModule.Types | ForEach-Object { Get-TypesRecursive $_ }
$g = $allTypes | Where-Object { $_.Name -eq 'GClass1911' } | Select-Object -First 1
if ($g) {
    Write-Output ('GClass1911 full: ' + $g.FullName)
    $g.Methods | Where-Object { $_.Name -eq 'CreateItem' } | ForEach-Object { Write-Output $_.FullName }
    $m = $g.Methods | Where-Object { $_.Name -eq 'CreateItem' -and $_.Parameters.Count -eq 2 } | Select-Object -First 1
    if ($m -and $m.HasBody) {
        Write-Output '--- GClass1911.CreateItem IL ---'
        $m.Body.Instructions | ForEach-Object { Write-Output ('{0}: {1} {2}' -f $_.Offset, $_.OpCode, $_.Operand) }
    }
}
$h = $allTypes | Where-Object { $_.Name -eq 'GClass846' } | Select-Object -First 1
if ($h) {
    Write-Output ('GClass846 full: ' + $h.FullName)
    Write-Output '--- GClass846 fields ---'
    $h.Fields | ForEach-Object { Write-Output $_.FullName }
    Write-Output '--- GClass846 properties ---'
    $h.Properties | ForEach-Object { Write-Output $_.FullName }
}
