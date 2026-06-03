$words = @("����","����","������")
$json = $words | ConvertTo-Json
[System.IO.File]::WriteAllText('wordlists\test_unicode.json', $json, [System.Text.UTF8Encoding]::new($true))
Write-Host 'Written'
