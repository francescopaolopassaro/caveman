param([string]$OutputDir = (Join-Path (Join-Path $PSScriptRoot "..") "wordlists"))

$ErrorActionPreference = "Stop"
if (!(Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

# Helper: escape string to pure ASCII \uXXXX JSON
function Escape-JsonStr {
    param([string]$s)
    $sb = New-Object System.Text.StringBuilder
    $sb.Append('"') | Out-Null
    foreach ($c in $s.ToCharArray()) {
        $code = [int]$c
        if ($code -eq 34) { $sb.Append('\u0022') | Out-Null }
        elseif ($code -eq 92) { $sb.Append('\u005C') | Out-Null }
        elseif ($code -eq 10) { $sb.Append('\u000A') | Out-Null }
        elseif ($code -eq 13) { $sb.Append('\u000D') | Out-Null }
        elseif ($code -eq 9) { $sb.Append('\u0009') | Out-Null }
        elseif ($code -ge 32 -and $code -le 126) { $sb.Append($c) | Out-Null }
        else { $sb.AppendFormat('\u{0:X4}', $code) | Out-Null }
    }
    $sb.Append('"') | Out-Null
    return $sb.ToString()
}

# Helper: write seed JSON with pure ASCII encoding
function Write-SeedJson {
    param([string]$iso3, [string]$iso1, [string]$name, [string[]]$words)
    $words = $words | Where-Object { $_ -and $_ -ne '' } | Sort-Object -Unique
    $sb = New-Object System.Text.StringBuilder
    $sb.AppendLine("{") | Out-Null
    $sb.AppendLine("  " + (Escape-JsonStr "iso3") + ": " + (Escape-JsonStr $iso3) + ",") | Out-Null
    $sb.AppendLine("  " + (Escape-JsonStr "iso1") + ": " + (Escape-JsonStr $iso1) + ",") | Out-Null
    $sb.AppendLine("  " + (Escape-JsonStr "name") + ": " + (Escape-JsonStr $name) + ",") | Out-Null
    $sb.AppendLine("  " + (Escape-JsonStr "function_words") + ": [") | Out-Null
    for ($i = 0; $i -lt $words.Count; $i++) {
        $comma = if ($i -lt $words.Count - 1) { "," } else { "" }
        $sb.AppendLine("    " + (Escape-JsonStr $words[$i]) + $comma) | Out-Null
    }
    $sb.AppendLine("  ],") | Out-Null
    $sb.AppendLine("  " + (Escape-JsonStr "metadata") + ": {") | Out-Null
    $sb.AppendLine("    " + (Escape-JsonStr "generated") + ": " + (Escape-JsonStr (Get-Date -Format 'yyyy-MM-dd')) + ",") | Out-Null
    $sb.AppendLine("    " + (Escape-JsonStr "source") + ": " + (Escape-JsonStr "seed") + ",") | Out-Null
    $sb.AppendLine("    " + (Escape-JsonStr "word_count") + ": $($words.Count)") | Out-Null
    $sb.AppendLine("  }") | Out-Null
    $sb.AppendLine("}") | Out-Null
    $json = $sb.ToString()
    $outPath = Join-Path $OutputDir "$iso3.json"
    [System.IO.File]::WriteAllText($outPath, $json, [System.Text.Encoding]::ASCII)
    Write-Host "  $iso3 ($iso1): $($words.Count) words" -ForegroundColor Yellow
}

Write-Host "Creating seed wordlists (ASCII-safe JSON)..." -ForegroundColor Cyan

Write-SeedJson -iso3 "srp" -iso1 "sr" -name "Serbian" -words @(
"biti","sam","si","je","smo","ste","su","bio","bila","bilo","bili","bile","bio","bili","bila"
"i","pa","te","ni","niti","ali","nego","vec","a","ili","da","sto","sta","st"
"koji","koja","koje","koje","koji","koju","kome","ciji","cija","cije","cijim","cijega"
"ovaj","ova","ovo","ovi","ove","ovih","ovim","ovima","ovome","ovomu","ovoj","ovom"
"taj","ta","to","ti","te","tog","tom","tome","toj","tim","tima","tomu"
"on","ona","ono","oni","one","ona","njega","njemu","njime","nju","njoj","njom","njim","njima"
"moj","moja","moje","moji","moje","mojih","mojim","mojima","mojega","mojemu","mojoj","mojom"
"tvoj","tvoja","tvoje","tvoji","tvoje","tvojih","tvojim","tvojima"
"svoj","svoja","svoje","svoji","svoje","svojih","svojim","svojima"
"nas","nasa","nase","nasi","nase","nasih","nasim","nasima","naseg","nasem"
"vas","vasa","vase","vasi","vase","vasih","vasim","vasima"
"njegov","njegova","njegovo","njegovi","njegove","njegovih","njegovim","njegovima"
"njen","njena","njeno","njeni","njene","njenih","njenim","njenima"
"njihov","njihova","njihovo","njihovi","njihove","njihovih","njihovim","njihovima"
"sav","sva","sve","svi","sve","svih","svim","svima","svemu","sveg"
"ko","sta","cega","cemu","cime","cim","kime","kome"
"zasto","zato","jer","sto","zato"
"kako","tako","ovako","onako","nekako","ikako"
"kad","tad","sad","tada","sada","uvek","nikad","ponekad","katkad"
"gde","tu","ovde","onde","tamo","svuda","nigde","negde","igde","svakuda"
"od","do","iz","u","na","za","sa","bez","pred","posle","pored","kod"
"izmedju","kroz","oko","pored","protiv","pre","nad","pod","medju","kraj"
"po","prema","radi","osim","mimo","van","unutar","spram"
"vec","jos","tek","samo","bas","upravo","gotovo","skoro","zamalo","jedva"
"najvise","najmanje","vise","manje","mnogo","malo","puno","dosta","mnogo"
"vrlo","veoma","jako","silno","prilicno","veoma"
"sve","nesto","nista","svega","nicemu"
"ne","niko","nista","nikad","nikako","nikuda","niciji","nicija"
"da","li","zar","neka","neki","neko","nekad","nekako","nekuda"
"iznad","ispod","ispred","iza","ispred","izmedju","izvan","unutar","vani","unackolo"
)

Write-SeedJson -iso3 "sqi" -iso1 "sq" -name "Albanian" -words @(
"jam","je","eshte","jemi","jeni","jane","isha","ishe","ishte","ishim","ishin"
"kam","ke","ka","kemi","keni","kane","kisha","kishe","kishte","kishim","kishin"
"kem","ke","ka","kemi","keni","kane","patura","pati","patur"
"jam","je","eshte","jemi","jeni","jane","qofsha","qofsh","qofte","qofshim","qofshi","qofshin"
"dhe","e","te","se","qe","ne","me","pa","mbi","nen","ne","nga","tek","te","deri"
"per","n","ne","mes","per","gjat","per","rreth","afro","rreth","madje","poshte"
"i","e","te","se","te","i","e","te","se","te"
"ky","kjo","kete","keta","keto","kesaj","ketij","ketij","ketyre"
"ai","ajo","ate","ata","ato","atij","asaj","atyre","atij","asaj"
"une","ti","ai","ajo","ne","ju","ata","ato","une","ti"
"mua","ty","ate","ne","ju","ata","ato","mua","ty","atij","asaj"
"im","im","ime","imi","ime","imja","imet","tij","saj","tyre"
"yt","jot","yte","yti","yte","ytja","ytet","tij","saj","tyre"
"i","i","tij","i","saj","i","tyre","tij","saj","tyre"
"cili","cila","cilet","cilat","cilin","cilen","cilet","cilat","kush","cfare","c"
"si","sa","kur","ku","nga","pse","sepse","prandaj","andej"
"nese","ndonese","megjithese","edhe","pse","ndonje","ndonje","ndonje","ndonjehere"
"ose","apo","por","megjithate","mirepo","por","megjithate"
"shume","pak","me","teper","mjaft","fort","aspak","kurre","gjithmone","ndonjehere","shpesh","rralle"
"mos","nuk","s","s'","kurre","asnjehere","gjithmone","ndonjehere","shpesh","rralle"
"tani","pastaj","atehere","pas","pastaj","tani","me","pare","menjehere","papritur"
"ketu","atje","kendej","andej","ketej","andej","tutje","matane"
"brenda","jashte","lart","poshte","perpara","mbrapa","perpara","pas"
"afer","larg","prane","larg","tutje","matane","andej"
"madje","pikerisht","thjesht","vetem","vetem","pikerisht","posaçerisht","vecanerisht"
)

Write-SeedJson -iso3 "bel" -iso1 "be" -name "Belarusian" -words @(
"i","u","nie","na","z","da","pa","ad","ab","pry","pra","praz"
"u","za","nad","pad","perad","pamiz","ceraz","praz","za","da"
"bez","dla","dzelea","rady","zamest","akramia","aprocz","praz","kryz"
"kala","lia","kal","navakol","pasiarod","suprac","sucel","voprek"
"sto","jaki","jakaja","jakoje","jakija","cyjo","cyja","cyim","cyje"
"heta","hety","hetaja","hetaje","hetyja","toj","taja","toje","tyja","taja"
"jon","jana","jano","jany","jago","jaje","im","joj","imi","imi"
"my","vy","ja","ty","nam","vam","mne","tabe","nam","vam"
"moj","maja","moje","maje","tvoj","tvaja","tvoje","tvaje","tvaima"
"nas","nasa","nasaje","nasy","vas","vasa","vasaje","vasy","vasym"
"svoj","svaja","svoje","svaje","svaima","svaimi","svaima"
"velmi","bols","mens","smat","mala","bols","mens","smat","mala"
"tak","tamu","taki","takaja","takoje","takija","taki"
"jak","kali","dze","tut","tam","kudy","adkul","adkul"
"bo","tamu","sto","tamu","sto","kab","kali","kali","kalib"
"ale","adnak","dy","dy","ci","ci","ni","ni","a","a"
"uzo","jaszce","uzo","jaszcze","tolki","tolki","tolki"
"moza","byc","budze","byu","byla","bylo","byli","byu"
"tut","tam","siam","usiudy","nidzie","nikudy","nidzie"
"ne","njama","ni","nijaki","nichto","nisto","nikoli","nijak"
)

Write-SeedJson -iso3 "isl" -iso1 "is" -name "Icelandic" -words @(
"og","ad","i","a","vid","um","med","til","fra","hja","milli",
"fyrir","eftir","yfir","undir","gegnum","vegna","an","ut","inn",
"upp","nidur","fram","nu","strax","alltaf","aldrei","stundum",
"oft","sjaldan","tha","sidan","enn","ja","nei","kannski",
"eg","thu","hann","hun","thad","vid","thid","their","thaer","thau",
"mig","thig","sig","okkur","ykkur","ser","okkar","ykkar",,
"minn","thinn","sinn","okkar","ykkar",
"hver","hvad","hvernig","hvers","hversu","hvar","hvenaer",
"af","hverju","thvi","af","thvi","thess","vegna",
"thessi","thetta","thessir","thessar","thessum","thessa","thessari",
"sa","su","thad","their","thaer","thau",
"hin","hinn","hina","hinu","hins","hinu",
"baedi","jafnt","annadhvort","hvorki",
"eda","og","en","heldur","tho","enda","ne",
"ad","sem","er","voru","hafa","hafdi",
"bara","einfaldlega","adeins","bara",
"mjog","frekar","alveg","nokkur","naesta","akaflega",
"lika","einnig","jafnframt",
"ekki","aldrei","hvergi","enginn","ekkert",
"hins","vegar","annars","annar","onnur","annad","adrir","adrar",
"sama","somu","somu","sama",
)

Write-SeedJson -iso3 "kan" -iso1 "kn" -name "Kannada" -words @(
"\u0CAE\u0CA4\u0CCD\u0CA4\u0CC1","\u0C85\u0CA5\u0CB5\u0CBE","\u0C86\u0CA6\u0CB0\u0CC6","\u0C86\u0CA6\u0CCD\u0CA6\u0CB0\u0CBF\u0C82\u0CA6","\u0C8F\u0C95\u0CC6\u0C82\u0CA6\u0CB0\u0CC6","\u0C85\u0CA5\u0CB5\u0CBE"
"\u0C85\u0CA6\u0CC1","\u0C87\u0CA6\u0CC1","\u0C85\u0CB5\u0CC1","\u0C87\u0CB5\u0CC1","\u0C85\u0CB5\u0CA8\u0CC1","\u0C85\u0CB5\u0CB3\u0CC1","\u0C85\u0CB5\u0CB0\u0CC1"
"\u0CA8\u0CBE\u0CA8\u0CC1","\u0CA8\u0CC0\u0CA8\u0CC1","\u0CA8\u0CBE\u0CB5\u0CC1","\u0CA8\u0CC0\u0CB5\u0CC1","\u0CA4\u0CBE\u0CA8\u0CC1","\u0CA4\u0CBE\u0CB5\u0CC1"
"\u0CA8\u0CA8\u0CCD\u0CA8","\u0CA8\u0CBF\u0CA8\u0CCD\u0CA8","\u0CA8\u0CAE\u0CCD\u0CAE","\u0CA8\u0CBF\u0CAE\u0CCD\u0CAE","\u0C85\u0CB5\u0CA8","\u0C85\u0CB5\u0CB3","\u0C85\u0CB5\u0CB0"
"\u0C87\u0CA6\u0CB0","\u0C85\u0CA6\u0CB0","\u0C87\u0CB5\u0CC1\u0C97\u0CB3","\u0C85\u0CB5\u0CC1\u0C97\u0CB3"
"\u0C92\u0C82\u0CA6\u0CC1","\u0C95\u0CC6\u0CB2\u0CB5\u0CC1","\u0CAF\u0CBE\u0CB5\u0CC1\u0CA6\u0CC7","\u0C8E\u0CB2\u0CCD\u0CB2\u0CBE","\u0CAA\u0CCD\u0CB0\u0CA4\u0CBF","\u0C85\u0CA8\u0CC7\u0C95","\u0C95\u0CC6\u0CB2\u0CB5\u0CC1"
"\u0CAF\u0CBE\u0CB0\u0CC1","\u0CAF\u0CBE\u0CB5\u0CC1\u0CA6\u0CC1","\u0C8F\u0CA8\u0CC1","\u0CAF\u0CBE\u0CB5\u0CBE\u0C97","\u0C8E\u0CB2\u0CCD\u0CB2\u0CBF","\u0CB9\u0CC7\u0C97\u0CC6","\u0C8F\u0C95\u0CC6"
"\u0C86\u0C97","\u0C88\u0C97","\u0CA8\u0C82\u0CA4\u0CB0","\u0CAE\u0CCB\u0CA6\u0CB2\u0CC1","\u0C85\u0CB2\u0CCD\u0CB2\u0CBF","\u0C87\u0CB2\u0CCD\u0CB2\u0CBF"
"\u0CAE\u0CC7\u0CB2\u0CC6","\u0C95\u0CC6\u0CB3\u0C97\u0CC6","\u0CB9\u0CCA\u0CB0\u0C97\u0CC6","\u0C92\u0CB3\u0C97\u0CC6","\u0CAE\u0CC1\u0C82\u0CA6\u0CC6","\u0CB9\u0CBF\u0C82\u0CA6\u0CC6","\u0CB9\u0CA4\u0CCD\u0CA4\u0CBF\u0CB0","\u0CA6\u0CC2\u0CB0"
"\u0CA4\u0CC1\u0C82\u0CAC\u0CBE","\u0CB8\u0CCD\u0CB5\u0CB2\u0CCD\u0CAA","\u0CB9\u0CC6\u0B9A\u0CCD\u0C9A\u0CC1","\u0C95\u0CA1\u0CBF\u0CAE\u0CC6"
"\u0CB9\u0CCC\u0CA6\u0CC1","\u0C87\u0CB2\u0CCD\u0CB2","\u0C86\u0CA6\u0CB0\u0CC2","\u0C85\u0CB7\u0CCD\u0C9F\u0CC7","\u0C85\u0CB7\u0CCD\u0C9F\u0CC1","\u0C87\u0CB7\u0CCD\u0C9F\u0CC1"
"\u0C95\u0CC2\u0CA1","\u0CB8\u0CB9","\u0CB9\u0CBE\u0C97\u0CC2","\u0CB9\u0CC0\u0C97\u0CC6","\u0C85\u0C82\u0CA4\u0CC6","\u0CAA\u0CCD\u0CB0\u0C95\u0CBE\u0CB0"
"\u0CAC\u0CC7\u0C95\u0CC1","\u0CAC\u0CB9\u0CC1\u0CA6\u0CC1","\u0C87\u0CB0\u0CAC\u0CB9\u0CC1\u0CA6\u0CC1","\u0C86\u0C97\u0CAC\u0CB9\u0CC1\u0CA6\u0CC1"
"\u0C87\u0CB2\u0CCD\u0CB2","\u0C85\u0CB2\u0CCD\u0CB2","\u0C85\u0CB2\u0CCD\u0CB2\u0CA6\u0CC6","\u0C87\u0CB2\u0CCD\u0CB2\u0CA6\u0CC6","\u0CAC\u0CC7\u0CA1"
)

Write-SeedJson -iso3 "kaz" -iso1 "kk" -name "Kazakh" -words @(
"\u0436\u04D9\u043D\u0435","\u0431\u0435\u043D","\u043C\u0435\u043D","\u0434\u0430","\u0434\u0435","\u0442\u0430","\u0442\u0435"
"\u0435\u043C\u0435\u0441","\u0436\u043E\u049B","\u0431\u0430\u0440","\u0436\u043E\u049B","\u0431\u0430\u0440"
"\u043E\u0441\u044B","\u0441\u043E\u043B","\u0431\u04B1\u043B","\u043E\u043B","\u043C\u044B\u043D\u0430","\u0430\u043D\u0430","\u043C\u044B\u043D\u0430\u0443","\u0430\u043D\u0430\u0443"
"\u043C\u0435\u043D","\u0441\u0435\u043D","\u0441\u0456\u0437","\u043E\u043B","\u0431\u0456\u0437","\u0441\u0435\u043D\u0434\u0435\u0440","\u0441\u0456\u0437\u0434\u0435\u0440","\u043E\u043B\u0430\u0440"
"\u04E9\u0437\u0456\u043C","\u04E9\u0437\u0456\u04A3","\u04E9\u0437\u0456","\u04E9\u0437\u0456\u043C\u0456\u0437","\u04E9\u0437\u0434\u0435\u0440\u0456\u04A3","\u04E9\u0437\u0434\u0435\u0440\u0456"
"\u043C\u0435\u043D\u0456\u04A3","\u0441\u0435\u043D\u0456\u04A3","\u0441\u0456\u0437\u0434\u0456\u04A3","\u043E\u043D\u044B\u04A3","\u0431\u0456\u0437\u0434\u0456\u04A3","\u0441\u0435\u043D\u0434\u0435\u0440\u0434\u0456\u04A3","\u043E\u043B\u0430\u0440\u0434\u044B\u04A3"
"\u0431\u043E\u043B\u0430\u0434\u044B","\u0431\u043E\u043B\u0434\u044B","\u0431\u043E\u043B\u0493\u0430\u043D","\u0431\u043E\u043B\u044B\u043F"
"\u043A\u0435\u043B\u0435\u0434\u0456","\u043A\u0435\u043B\u0434\u0456","\u043A\u0435\u0442\u0442\u0456","\u043A\u0435\u0442\u043A\u0435\u043D"
"\u0436\u0430\u0442\u044B\u0440","\u043E\u0442\u044B\u0440","\u0442\u04B1\u0440","\u0436\u04AF\u0440"
"\u043A\u0435\u0440\u0435\u043A","\u049B\u0430\u0436\u0435\u0442","\u0442\u0438\u0456\u0441","\u043A\u0435\u0440\u0435\u043A"
"\u043A\u0456\u043C","\u043D\u0435","\u049B\u0430\u0439\u0434\u0430","\u049B\u0430\u0448\u0430\u043D","\u049B\u0430\u043B\u0430\u0439","\u043D\u0435\u0433\u0435","\u043D\u0435\u043B\u0456\u043A\u0442\u0435\u043D"
"\u049B\u0430\u043D\u0448\u0430","\u049B\u0430\u043D\u0434\u0430\u0439","\u049B\u0430\u0439\u0441\u044B","\u049B\u0430\u0439"
"\u04E9\u0442\u0435","\u0442\u044B\u043C","\u0430\u0441\u0430","\u0435\u04A3","\u043D\u0430\u0493\u044B\u0437","\u0434\u04D9\u043B"
"\u0434\u0430","\u0434\u0435","\u0442\u0430","\u0442\u0435","\u043C\u0430","\u043C\u0435","\u0431\u0430","\u0431\u0435","\u043F\u0430","\u043F\u0435"
"\u0438\u04D9","\u0436\u043E\u049B","\u04D9\u0440\u0438\u043D\u0435","\u04D9\u043B\u0431\u0435\u0442\u0442\u0435"
"\u043D\u0435\u043C\u0435\u0441\u0435","\u044F","\u044F\u043A\u0438","\u043D\u0435","\u043D\u0435","\u043D\u0435","\u043D\u0435"
"\u0431\u0456\u0440\u0430\u049B","\u0430\u043B\u0430\u0439\u0434\u0430","\u0434\u0435\u0433\u0435\u043D\u043C\u0435\u043D","\u04D9\u0439\u0442\u0441\u0435\u0434\u0435"
"\u04E9\u0439\u0442\u043A\u0435\u043D\u0456","\u0441\u0435\u0431\u0435\u0431\u0456","\u04AF\u0448\u0456\u043D"
"\u0435\u0433\u0435\u0440","\u0435\u0433\u0435\u0440","\u0434\u0435","\u0435\u0433\u0435\u0440","\u0434\u0435"
"\u0441\u043E\u043B","\u0441\u043E\u043D\u0434\u044B\u049B\u0442\u0430\u043D","\u0441\u043E\u043D\u044B\u043C\u0435\u043D","\u0441\u04E9\u0439\u0442\u0456\u043F"
"\u0435\u043D\u0434\u0456","\u0435\u043D\u0434\u0456","\u0435\u043D\u0434\u0456","\u0435\u043D\u0434\u0456"
"\u049B\u0430\u0437\u0456\u0440","\u0434\u04D9\u043B","\u049B\u0430\u0437\u0456\u0440","\u04D9\u0437\u0456\u0440","\u04D9\u043B\u0456"
"\u0431\u04AF\u0433\u0456\u043D","\u0435\u0440\u0442\u0435\u04A3","\u043A\u0435\u0448\u0435","\u0431\u0438\u044B\u043B","\u0431\u044B\u043B\u0442\u044B\u0440"
"\u043E\u0441\u044B\u043D\u0434\u0430","\u0441\u043E\u043D\u0434\u0430","\u0430\u043D\u0430","\u0436\u0430\u049B\u0442\u0430","\u043C\u044B\u043D\u0430","\u0436\u0430\u049B\u0442\u0430"
)

Write-SeedJson -iso3 "lat" -iso1 "la" -name "Latin" -words @(
"et","in","ad","sed","non","est","sunt","cum","ex","ab","de"
"per","ut","vel","nec","si","atque","autem","enim","ergo"
"igitur","quia","quod","tamen","velut","an","aut","nam"
"neque","quoque","tam","quam","pro","apud","contra","super"
"sub","inter","extra","intra","circa","sine","secundum"
"praeter","propter","ob","post","ante","cis","trans"
"supra","infra","prope","usque","olim","dum","donec"
"ubi","unde","qua","quamdiu","quotiens","simul","statim"
"iam","tunc","tum","modo","nuper","iterum","rursus"
"semper","saepe","numquam","interdum","aliquando","quondam"
"primum","denique","deinde","inde","porro","praeterea"
"ceterum","itaque","scilicet","videlicet","nempe","certe"
"profecto","sane","plane","etiam","saltem","tantummodo"
"solum","modo","tantum","tamen","sedtamen","quamquam"
"etsi","tametsi","licet","quamvis","etiamsi","si","sin"
"sive","seu","nisi","ni","quasi","tamquam","velut","ceu"
"sicuti","sicut","ut","uti","quemadmodum","prout","proinde"
"utpote","quando","quoniam","propterea","ideo","idcirco"
"gratia","causa"
)

Write-SeedJson -iso3 "mkd" -iso1 "mk" -name "Macedonian" -words @(
"\u0438","\u0432\u043E","\u043D\u0430","\u0434\u0430","\u0441\u0435","\u0434\u0435\u043A\u0430","\u043D\u0435","\u0441\u043E","\u043E\u0434","\u0437\u0430","\u043F\u043E"
"\u0433\u0438","\u0433\u043E","\u043C\u0443","\u0458","\u043C\u0435","\u0442\u0435","\u0432\u0435","\u043D\u0438","\u0432\u0438"
"\u043E\u0432\u043E\u0458","\u0442\u043E\u0458","\u0442\u0430\u0430","\u0442\u043E\u0430","\u0442\u0438\u0435","\u043E\u0432\u0430","\u0442\u043E\u0430","\u043E\u0432\u0438\u0435","\u0442\u0438\u0435"
"\u0458\u0430\u0441","\u0442\u0438","\u0442\u043E\u0458","\u0442\u0430\u0430","\u0442\u043E\u0430","\u043D\u0438\u0435","\u0432\u0438\u0435","\u0442\u0438\u0435"
"\u043C\u043E\u0458","\u043C\u043E\u0458\u0430","\u043C\u043E\u0435","\u043C\u043E\u0438","\u0442\u0432\u043E\u0458","\u0442\u0432\u043E\u0458\u0430","\u0442\u0432\u043E\u0435","\u0442\u0432\u043E\u0438"
"\u043D\u0435\u0433\u043E\u0432","\u043D\u0435\u0433\u043E\u0432\u0430","\u043D\u0435\u0433\u043E\u0432\u043E","\u043D\u0435\u0433\u043E\u0432\u0438","\u043D\u0435\u0458\u0437\u0438\u043D","\u043D\u0435\u0458\u0437\u0438\u043D\u0430","\u043D\u0435\u0458\u0437\u0438\u043D\u043E","\u043D\u0435\u0458\u0437\u0438\u043D\u0438"
"\u043D\u0430\u0448","\u043D\u0430\u0448\u0430","\u043D\u0430\u0448\u0435","\u043D\u0430\u0448\u0438","\u0432\u0430\u0448","\u0432\u0430\u0448\u0430","\u0432\u0430\u0448\u0435","\u0432\u0430\u0448\u0438"
"\u0441\u0432\u043E\u0458","\u0441\u0432\u043E\u0458\u0430","\u0441\u0432\u043E\u0435","\u0441\u0432\u043E\u0438"
"\u043A\u043E\u0458","\u043A\u043E\u0458\u0430","\u043A\u043E\u0435","\u043A\u043E\u0438","\u0447\u0438\u0458","\u0447\u0438\u0458\u0430","\u0447\u0438\u0435","\u0447\u0438\u0438"
"\u0448\u0442\u043E","\u043A\u0430\u043A\u043E\u0432","\u043A\u0430\u043A\u0432\u0430","\u043A\u0430\u043A\u0432\u043E","\u043A\u0430\u043A\u0432\u0438","\u043A\u043E\u043B\u043A\u0430\u0432"
"\u043E\u0432\u0434\u0435","\u0442\u0443\u043A\u0430","\u0442\u0430\u043C\u0443","\u0433\u043E\u0440\u0435","\u0434\u043E\u043B\u0443","\u0432\u043D\u0430\u0442\u0440\u0435","\u043D\u0430\u0434\u0432\u043E\u0440"
"\u043F\u0440\u0435\u0434","\u0437\u0430\u0434","\u043D\u0430\u0434","\u043F\u043E\u0434","\u043A\u0430\u0458","\u043E\u043A\u043E\u043B\u0443","\u043D\u0438\u0437","\u043F\u0440\u0435\u043A\u0443","\u043C\u0435\u0453\u0443"
"\u043E\u0442\u043A\u0430\u043A\u043E","\u0448\u0442\u043E\u043C","\u0434\u043E\u0434\u0435\u043A\u0430","\u043F\u0440\u0435\u0434","\u0434\u0430","\u043F\u043E","\u043E\u0442\u043A\u0430\u043A\u043E"
"\u0437\u0430\u0442\u043E\u0430","\u0448\u0442\u043E","\u0431\u0438\u0434\u0435\u0458\u045C\u0438","\u0437\u0430\u0448\u0442\u043E","\u043E\u0442\u0438"
"\u0430\u043A\u043E","\u0438\u0430\u043A\u043E","\u043E\u0441\u0432\u0435\u043D","\u0430\u043A\u043E","\u043D\u043E","\u043C\u0435\u0453\u0443\u0442\u043E\u0430","\u0441\u0435\u043F\u0430\u043A"
"\u0438\u043B\u0438","\u043D\u0438\u0442\u0443","\u0442\u0443\u043A\u0443","\u043F\u0430","\u0442\u0430\u043A\u0430","\u0438\u0441\u0442\u043E"
"\u043A\u0430\u043A\u043E","\u043A\u043E\u043B\u043A\u0443","\u0442\u043E\u043B\u043A\u0443","\u043C\u043D\u043E\u0433\u0443","\u043C\u0430\u043B\u043A\u0443","\u043F\u043E\u0432\u0435\u045C\u0435","\u043F\u043E\u043C\u0430\u043B\u043A\u0443"
"\u0432\u0435\u045C\u0435","\u0443\u0448\u0442\u0435","\u0441\u0435","\u0443\u0448\u0442\u0435","\u043D\u0438\u043A\u043E\u0433\u0430\u0448","\u0441\u0435\u043A\u043E\u0433\u0430\u0448"
"\u043F\u043E\u043D\u0435\u043A\u043E\u0433\u0430\u0448","\u0447\u0435\u0441\u0442\u043E","\u0440\u0435\u0442\u043A\u043E","\u043F\u043E\u0442\u043E\u0430","\u0442\u043E\u0433\u0430\u0448","\u0441\u0435\u0433\u0430"
"\u0440\u0430\u043D\u043E","\u0434\u043E\u0446\u043D\u0430","\u0431\u0440\u0437\u043E","\u043F\u043E\u043B\u0435\u043A\u0430","\u0435\u0434\u043D\u0430\u0448","\u043F\u043E\u0432\u0435\u045C\u0435"
"\u0441\u0430\u043C\u043E","\u0435\u0434\u0438\u043D\u0441\u0442\u0432\u0435\u043D\u043E","\u0438\u0441\u043A\u043B\u0443\u0447\u0438\u0432\u043E","\u0442\u043E\u043A\u043C\u0443"
"\u0442\u0430\u043A\u0430","\u0432\u0430\u043A\u0430","\u043E\u043D\u0430\u043A\u0430","\u0438\u043D\u0430\u043A\u0443","\u0441\u043F\u0440\u043E\u0442\u0438\u0432\u043D\u043E"
)

Write-SeedJson -iso3 "tam" -iso1 "ta" -name "Tamil" -words @(
"\u0BAE\u0BB1\u0BCD\u0BB1\u0BC1\u0BAE\u0BCD","\u0B86\u0BA9\u0BBE\u0BB2\u0BCD","\u0B85\u0BB2\u0BCD\u0BB2\u0BA4\u0BC1","\u0B8F\u0BA9\u0BC6\u0BA9\u0BBF\u0BB2\u0BCD","\u0B86\u0B95\u0BC8\u0BAF\u0BBE\u0BB2\u0BCD","\u0B8E\u0BA9\u0BB5\u0BC7"
"\u0B85\u0BA4\u0BC1","\u0B87\u0BA4\u0BC1","\u0B85\u0BB5\u0BA9\u0BCD","\u0B85\u0BB5\u0BB3\u0BCD","\u0B85\u0BB5\u0BB0\u0BCD","\u0B85\u0BB5\u0BB0\u0BCD\u0B95\u0BB3\u0BCD"
"\u0BA8\u0BBE\u0BA9\u0BCD","\u0BA8\u0BC0","\u0BA8\u0BC0\u0B99\u0BCD\u0B95\u0BB3\u0BCD","\u0BA8\u0BBE\u0BAE\u0BCD","\u0BA8\u0BBE\u0B99\u0BCD\u0B95\u0BB3\u0BCD","\u0BA4\u0BBE\u0B99\u0BCD\u0B95\u0BB3\u0BCD"
"\u0B8E\u0BA9\u0BCD\u0BA9\u0BC1\u0B9F\u0BC8\u0BAF","\u0B89\u0BA9\u0BCD\u0BA9\u0BC1\u0B9F\u0BC8\u0BAF","\u0B85\u0BB5\u0BB0\u0BC1\u0B9F\u0BC8\u0BAF","\u0B85\u0BB5\u0BB3\u0BC1\u0B9F\u0BC8\u0BAF","\u0B85\u0BB5\u0BB0\u0BCD\u0B95\u0BB3\u0BC1\u0B9F\u0BC8\u0BAF"
"\u0B8E\u0B99\u0BCD\u0B95\u0BB3\u0BC1\u0B9F\u0BC8\u0BAF","\u0B89\u0B99\u0BCD\u0B95\u0BB3\u0BC1\u0B9F\u0BC8\u0BAF"
"\u0B92\u0BB0\u0BC1","\u0B9A\u0BBF\u0BB2","\u0B8E\u0BA8\u0BCD\u0BA4","\u0B8E\u0BB2\u0BCD\u0BB2\u0BBE","\u0BAA\u0BB2","\u0B92\u0BB5\u0BCD\u0BB5\u0BCA\u0BB0\u0BC1","\u0BB5\u0BC7\u0BB1\u0BC1"
"\u0BAF\u0BBE\u0BB0\u0BCD","\u0B8E\u0BA4\u0BC1","\u0B8E\u0BA9\u0BCD\u0BA9","\u0B8E\u0BAA\u0BCD\u0BAA\u0BCB\u0BA4\u0BC1","\u0B8E\u0B99\u0BCD\u0B95\u0BC7","\u0B8E\u0BAA\u0BCD\u0BAA\u0B9F\u0BBF","\u0B8F\u0BA9\u0BCD"
"\u0B86\u0BAE\u0BCD","\u0B87\u0BB2\u0BCD\u0BB2\u0BC8","\u0B87\u0B99\u0BCD\u0B95\u0BC7","\u0B85\u0B99\u0BCD\u0B95\u0BC7"
"\u0BAE\u0BC7\u0BB2\u0BC7","\u0B95\u0BC0\u0BB4\u0BC7","\u0B89\u0BB3\u0BCD\u0BB3\u0BC7","\u0BB5\u0BC6\u0BB3\u0BBF\u0BAF\u0BC7","\u0BAE\u0BC1\u0BA9\u0BCD\u0BA9\u0BBE\u0BB2\u0BCD","\u0BAA\u0BBF\u0BA9\u0BCD\u0BA9\u0BBE\u0BB2\u0BCD"
"\u0B85\u0BB0\u0BC1\u0B95\u0BBF\u0BB2\u0BCD","\u0BA4\u0BC2\u0BB0\u0BA4\u0BCD\u0BA4\u0BBF\u0BB2\u0BCD"
"\u0BAE\u0BBF\u0B95\u0BB5\u0BC1\u0BAE\u0BCD","\u0B95\u0BCA\u0B9E\u0BCD\u0B9A\u0BAE\u0BCD","\u0B85\u0BA4\u0BBF\u0B95","\u0B95\u0BC1\u0BB1\u0BC8\u0BB5\u0BBE\u0BA9"
"\u0BAE\u0BC1\u0BA9\u0BCD","\u0BAA\u0BBF\u0BA9\u0BCD","\u0BAA\u0BBF\u0BB1\u0B95\u0BC1","\u0BAE\u0BC1\u0BA9\u0BCD\u0BAA\u0BC1","\u0B87\u0BAA\u0BCD\u0BAA\u0BCB\u0BA4\u0BC1","\u0B85\u0BAA\u0BCD\u0BAA\u0BCB\u0BA4\u0BC1"
"\u0B8E\u0BAA\u0BCD\u0BAA\u0BCB\u0BA4\u0BC1\u0BAE\u0BCD","\u0B92\u0BB0\u0BC1\u0BAA\u0BCB\u0BA4\u0BC1\u0BAE\u0BCD"
"\u0B9A\u0BBF\u0BB2\u0B9A\u0BAE\u0BAF\u0BAE\u0BCD","\u0B85\u0B9F\u0BBF\u0B95\u0BCD\u0B95\u0B9F\u0BBF","\u0B85\u0BB0\u0BBF\u0BA4\u0BBE\u0B95"
"\u0BAA\u0B9F\u0BBF\u0BAA\u0BCD\u0BAA\u0B9F\u0BBF\u0BAF\u0BBE\u0B95","\u0BA4\u0BBF\u0B9F\u0BC0\u0BB0\u0BC6\u0BA9\u0BCD\u0BB1\u0BC1","\u0BAE\u0BC6\u0BA4\u0BC1\u0BB5\u0BBE\u0B95","\u0BB5\u0BBF\u0BB0\u0BC8\u0BB5\u0BBE\u0B95"
"\u0BAE\u0BC0\u0BA3\u0BCD\u0B9F\u0BC1\u0BAE\u0BCD","\u0B87\u0BA9\u0BCD\u0BA9\u0BC1\u0BAE\u0BCD","\u0B8F\u0BB1\u0BCD\u0B95\u0BA9\u0BB5\u0BC7"
"\u0B87\u0BA4\u0BA9\u0BBE\u0BB2\u0BCD","\u0B85\u0BA4\u0BA9\u0BBE\u0BB2\u0BCD","\u0B8E\u0BA9\u0BB5\u0BC7","\u0B8E\u0BA9\u0BBF\u0BA9\u0BC1\u0BAE\u0BCD","\u0B86\u0BA9\u0BBE\u0BB2\u0BC1\u0BAE\u0BCD","\u0B87\u0BB0\u0BC1\u0BA8\u0BCD\u0BA4\u0BBE\u0BB2\u0BC1\u0BAE\u0BCD"
"\u0B95\u0BC2\u0B9F","\u0BA4\u0BBE\u0BA9\u0BCD","\u0B89\u0BAE\u0BCD","\u0B8F"
)

Write-SeedJson -iso3 "tel" -iso1 "te" -name "Telugu" -words @(
"\u0C2E\u0C30\u0C3F\u0C2F\u0C41","\u0C32\u0C47\u0C26\u0C3E","\u0C15\u0C3E\u0C28\u0C40","\u0C0E\u0C02\u0C26\u0C41\u0C15\u0C02\u0C1F\u0C47","\u0C15\u0C3E\u0C2C\u0C1F\u0C4D\u0C1F\u0C3F","\u0C05\u0C26\u0C3F","\u0C07\u0C26\u0C3F"
"\u0C05\u0C24\u0C28\u0C41","\u0C06\u0C2E\u0C46","\u0C05\u0C26\u0C3F","\u0C35\u0C3E\u0C30\u0C41"
"\u0C28\u0C47\u0C28\u0C41","\u0C28\u0C41\u0C35\u0C4D\u0C35\u0C41","\u0C2E\u0C40\u0C30\u0C41","\u0C2E\u0C47\u0C2E\u0C41","\u0C35\u0C3E\u0C33\u0C4D\u0C33\u0C41"
"\u0C28\u0C3E","\u0C28\u0C40","\u0C2E\u0C40","\u0C05\u0C24\u0C28\u0C3F","\u0C06\u0C2E\u0C46","\u0C35\u0C3E\u0C30\u0C3F","\u0C2E\u0C28"
"\u0C12\u0C15","\u0C15\u0C4A\u0C28\u0C4D\u0C28\u0C3F","\u0C0F\u0C26\u0C48\u0C28\u0C3E","\u0C05\u0C28\u0C4D\u0C28\u0C3F","\u0C2A\u0C4D\u0C30\u0C24\u0C3F","\u0C05\u0C28\u0C47\u0C15","\u0C15\u0C4A\u0C28\u0C4D\u0C28\u0C3F"
"\u0C0E\u0C35\u0C30\u0C41","\u0C0F\u0C2E\u0C3F\u0C1F\u0C3F","\u0C0F\u0C26\u0C3F","\u0C0E\u0C2A\u0C4D\u0C2A\u0C41\u0C21\u0C41","\u0C0E\u0C15\u0C4D\u0C15\u0C21","\u0C0E\u0C32\u0C3E","\u0C0E\u0C02\u0C26\u0C41\u0C15\u0C41"
"\u0C05\u0C35\u0C41\u0C28\u0C41","\u0C15\u0C3E\u0C26\u0C41","\u0C07\u0C15\u0C4D\u0C15\u0C21","\u0C05\u0C15\u0C4D\u0C15\u0C21"
"\u0C2A\u0C48\u0C28","\u0C15\u0C3F\u0C02\u0C26","\u0C32\u0C4B\u0C2A\u0C32","\u0C2C\u0C2F\u0C1F","\u0C2E\u0C41\u0C02\u0C26\u0C41","\u0C35\u0C46\u0C28\u0C41\u0C15"
"\u0C26\u0C17\u0C4D\u0C17\u0C30","\u0C26\u0C42\u0C30\u0C02\u0C17\u0C3E"
"\u0C1A\u0C3E\u0C32\u0C3E","\u0C15\u0C4A\u0C02\u0C1A\u0C46\u0C02","\u0C0E\u0C15\u0C4D\u0C15\u0C41\u0C35","\u0C24\u0C15\u0C4D\u0C15\u0C41\u0C35"
"\u0C2E\u0C41\u0C02\u0C26\u0C41","\u0C24\u0C30\u0C4D\u0C35\u0C3E\u0C24","\u0C07\u0C2A\u0C4D\u0C2A\u0C41\u0C21\u0C41","\u0C05\u0C2A\u0C4D\u0C2A\u0C41\u0C21\u0C41"
"\u0C0E\u0C32\u0C4D\u0C32\u0C2A\u0C4D\u0C2A\u0C41\u0C21\u0C41","\u0C0E\u0C2A\u0C4D\u0C2A\u0C41\u0C21\u0C41"
"\u0C05\u0C2A\u0C4D\u0C2A\u0C41\u0C21\u0C2A\u0C4D\u0C2A\u0C41\u0C21\u0C41","\u0C24\u0C30\u0C1A\u0C41\u0C17\u0C3E","\u0C05\u0C30\u0C41\u0C26\u0C41\u0C17\u0C3E"
"\u0C15\u0C4D\u0C30\u0C2E\u0C17\u0C3E","\u0C05\u0C15\u0C38\u0C4D\u0C2E\u0C3E\u0C24\u0C4D\u0C24\u0C41\u0C17\u0C3E","\u0C28\u0C46\u0C2E\u0C4D\u0C2E\u0C26\u0C3F\u0C17\u0C3E","\u0C24\u0C35\u0C30\u0C17\u0C3E"
"\u0C2E\u0C33\u0C4D\u0C33\u0C40","\u0C07\u0C02\u0C15\u0C3E","\u0C07\u0C2A\u0C4D\u0C2A\u0C1F\u0C3F\u0C15\u0C47","\u0C07\u0C26\u0C3F\u0C17\u0C4B","\u0C05\u0C26\u0C3F\u0C17\u0C4B"
"\u0C05\u0C02\u0C26\u0C41\u0C1A\u0C47\u0C24","\u0C05\u0C2F\u0C3F\u0C28\u0C3E","\u0C05\u0C2F\u0C3F\u0C28\u0C2A\u0C4D\u0C2A\u0C1F\u0C3F\u0C15\u0C40","\u0C05\u0C32\u0C3E\u0C17\u0C47"
"\u0C38\u0C30\u0C47","\u0C15\u0C47\u0C35\u0C32\u0C02","\u0C2E\u0C3E\u0C24\u0C4D\u0C30\u0C2E\u0C47","\u0C26\u0C3E\u0C26\u0C3E\u0C2A\u0C41","\u0C38\u0C41\u0C2E\u0C3E\u0C30\u0C41"
"\u0C38\u0C30\u0C3F\u0C17\u0C4D\u0C17\u0C3E","\u0C16\u0C1A\u0C4D\u0C1A\u0C3F\u0C24\u0C17\u0C3E","\u0C28\u0C3F\u0C1C\u0C02\u0C17\u0C3E","\u0C2C\u0C39\u0C41\u0C36\u0C3E"
"\u0C0E\u0C02\u0C24\u0C4B","\u0C05\u0C02\u0C24\u0C3E","\u0C05\u0C02\u0C24","\u0C07\u0C02\u0C24","\u0C05\u0C02\u0C24\u0C47"
)

Write-Host "`nDone! All seed wordlists created." -ForegroundColor Cyan
