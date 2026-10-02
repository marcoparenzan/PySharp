# Baseline/progress probe for the cvintro notebooks (see NOTEBOOKS_PLAN.md).
# For every lessonNN notebook: which top-level modules it imports, which of those PySharp can import,
# and which `np.*` names it uses that PySharp's numpy lacks. Writes NOTEBOOKS_BASELINE.md.
param(
  [string]$Notebooks = 'D:\clones\sbirchfield.github.io\cvintro\notebooks',
  [string]$Out = (Join-Path $PSScriptRoot '..\NOTEBOOKS_BASELINE.md'),
  [string]$Pysharp = (Join-Path $PSScriptRoot '..\src\PySharp\bin\Debug\net10.0\PySharp.dll')
)
$tmp = Join-Path $env:TEMP 'nbprobe'; New-Item -ItemType Directory -Force $tmp | Out-Null
$rows = @()
foreach ($f in Get-ChildItem $Notebooks -Filter *.ipynb | Sort-Object Name) {
  $nb = Get-Content $f.FullName -Raw | ConvertFrom-Json
  $code = ($nb.cells | ? { $_.cell_type -eq 'code' } | % { $_.source -join '' }) -join "`n"
  $mods = [regex]::Matches($code,'(?m)^\s*(?:import|from)\s+([\w\.]+)') | % { $_.Groups[1].Value } | Sort-Object -Unique
  $np = [regex]::Matches($code,'\bnp\.([\w\.]+)') | % { $_.Groups[1].Value } | Sort-Object -Unique
  $py = "import sys`nmods=[" + (($mods | % { "'$_'" }) -join ',') + "]`nbad=[]`nfor m in mods:`n    try: __import__(m)`n    except Exception: bad.append(m)`n" +
        "import numpy as np`nmiss=[]`nfor n in [" + (($np | % { "'$_'" }) -join ',') + "]:`n    o=np; ok=True`n    for p in n.split('.'):`n        if hasattr(o,p): o=getattr(o,p)`n        else: ok=False; break`n    if not ok: miss.append(n)`nprint('MODS', ' '.join(bad))`nprint('NP', ' '.join(miss))`n"
  $p = Join-Path $tmp ($f.BaseName + '.py'); Set-Content $p $py -Encoding utf8
  $o = dotnet $Pysharp run $p 2>&1
  $bad = (($o | ? { $_ -like 'MODS*' }) -replace '^MODS\s*','').Trim()
  $miss = (($o | ? { $_ -like 'NP*' }) -replace '^NP\s*','').Trim()
  $rows += [pscustomobject]@{ Lesson = $f.BaseName; Modules = $bad; NpMissing = $miss; NpMissingCount = @($miss -split ' ' | ? { $_ }).Count }
}
$md = "# Notebook baseline`n`nGenerated $(Get-Date -Format 'yyyy-MM-dd HH:mm') by tools/notebook_probe.ps1 - do not edit by hand.`n`n| Lesson | Unimportable modules | np names missing | Missing list |`n|---|---|---|---|`n"
foreach ($r in $rows) { $md += "| $($r.Lesson) | $($r.Modules) | $($r.NpMissingCount) | $($r.NpMissing) |`n" }
$ready = @($rows | ? { -not $_.Modules -and $_.NpMissingCount -eq 0 }).Count
$md += "`n**Fully importable and numpy-complete: $ready / $($rows.Count)**`n"
Set-Content $Out $md -Encoding utf8; "$ready / $($rows.Count) ready -> $Out"
