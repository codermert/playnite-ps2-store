<#
  BD Soft PS2 Store - catalog builder

  Builds src\catalog.json from open data sources:
    - Wikidata (CC0)         : PS2 titles, release year, genres, developers, publishers, English Wikipedia article
    - PCSX2 GameIndex.yaml   : PS2 serials and regions (used to match covers and widen the catalog)

  Usage:
    powershell -ExecutionPolicy Bypass -File tools\BuildCatalog.ps1 -GameIndex "D:\PCSX2\resources\GameIndex.yaml"
#>
param(
    [Parameter(Mandatory = $true)][string]$GameIndex,
    [string]$Out = (Join-Path $PSScriptRoot '..\src\catalog.json')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Invoke-Sparql([string]$query) {
    $url = 'https://query.wikidata.org/sparql?format=json&query=' + [uri]::EscapeDataString($query)
    $headers = @{ 'User-Agent' = 'BDSoftPS2Store-CatalogBuilder/1.0 (Playnite plugin)'; 'Accept' = 'application/sparql-results+json' }
    for ($try = 1; $try -le 3; $try++) {
        try {
            $r = Invoke-RestMethod $url -Headers $headers -TimeoutSec 180
            return $r.results.bindings
        } catch {
            if ($try -eq 3) { throw }
            Start-Sleep -Seconds (5 * $try)
        }
    }
}

function Get-NormalizedName([string]$name) {
    if (-not $name) { return '' }
    $s = $name.ToLowerInvariant()
    $s = [regex]::Replace($s, '\[[^\]]*\]|\([^\)]*\)', ' ')
    $s = $s.Replace('&', ' and ')
    $s = [regex]::Replace($s, "[^a-z0-9]+", ' ').Trim()
    $s = [regex]::Replace($s, '^the ', '')
    return $s.Replace(' ', '')
}

function Get-QId([string]$uri) { return $uri.Substring($uri.LastIndexOf('/') + 1) }

function Test-RealLabel([string]$label) { return ($label -and -not ($label -match '^Q\d+$')) }

# Genre label (Wikidata, English) -> store category
function Get-Categories([string[]]$genres) {
    $cats = New-Object System.Collections.Generic.List[string]
    foreach ($g in $genres) {
        $l = $g.ToLowerInvariant()
        if ($l -match 'role-playing|rpg|roguelike|dungeon crawl') { $cats.Add('RPG') }
        if ($l -match 'racing|driving|kart|motorcycle|vehicular') { $cats.Add('Racing') }
        if ($l -match 'fighting|beat ''em up|brawler|wrestling') { $cats.Add('Fighting') }
        if ($l -match 'sport|football|soccer|basketball|baseball|golf|tennis|hockey|skate|snowboard|surf|boxing|bowling|cricket|rugby|olympic|extreme') { $cats.Add('Sports') }
        if ($l -match 'horror') { $cats.Add('Horror') }
        if ($l -match 'adventure|point-and-click|visual novel|interactive') { $cats.Add('Adventure') }
        if ($l -match 'action|shooter|shoot ''em up|hack and slash|platform|stealth|third-person|first-person|run and gun|open world|beat ''em up') { $cats.Add('Action') }
    }
    return @($cats | Select-Object -Unique)
}

Write-Host 'Wikidata: base list...'
$base = Invoke-Sparql 'SELECT ?game ?gameLabel ?date ?article WHERE { ?game wdt:P400 wd:Q10680. OPTIONAL { ?game wdt:P577 ?date. } OPTIONAL { ?article schema:about ?game; schema:isPartOf <https://en.wikipedia.org/>. } SERVICE wikibase:label { bd:serviceParam wikibase:language "en". } }'
Write-Host 'Wikidata: genres...'
$genreRows = Invoke-Sparql 'SELECT ?game ?xLabel WHERE { ?game wdt:P400 wd:Q10680; wdt:P136 ?x. SERVICE wikibase:label { bd:serviceParam wikibase:language "en". } }'
Write-Host 'Wikidata: developers...'
$devRows = Invoke-Sparql 'SELECT ?game ?xLabel WHERE { ?game wdt:P400 wd:Q10680; wdt:P178 ?x. SERVICE wikibase:label { bd:serviceParam wikibase:language "en". } }'
Write-Host 'Wikidata: publishers...'
$pubRows = Invoke-Sparql 'SELECT ?game ?xLabel WHERE { ?game wdt:P400 wd:Q10680; wdt:P123 ?x. SERVICE wikibase:label { bd:serviceParam wikibase:language "en". } }'

$games = @{}
foreach ($r in $base) {
    $id = Get-QId $r.game.value
    $label = $r.gameLabel.value
    if (-not (Test-RealLabel $label)) { continue }
    if (-not $games.ContainsKey($id)) {
        $games[$id] = [ordered]@{ Id = $id; Title = $label; Year = 0; Wiki = ''; Genres = New-Object System.Collections.Generic.List[string]; Developers = New-Object System.Collections.Generic.List[string]; Publishers = New-Object System.Collections.Generic.List[string] }
    }
    $g = $games[$id]
    if ($r.date -and $r.date.value -match '^(\d{4})') {
        $y = [int]$Matches[1]
        if ($y -ge 1999 -and $y -le 2014 -and ($g.Year -eq 0 -or $y -lt $g.Year)) { $g.Year = $y }
    }
    if ($r.article -and -not $g.Wiki) { $g.Wiki = [uri]::UnescapeDataString($r.article.value.Substring($r.article.value.LastIndexOf('/') + 1)) }
}
foreach ($pair in @(@($genreRows, 'Genres'), @($devRows, 'Developers'), @($pubRows, 'Publishers'))) {
    foreach ($r in $pair[0]) {
        $id = Get-QId $r.game.value
        if (-not $games.ContainsKey($id)) { continue }
        $label = $r.xLabel.value
        if ((Test-RealLabel $label) -and -not $games[$id][$pair[1]].Contains($label)) { $games[$id][$pair[1]].Add($label) }
    }
}
Write-Host ("Wikidata games: {0}" -f $games.Count)

Write-Host 'PCSX2 GameIndex...'
$regionRank = @{ 'NTSC-U' = 0; 'PAL-E' = 1; 'PAL-M' = 1; 'PAL-U' = 1; 'PAL' = 1; 'NTSC-J' = 2 }
$serials = @{}   # normalized name -> @{ Serial; Region; Name; Rank }
$serial = $null; $name = $null; $nameEn = $null; $region = $null
$flush = {
    if ($serial) {
        $n = if ($nameEn) { $nameEn } else { $name }
        if ($n -and -not ($n -match '(?i)\b(demo|trial|kiosk|beta|sampler|preview|taikenban|disc 2|disc 3)\b')) {
            $rank = if ($regionRank.ContainsKey($region)) { $regionRank[$region] } else { 3 }
            if ($serial -match '^(SLUS|SCUS)') { $rank -= 0.5 }
            $key = Get-NormalizedName $n
            if ($key -and (-not $serials.ContainsKey($key) -or $rank -lt $serials[$key].Rank)) {
                $clean = [regex]::Replace($n, '\s*\[[^\]]*\]\s*', ' ').Trim()
                $serials[$key] = @{ Serial = $serial; Region = $region; Name = $clean; Rank = $rank }
            }
        }
    }
}
foreach ($line in [IO.File]::ReadLines($GameIndex, [Text.Encoding]::UTF8)) {
    if ($line -match '^([A-Z]{4}-\d{5}):') {
        & $flush
        $serial = $Matches[1]; $name = $null; $nameEn = $null; $region = $null
    } elseif ($serial -and $line -match '^  name: "(.*)"') { $name = $Matches[1] }
    elseif ($serial -and $line -match '^  name-en: "(.*)"') { $nameEn = $Matches[1] }
    elseif ($serial -and $line -match '^  region: "(.*)"') { $region = $Matches[1] }
}
& $flush
Write-Host ("GameIndex titles: {0}" -f $serials.Count)

$items = New-Object System.Collections.Generic.List[object]
$used = @{}
foreach ($g in $games.Values) {
    $key = Get-NormalizedName $g.Title
    if (-not $key -or $used.ContainsKey($key)) { continue }
    $used[$key] = $true
    $s = $serials[$key]
    $items.Add([ordered]@{
        Id = $g.Id
        Title = $g.Title
        Serial = if ($s) { $s.Serial } else { '' }
        Region = if ($s) { $s.Region } else { '' }
        Year = $g.Year
        Genres = @($g.Genres | Select-Object -First 4)
        Categories = @(Get-Categories $g.Genres)
        Developers = @($g.Developers | Select-Object -First 3)
        Publishers = @($g.Publishers | Select-Object -First 3)
        Wiki = $g.Wiki
    })
}
$matched = @($items | Where-Object { $_.Serial }).Count

# Widen the catalog with NTSC-U / PAL titles that Wikidata does not list
foreach ($key in $serials.Keys) {
    if ($used.ContainsKey($key)) { continue }
    $s = $serials[$key]
    if ($s.Rank -ge 2) { continue }
    $used[$key] = $true
    $items.Add([ordered]@{
        Id = $s.Serial; Title = $s.Name; Serial = $s.Serial; Region = $s.Region; Year = 0
        Genres = @(); Categories = @(); Developers = @(); Publishers = @(); Wiki = ''
    })
}

$sorted = @($items | Sort-Object { $_.Title })
$json = ConvertTo-Json -InputObject $sorted -Depth 4 -Compress
$dir = Split-Path $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
[IO.File]::WriteAllText($Out, $json, (New-Object Text.UTF8Encoding $false))
Write-Host ("catalog.json: {0} games ({1} Wikidata titles matched to a serial) -> {2}" -f $sorted.Count, $matched, (Resolve-Path $Out))
