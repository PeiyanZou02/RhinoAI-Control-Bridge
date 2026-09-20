param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rhino = 'C:\Program Files\Rhino 8\System'
New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist') | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
if ($Tests) { $sources += @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs' | Select-Object -ExpandProperty FullName) }
# PowerShell 7 bundles Roslyn. Windows PowerShell 5.1 borrows the copy that ships with Rhino 8,
# so no separate .NET SDK, NuGet download or PowerShell upgrade is needed.
Add-Type -TypeDefinition 'public class RhinoAIBuildWarmup {}' -ErrorAction SilentlyContinue
if (-not ('Microsoft.CodeAnalysis.CSharp.CSharpCompilation' -as [type])) {
    # Rhino's Roslyn depends on newer System.Memory/Immutable builds than .NET Framework binds by default.
    Add-Type -TypeDefinition @"
using System;using System.IO;using System.Reflection;
public static class RhinoAIRoslynResolver {
    static string dir;
    public static void Install(string folder){dir=folder;AppDomain.CurrentDomain.AssemblyResolve+=Resolve;}
    static Assembly Resolve(object sender,ResolveEventArgs args){
        string name=new AssemblyName(args.Name).Name;if(name.EndsWith(".resources"))return null;
        foreach(var loaded in AppDomain.CurrentDomain.GetAssemblies())if(loaded.GetName().Name==name)return loaded;
        string path=Path.Combine(dir,name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;
    }
}
"@
    [RhinoAIRoslynResolver]::Install($rhino)
    [void][Reflection.Assembly]::LoadFrom((Join-Path $rhino 'Microsoft.CodeAnalysis.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $rhino 'Microsoft.CodeAnalysis.CSharp.dll'))
}
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll') | ForEach-Object { Join-Path $framework $_ }
$refs += @((Join-Path $rhino 'RhinoCommon.dll'),(Join-Path $rhino 'Newtonsoft.Json.dll'))
$metadata = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($ref in $refs) { $metadata.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($ref)) }
$syntax = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($source in $sources) { $syntax.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($source))) }
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release)
$assemblyName = if ($Tests) { 'Rhino2Comfy.Tests' } else { 'Rhino2Comfy' }
$outputFile = if ($Tests) { Join-Path $root 'dist\Rhino2Comfy.Tests.dll' } else { Join-Path $root 'dist\Rhino2Comfy.rhp' }
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($assemblyName, $syntax, $metadata, $options)
$stream = [IO.MemoryStream]::new()
try { $result = $compilation.Emit($stream); $bytes = $stream.ToArray() } finally { $stream.Dispose() }
foreach ($diagnostic in $result.Diagnostics) { if ($diagnostic.Severity -ne 'Hidden') { Write-Output $diagnostic.ToString() } }
if (-not $result.Success) { throw 'Plugin compilation failed.' }
$temporary = $outputFile + '.new'
[IO.File]::WriteAllBytes($temporary, $bytes)
if (Test-Path -LiteralPath $outputFile) {
    # Rename mapped assemblies instead of truncating a file still loaded by Rhino.
    $resolved = [IO.Path]::GetFullPath($outputFile)
    $distRoot = [IO.Path]::GetFullPath((Join-Path $root 'dist')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build output escaped dist.' }
    Move-Item -LiteralPath $resolved -Destination ($resolved + '.previous-' + [Guid]::NewGuid().ToString('N'))
}
Move-Item -LiteralPath $temporary -Destination $outputFile
Write-Output $outputFile
