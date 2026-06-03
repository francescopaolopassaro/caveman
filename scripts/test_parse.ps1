#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads function word lists for Caveman from stopwords-iso (GitHub).
    Generates JSON wordlist files for all languages with coverage.
    For uncovered languages, creates minimal seed-based wordlists from embedded data.

    Supported languages (53 total):
      - 44 from stopwords-iso download
      - 9 from seed data (Afrikaans, Belarusian, Icelandic, Kazakh,
        Kannada, Latin, Macedonian, Albanian, Tamil, Telugu)
.DESCRIPTION
    Downloads from: https://github.com/stopwords-iso/stopwords-iso
    Output: /wordlists/{iso3}.json files
#>
param(
    [string]$OutputDir = (Join-Path (Join-Path $PSScriptRoot "..") "wordlists")
)

$ErrorActionPreference = "Stop"
$baseUrl = "https://raw.githubusercontent.com/stopwords-iso/stopwords-iso/master/stopwords-{0}.txt"
if (!(Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

# ?????? Language master table ??????
$languages = @(
    @{iso3="afr"; iso1="af"; name="Afrikaans"}
    @{iso3="sqi"; iso1="sq"; name="Albanian"}
    @{iso3="ara"; iso1="ar"; name="Arabic"}
    @{iso3="hye"; iso1="hy"; name="Armenian"}
    @{iso3="eus"; iso1="eu"; name="Basque"}
    @{iso3="bel"; iso1="be"; name="Belarusian"}
    @{iso3="ben"; iso1="bn"; name="Bengali"}
    @{iso3="bul"; iso1="bg"; name="Bulgarian"}
    @{iso3="cat"; iso1="ca"; name="Catalan"}
    @{iso3="zho"; iso1="zh"; name="Chinese"}
    @{iso3="hrv"; iso1="hr"; name="Croatian"}
    @{iso3="ces"; iso1="cs"; name="Czech"}
    @{iso3="dan"; iso1="da"; name="Danish"}
    @{iso3="nld"; iso1="nl"; name="Dutch"}
    @{iso3="eng"; iso1="en"; name="English"}
    @{iso3="est"; iso1="et"; name="Estonian"}
    @{iso3="fin"; iso1="fi"; name="Finnish"}
    @{iso3="fra"; iso1="fr"; name="French"}
    @{iso3="glg"; iso1="gl"; name="Galician"}
    @{iso3="deu"; iso1="de"; name="German"}
    @{iso3="ell"; iso1="el"; name="Greek"}
    @{iso3="heb"; iso1="he"; name="Hebrew"}
    @{iso3="hin"; iso1="hi"; name="Hindi"}
    @{iso3="hun"; iso1="hu"; name="Hungarian"}
    @{iso3="isl"; iso1="is"; name="Icelandic"}
    @{iso3="ind"; iso1="id"; name="Indonesian"}
    @{iso3="gle"; iso1="ga"; name="Irish"}
    @{iso3="ita"; iso1="it"; name="Italian"}
    @{iso3="jpn"; iso1="ja"; name="Japanese"}
    @{iso3="kan"; iso1="kn"; name="Kannada"}
    @{iso3="kaz"; iso1="kk"; name="Kazakh"}
    @{iso3="kor"; iso1="ko"; name="Korean"}
    @{iso3="lat"; iso1="la"; name="Latin"}
    @{iso3="lav"; iso1="lv"; name="Latvian"}
    @{iso3="lit"; iso1="lt"; name="Lithuanian"}
    @{iso3="mkd"; iso1="mk"; name="Macedonian"}
    @{iso3="msa"; iso1="ms"; name="Malay"}
    @{iso3="mar"; iso1="mr"; name="Marathi"}
    @{iso3="nor"; iso1="no"; name="Norwegian"}
    @{iso3="fas"; iso1="fa"; name="Persian"}
    @{iso3="pol"; iso1="pl"; name="Polish"}
    @{iso3="por"; iso1="pt"; name="Portuguese"}
    @{iso3="ron"; iso1="ro"; name="Romanian"}
    @{iso3="rus"; iso1="ru"; name="Russian"}
    @{iso3="srp"; iso1="sr"; name="Serbian"}
    @{iso3="slk"; iso1="sk"; name="Slovak"}
    @{iso3="slv"; iso1="sl"; name="Slovenian"}
    @{iso3="spa"; iso1="es"; name="Spanish"}
    @{iso3="swe"; iso1="sv"; name="Swedish"}
    @{iso3="tam"; iso1="ta"; name="Tamil"}
    @{iso3="tel"; iso1="te"; name="Telugu"}
    @{iso3="tha"; iso1="th"; name="Thai"}
    @{iso3="tur"; iso1="tr"; name="Turkish"}
    @{iso3="ukr"; iso1="uk"; name="Ukrainian"}
    @{iso3="urd"; iso1="ur"; name="Urdu"}
    @{iso3="vie"; iso1="vi"; name="Vietnamese"}
)

# ?????? stopwords-iso coverage ??????
$hasCoverage = @(
    "af","ar","hy","eu","bn","bg","ca","zh","hr","cs","da",
    "nl","en","et","fi","fr","gl","de","el","he","hi",
    "hu","id","ga","it","ja","ko","lv","lt","ms","mr",
    "no","fa","pl","pt","ro","ru","sr","sk","sl","es",
    "sv","th","tr","uk","ur","vi"
)

# ?????? Seed word data (file paths for uncovered languages) ??????
$seedDir = Join-Path $PSScriptRoot "seed-data"
$seeds = @{}

# ?????? Generate JSON wordlist ??????
function New-Wordlist {
    param([hashtable]$lang, [string[]]$words, [string]$source)
    $name = $lang.name
    $iso3 = $lang.iso3
    $iso1 = $lang.iso1

    # Build function_words array (just use all words as-is for now ??? no POS categorisation)
    $functionWords = $words | ForEach-Object { $_.Trim().ToLowerInvariant() } `
        | Where-Object { $_ -ne '' } | Sort-Object -Unique

    $output = [ordered]@{
        iso3 = $iso3
        iso1 = $iso1
        name = $name
        function_words = @($functionWords)
        metadata = [ordered]@{
            generated = (Get-Date -Format "yyyy-MM-dd")
            source = $source
            word_count = $functionWords.Count
        }
    }

    $json = $output | ConvertTo-Json -Depth 3
    $outPath = Join-Path $OutputDir "$iso3.json"
    $json | Set-Content -Path $outPath -Encoding UTF8
    Write-Host "  ??? $iso3.json ($($functionWords.Count) words)" -ForegroundColor Magenta
}

# ??????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
# MAIN
# ??????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
$wc = New-Object System.Net.WebClient
$stats = @{stopwords=0; seed=0; skipped=0}

foreach ($lang in $languages) {
    $iso1 = $lang.iso1
    $iso3 = $lang.iso3
    Write-Host "Processing $($lang.name) ($iso3)..." -ForegroundColor Cyan

    # Try stopwords-iso
    if ($hasCoverage -contains $iso1) {
        $url = $baseUrl -f $iso1
        try {
            $content = $wc.DownloadString($url)
            $words = ($content -split "`n") | ForEach-Object { $_.Trim() } `
                | Where-Object { $_ -and $_ -notmatch '^\s*#' -and $_ -ne '' }
            if ($words.Count -gt 0) {
                New-Wordlist -lang $lang -words $words -source "stopwords-iso"
                $stats.stopwords++
                continue
            }
        } catch {
            # fallthrough to seed
        }
    }

    # Try seed data from scripts/seed-data/{iso3}.txt
    $seedPath = Join-Path $seedDir "$iso3.txt"
    if (Test-Path $seedPath) {
        $words = Get-Content $seedPath -Encoding UTF8 `
            | Where-Object { $_ -and $_ -ne '' }
        if ($words.Count -gt 0) {
            New-Wordlist -lang $lang -words $words -source "seed"
            $stats.seed++
            continue
        }
    }

    Write-Host "  SKIPPED ??? no word data for $iso3" -ForegroundColor Red
    $stats.skipped++
}

# ?????? Summary ??????
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  GENERATION COMPLETE" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  stopwords-iso:   $($stats.stopwords)" -ForegroundColor Green
Write-Host "  seed:            $($stats.seed)" -ForegroundColor Yellow
Write-Host "  skipped:         $($stats.skipped)" -ForegroundColor Red
Write-Host "  total expected:  $($languages.Count)" -ForegroundColor White

$actual = (Get-ChildItem "$OutputDir\*.json").Count
Write-Host "  files generated: $actual" -ForegroundColor White

$totalWords = 0
Get-ChildItem "$OutputDir\*.json" | ForEach-Object {
    try { $d = Get-Content $_.FullName -Raw | ConvertFrom-Json; $totalWords += $d.function_words.Count } catch {}
}
Write-Host "  total word entries: $totalWords" -ForegroundColor White
Write-Host "`nDone!" -ForegroundColor Cyan

