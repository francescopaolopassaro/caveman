$url = 'https://raw.githubusercontent.com/stopwords-iso/stopwords-iso/master/stopwords-en.txt'
try {
    $wc = New-Object System.Net.WebClient
    $c = $wc.DownloadString($url)
    $words = ($c -split "
") | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -notmatch '^\s*#' -and $_ -ne '' }
    Write-Host "Downloaded  words"
} catch {
    Write-Host "FAILED: "
}
