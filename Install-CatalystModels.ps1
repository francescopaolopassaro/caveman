# 1. Definisci la lista di tutte le lingue supportate
$languages = @("Afrikaans", "Albanian", "Arabic", "Armenian", "Basque", "Belarusian", "Bengali", "Bulgarian", "Catalan", "Chinese", "Croatian", "Czech", "Danish", "Dutch", "English", "Estonian", "Finnish", "French", "Galician", "German", "Greek", "Hebrew", "Hindi", "Hungarian", "Icelandic", "Indonesian", "Irish", "Italian", "Japanese", "Kannada", "Kazakh", "Korean", "Latin", "Latvian", "Lithuanian", "Macedonian", "Malay", "Marathi", "Norwegian", "Persian", "Polish", "Portuguese", "Romanian", "Russian", "Serbian", "Slovak", "Slovenian", "Spanish", "Swedish", "Tamil", "Telugu", "Thai", "Turkish", "Ukrainian", "Urdu", "Vietnamese")

# Forziamo l'aggiornamento del pacchetto core alla versione stabile più recente
Write-Host "Aggiornamento Catalyst Core..." -ForegroundColor Cyan
dotnet add package Catalyst

# Installazione modelli allineati
foreach ($lang in $languages) {
    $packageName = "Catalyst.Models.$lang"
    Write-Host "Installazione $packageName..." -ForegroundColor White
    dotnet add package $packageName
}

# 2. Tenta di recuperare la versione di Catalyst già installata per evitare conflitti
$currentVersion = dotnet list package | Select-String "Catalyst "
if ($currentVersion) {
    $version = ($currentVersion -split '\s+')[3]
    Write-Host "Rilevata versione Catalyst: $version" -ForegroundColor Cyan
} else {
    $version = "1.0.30952" # Versione di fallback se non rilevata
    Write-Host "Catalyst non rilevato, uso versione di default: $version" -ForegroundColor Yellow
}

Write-Host "Inizio installazione di $($languages.Count) pacchetti modelli..." -ForegroundColor Green

# 3. Ciclo di installazione
foreach ($lang in $languages) {
    $packageName = "Catalyst.Models.$lang"
    Write-Host "Installazione di $packageName..." -ForegroundColor White
    
    # Esegue il comando dotnet add package
    dotnet add package $packageName --version $version
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Errore durante l'installazione di $packageName. Potrebbe non esistere per questa versione." -ForegroundColor Red
    }
}

Write-Host "`nOperazione completata!" -ForegroundColor Green