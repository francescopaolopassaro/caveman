param(
    [string]$OutputDir = (Join-Path (Join-Path $PSScriptRoot "..") "wordlists")
)

$ErrorActionPreference = "Stop"
$jsonUrl = "https://raw.githubusercontent.com/stopwords-iso/stopwords-iso/master/stopwords-iso.json"
if (!(Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

# ── Language map ISO 639-1 → ISO 639-3 ──
$iso1to3 = @{
    af="afr"; ar="ara"; hy="hye"; eu="eus"; bn="ben"; bg="bul"
    ca="cat"; zh="zho"; hr="hrv"; cs="ces"; da="dan"; nl="nld"
    en="eng"; et="est"; fi="fin"; fr="fra"; gl="glg"; de="deu"
    el="ell"; he="heb"; hi="hin"; hu="hun"; id="ind"; ga="gle"
    it="ita"; ja="jpn"; ko="kor"; lv="lav"; lt="lit"; ms="msa"
    mr="mar"; no="nor"; fa="fas"; pl="pol"; pt="por"; ro="ron"
    ru="rus"; sr="srp"; sk="slk"; sl="slv"; es="spa"; sv="swe"
    th="tha"; tr="tur"; uk="ukr"; ur="urd"; vi="vie"
}

# ── Download stopwords JSON ──
Write-Host "Downloading stopwords-iso JSON..." -ForegroundColor Cyan
$wc = New-Object System.Net.WebClient
$jsonText = $wc.DownloadString($jsonUrl)
$stopwords = $jsonText | ConvertFrom-Json
Write-Host "  Languages in JSON: $(($stopwords.PSObject.Properties | Measure-Object).Count)" -ForegroundColor Green

# ── Generate wordlist per language ──
$generated = 0
foreach ($prop in $stopwords.PSObject.Properties) {
    $iso1 = $prop.Name
    $iso3 = $iso1to3[$iso1]
    if (-not $iso3) { continue }  # skip languages not in Caveman

    $words = $prop.Value | ForEach-Object { $_.ToString().Trim().ToLowerInvariant() } `
        | Where-Object { $_ -ne '' } | Sort-Object -Unique

    Write-Host "  $iso3 ($iso1): $($words.Count) words" -ForegroundColor Gray

    $output = [ordered]@{
        iso3 = $iso3
        iso1 = $iso1
        function_words = @($words)
        metadata = [ordered]@{
            generated = (Get-Date -Format "yyyy-MM-dd")
            source = "stopwords-iso"
            word_count = $words.Count
        }
    }

    $json = $output | ConvertTo-Json -Depth 3
    $outPath = Join-Path $OutputDir "$iso3.json"
    $json | Set-Content -Path $outPath -Encoding UTF8
    $generated++
}

Write-Host "`nDone! Generated $generated wordlist files in $OutputDir" -ForegroundColor Cyan
