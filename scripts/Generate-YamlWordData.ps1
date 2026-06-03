param([string]$OutputDir = "$PSScriptRoot/../worddata")

$null = New-Item -ItemType Directory -Force -Path $OutputDir

$LangInfo = @{}
# Add all languages
$langs = @(
    @("eng","en","English","germanic"), @("ita","it","Italian","romance"), @("fra","fr","French","romance"),
    @("deu","de","German","germanic"), @("spa","es","Spanish","romance"), @("por","pt","Portuguese","romance"),
    @("nld","nl","Dutch","germanic"), @("ron","ro","Romanian","romance"), @("cat","ca","Catalan","romance"),
    @("glg","gl","Galician","romance"), @("swe","sv","Swedish","germanic"), @("dan","da","Danish","germanic"),
    @("nor","nb","Norwegian","germanic"), @("pol","pl","Polish","slavic"), @("ces","cs","Czech","slavic"),
    @("hun","hu","Hungarian","other"), @("fin","fi","Finnish","other"), @("tur","tr","Turkish","other"),
    @("ell","el","Greek","other"), @("rus","ru","Russian","slavic"), @("bul","bg","Bulgarian","slavic"),
    @("srp","sr","Serbian","slavic"), @("hrv","hr","Croatian","slavic"), @("slk","sk","Slovak","slavic"),
    @("slv","sl","Slovenian","slavic"), @("lit","lt","Lithuanian","other"), @("lav","lv","Latvian","other"),
    @("est","et","Estonian","other"), @("isl","is","Icelandic","germanic"), @("eus","eu","Basque","other"),
    @("gle","ga","Irish","other"), @("lat","la","Latin","romance"), @("msa","ms","Malay","other"),
    @("ind","id","Indonesian","other"), @("vie","vi","Vietnamese","other"), @("sqi","sq","Albanian","other"),
    @("hye","hy","Armenian","other"), @("bel","be","Belarusian","slavic"), @("ben","bn","Bengali","other"),
    @("ara","ar","Arabic","other"), @("heb","he","Hebrew","other"), @("hin","hi","Hindi","other"),
    @("mar","mr","Marathi","other"), @("urd","ur","Urdu","other"), @("fas","fa","Persian","other"),
    @("kan","kn","Kannada","other"), @("tam","ta","Tamil","other"), @("tel","te","Telugu","other"),
    @("tha","th","Thai","other"), @("jpn","ja","Japanese","other"), @("kor","ko","Korean","other"),
    @("zho","zh","Chinese","other"), @("kaz","kk","Kazakh","other"), @("mkd","mk","Macedonian","slavic"),
    @("ukr","uk","Ukrainian","slavic")
)
foreach ($l in $langs) { $LangInfo[$l[0]] = @{iso1=$l[1]; name=$l[2]; type=$l[3]} }

function Add-Line($sb, $text) { $null = $sb.AppendLine($text) }

function Get-FrequencyWords($iso3) {
    $info = $LangInfo[$iso3]
    $iso2 = $info.iso1
    $url = "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/$iso2/${iso2}_50k.txt"
    try {
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "Mozilla/5.0")
        $content = $wc.DownloadString($url)
        $words = New-Object System.Collections.Generic.List[string]
        foreach ($line in ($content -split "`n")) {
            $parts = $line -split " "
            if ($parts.Length -ge 1 -and $parts[0] -match "^[\p{L}]+$") {
                $words.Add($parts[0].ToLower())
            }
        }
        return $words.ToArray()
    } catch {
        return @()
    }
}

function Expand-FunctionWords($iso3, $freqWords) {
    $stopPath = "$PSScriptRoot/../wordlists/$iso3.json"
    $fw = New-Object System.Collections.Generic.HashSet[string]
    if (Test-Path $stopPath) {
        try {
            $sw = Get-Content $stopPath -Raw -ErrorAction Stop | ConvertFrom-Json
            foreach ($w in $sw.function_words) { $null = $fw.Add($w.Trim().ToLower()) }
        } catch {}
    }
    if ($freqWords.Count -gt 0) {
        $short = $freqWords | Where-Object { $_.Length -le 4 } | Select-Object -First 300
        foreach ($w in $short) { $null = $fw.Add($w) }
    }
    return $fw
}

function Generate-Lemmas($iso3, $freqWords, $type) {
    $lemmas = New-Object System.Collections.Generic.Dictionary[string, string]
    $seen = New-Object System.Collections.Generic.HashSet[string]
    $topN = if ($freqWords.Count -gt 0) { $freqWords | Where-Object { $_ -match "^[\p{L}]{3,12}$" } | Select-Object -First 5000 } else { @() }
    $wordPool = if ($topN.Count -ge 50) { $topN } else { @("word","time","people","way","water","day","man","woman","child","year","hand","eye","head","door","room","house","car","book","food","city","world","life","work","home","place","point","case","week","month","night","morning","school","family","group","country","problem","hand","part","place","number","name","line","end","side","turn","cause","effect","result","change","start","end","help","need","love","hate","play","work","live","move","keep","show","try","ask","tell","give","take","come","go","get","make","see","know","think","want","find","use","call","feel","look","seem","put","run","walk","stand","sit","read","write","eat","drink","sleep","buy","sell","pay","send","receive","speak","hear","learn","teach","leave","stay","wait","follow","lead","bring","carry","hold","let","set","turn","open","close","begin","finish","stop","continue","allow","accept","deny","refuse","believe","doubt","remember","forget","hope","wish","expect","wonder","understand","explain","describe","report","claim","suggest","offer","propose","decide","choose","select","collect","gather","spread","cover","fill","empty","build","destroy","create","produce","develop","grow","increase","reduce","improve","break","fix","repair","damage","harm","protect","defend","attack","fight","win","lose","enjoy","prefer","respect","admire","trust","support","oppose","resist","insist","persist","exist","occur","happen","appear","disappear","arrive","depart","return","enter","exit","cross","pass","reach","avoid","escape","face","encounter","meet","visit","attend","join","participate","share","divide","separate","unite","combine","connect","attach","remove","replace","exchange","trade","borrow","lend","owe","own","possess","lack","need","require","include","exclude","contain","consist","involve","concern","relate","apply","refer","belong","depend","rely","base","establish","found","settle","occupy","inhabit","populate","cultivate","farm","hunt","fish","gather","mine","manufacture","produce","distribute","transport","deliver","serve","supply","provide","equip","furnish","decorate","paint","draw","sculpt","carve","build","construct","assemble","install","mount","fix","attach","fasten","lock","secure","tie","bind","chain","link","connect","bridge","span","extend","stretch","reach","spread","scatter","disperse","distribute","arrange","order","organize","classify","sort","group","categorize","label","tag","mark","identify","recognize","distinguish","differentiate","discriminate","separate","divide","split","break","fracture","crack","shatter","smash","crush","grind","mill","pound","beat","whip","stir","mix","blend","combine","merge","fuse","melt","dissolve","liquefy","solidify","freeze","harden","soften","warm","cool","heat","boil","steam","bake","roast","fry","broil","grill","barbecue","smoke","cure","preserve","pickle","can","bottle","store","stock","supply","reserve","save","keep","hold","maintain","preserve","conserve","protect","guard","defend","shelter","shield","cover","wrap","envelop","surround","encircle","ring","circle","orbit","rotate","spin","twirl","whirl","turn","revolve","swivel","pivot","axis","center","focus","concentrate","centralize","decentralize","disperse","distribute","spread","diffuse","scatter","broadcast","transmit","communicate","convey","express","state","declare","announce","proclaim","advertise","promote","publicize","reveal","disclose","expose","uncover","show","display","exhibit","present","demonstrate","illustrate","exemplify","represent","symbolize","stand","signify","mean","denote","connote","imply","suggest","hint","indicate","signal","point","direct","guide","lead","conduct","escort","accompany","chaperone","usher","steer","navigate","pilot","drive","ride","travel","move","transport","carry","convey","bear","shoulder","support","sustain","endure","tolerate","withstand","resist","oppose","defy","challenge","confront","face","meet","encounter","experience","undergo","suffer","endure","survive","live","exist","subsist","persist","continue","last","remain","stay","abide","reside","dwell","inhabit","occupy","settle","colonize","populate","people","crowd","fill","pack","stuff","cram","jam","wedge","force","push","shove","thrust","propel","project","launch","fire","shoot","blast","explode","detonate","ignite","kindle","light","illuminate","brighten","darken","dim","shade","shadow","obscure","hide","conceal","mask","disguise","camouflage","cloak","veil","screen","filter","sift","strain","purify","clean","wash","rinse","bathe","shower","soak","drench","saturate","soak","steep","infuse","brew","ferment","distill","refine","process","treat","handle","manage","administer","govern","rule","reign","control","direct","command","order","instruct","teach","educate","train","coach","tutor","mentor","guide","counsel","advise","recommend","suggest","urge","encourage","motivate","inspire","stimulate","provoke","incite","spur","goad","prod","push","drive","impel","compel","force","oblige","require","demand","expect","anticipate","predict","forecast","project","estimate","calculate","compute","reckon","count","tally","total","sum","add","subtract","multiply","divide","compute","figure","determine","ascertain","establish","confirm","verify","validate","authenticate","certify","attest","testify","swear","vow","pledge","promise","commit","dedicate","devote","consecrate","sanctify","bless","curse","damn","condemn","criticize","blame","accuse","charge","indict","impeach","arraign","prosecute","sue","litigate","arbitrate","mediate","negotiate","bargain","haggle","deal","trade","exchange","swap","barter","sell","market","merchandise","advertise","promote","push","boost","enhance","improve","better","upgrade","update","revise","amend","correct","rectify","remedy","cure","heal","mend","repair","fix","restore","renew","renovate","refurbish","recondition","overhaul","rebuild","reconstruct","recreate","reproduce","duplicate","copy","imitate","mimic","simulate","pretend","fake","forge","falsify","distort","misrepresent","lie","deceive","mislead","trick","fool","cheat","swindle","defraud","con","scam","rig","manipulate","exploit","abuse","mistreat","harm","injure","wound","hurt","damage","impair","maim","cripple","disable","incapacitate","paralyze","kill","murder","assassinate","execute","slay","massacre","slaughter","butcher","sacrifice","offer","donate","contribute","grant","award","bestow","confer","present","give","provide","supply","furnish","equip","outfit","rig","fit","adapt","adjust","modify","alter","change","convert","transform","metamorphose","transmute","transubstantiate","transfigure","transmogrify","mutate","evolve","develop","grow","mature","ripen","age","season","weather","wear","erode","corrode","rust","decay","rot","decompose","putrefy","spoil","go","bad","sour","curdle","ferment","rise","leaven","proof","knead","roll","shape","form","mold","model","fashion","craft","forge","hammer","chisel","whittle","carve","engrave","etch","inscribe","write","pen","draft","compose","author","publish","print","reproduce","copy","photocopy","scan","fax","telex","cable","wire","radio","televise","broadcast","stream","podcast","webcast","upload","download","transfer","backup","archive","store","save","cache","buffer","queue","spool","print","output","display","render","generate","produce","create","originate","initiate","start","begin","commence","launch","inaugurate","open","establish","found","institute","organize","set","up","arrange","plan","design","devise","conceive","formulate","develop","work","out","elaborate","refine","polish","perfect","complete","finish","end","conclude","terminate","cease","stop","halt","pause","suspend","interrupt","discontinue","quit","resign","retire","abdicate","relinquish","surrender","yield","submit","capitulate","succumb","comply","conform","obey","follow","adhere","stick","cling","hold","grasp","grip","clutch","seize","snatch","grab","catch","capture","apprehend","arrest","detain","confine","imprison","jail","incarcerate","pen","cage","coop","enclose","surround","circumscribe","limit","restrict","constrain","confine","bound","border","edge","rim","margin","verge","brink","threshold","doorway","gate","portal","entrance","entry","access","admission","admittance","entree","ingress","egress","exit","outlet","vent","opening","gap","hole","perforation","puncture","pierce","stab","stick","lance","spear","harpoon","gore","impale","spike","skewer","needle","pin","tack","nail","screw","bolt","rivet","weld","solder","braze","glue","paste","cement","mortar","plaster","stucco","daub","smear","spread","coat","paint","varnish","lacquer","enamel","glaze","polish","buff","shine","burnish","furbish","scour","scrub","abrade","scrape","scratch","grate","rasp","file","sand","smooth","level","flatten","plane","shave","trim","prune","clip","crop","shear","cut","sever","split","cleave","rend","tear","rip","shred","mince","hash","dice","cube","slice","carve","fillet","bone","skin","peel","pare","strip","divest","denude","bare","uncover","expose","reveal","show","display","exhibit","demonstrate","prove","verify","confirm","substantiate","corroborate","authenticate","validate","ratify","endorse","sanction","approve","authorize","license","permit","allow","permit","let","enable","facilitate","ease","smooth","grease","oil","lubricate","fuel","power","energize","activate","trigger","spark","set","off","stimulate","arouse","rouse","awaken","wake","revive","animate","enliven","vitalize","invigorate","strengthen","fortify","reinforce","bolster","brace","support","prop","buttress","shore","underpin","undergird","foundation","base","basis","ground","footing","foothold","purchase","grip","hold","leverage","advantage","edge","upper","hand","superiority","dominance","supremacy","preeminence","primacy","paramountcy","preponderance","ascendancy","mastery","control","command","domination","sovereignty","authority","jurisdiction","power","sway","influence","clout","pressure","leverage","weight","pull","drag","tug","yank","heave","haul","tow","trail","draw","pull")
    }
    
    if ($type -eq "germanic" -and $iso3 -eq "eng") {
        $irreg = @(
            @("be","am","is","are","was","were","been","being"),
            @("have","has","had","having"),
            @("do","does","did","done","doing"),
            @("say","said","saying"),
            @("go","went","gone","going"),
            @("get","got","gotten","getting"),
            @("make","made","making"),
            @("know","knew","known","knowing"),
            @("think","thought","thought","thinking"),
            @("take","took","taken","taking"),
            @("see","saw","seen","seeing"),
            @("come","came","come","coming"),
            @("find","found","found","finding"),
            @("give","gave","given","giving"),
            @("tell","told","told","telling"),
            @("feel","felt","felt","feeling"),
            @("leave","left","left","leaving"),
            @("keep","kept","kept","keeping"),
            @("begin","began","begun","beginning"),
            @("bring","brought","brought","bringing"),
            @("write","wrote","written","writing"),
            @("sit","sat","sat","sitting"),
            @("stand","stood","stood","standing"),
            @("lose","lost","lost","losing"),
            @("pay","paid","paid","paying"),
            @("meet","met","met","meeting"),
            @("speak","spoke","spoken","speaking"),
            @("read","read","read","reading"),
            @("spend","spent","spent","spending"),
            @("grow","grew","grown","growing"),
            @("win","won","won","winning"),
            @("buy","bought","bought","buying"),
            @("send","sent","sent","sending"),
            @("build","built","built","building"),
            @("fall","fell","fallen","falling"),
            @("cut","cut","cut","cutting"),
            @("sell","sold","sold","selling"),
            @("break","broke","broken","breaking"),
            @("drive","drove","driven","driving"),
            @("eat","ate","eaten","eating"),
            @("draw","drew","drawn","drawing"),
            @("throw","threw","thrown","throwing"),
            @("sing","sang","sung","singing"),
            @("swim","swam","swum","swimming"),
            @("fly","flew","flown","flying"),
            @("wear","wore","worn","wearing"),
            @("teach","taught","taught","teaching"),
            @("catch","caught","caught","catching"),
            @("fight","fought","fought","fighting"),
            @("choose","chose","chosen","choosing"),
            @("hide","hid","hidden","hiding"),
            @("bite","bit","bitten","biting"),
            @("blow","blew","blown","blowing"),
            @("freeze","froze","frozen","freezing"),
            @("hang","hung","hung","hanging"),
            @("lead","led","led","leading"),
            @("lend","lent","lent","lending"),
            @("mean","meant","meant","meaning"),
            @("shoot","shot","shot","shooting"),
            @("shake","shook","shaken","shaking"),
            @("rise","rose","risen","rising"),
            @("ride","rode","ridden","riding"),
            @("ring","rang","rung","ringing"),
            @("steal","stole","stolen","stealing"),
            @("stick","stuck","stuck","sticking"),
            @("strike","struck","struck","striking"),
            @("sweep","swept","swept","sweeping"),
            @("swing","swung","swung","swinging"),
            @("tear","tore","torn","tearing"),
            @("wake","woke","woken","waking"),
            @("wind","wound","wound","winding"),
            @("bend","bent","bent","bending"),
            @("bind","bound","bound","binding"),
            @("bleed","bled","bled","bleeding"),
            @("breed","bred","bred","breeding"),
            @("cling","clung","clung","clinging"),
            @("creep","crept","crept","creeping"),
            @("deal","dealt","dealt","dealing"),
            @("dig","dug","dug","digging"),
            @("dive","dove","dived","diving"),
            @("dream","dreamt","dreamt","dreaming"),
            @("feed","fed","fed","feeding"),
            @("flee","fled","fled","fleeing"),
            @("fling","flung","flung","flinging"),
            @("forbid","forbade","forbidden","forbidding"),
            @("forgive","forgave","forgiven","forgiving"),
            @("forsake","forsook","forsaken","forsaking"),
            @("grind","ground","ground","grinding"),
            @("hear","heard","heard","hearing"),
            @("hit","hit","hit","hitting"),
            @("hold","held","held","holding"),
            @("hurt","hurt","hurt","hurting"),
            @("kneel","knelt","knelt","kneeling"),
            @("lay","laid","laid","laying"),
            @("leap","leapt","leapt","leaping"),
            @("lie","lay","lain","lying"),
            @("light","lit","lit","lighting"),
            @("make","made","made","making"),
            @("mistake","mistook","mistaken","mistaking"),
            @("overcome","overcame","overcome","overcoming"),
            @("overthrow","overthrew","overthrown","overthrowing"),
            @("prove","proved","proven","proving"),
            @("put","put","put","putting"),
            @("quit","quit","quit","quitting"),
            @("rid","rid","rid","ridding"),
            @("run","ran","run","running"),
            @("seek","sought","sought","seeking"),
            @("set","set","set","setting"),
            @("sew","sewed","sewn","sewing"),
            @("shake","shook","shaken","shaking"),
            @("shine","shone","shone","shining"),
            @("shrink","shrank","shrunk","shrinking"),
            @("shut","shut","shut","shutting"),
            @("slide","slid","slid","sliding"),
            @("slit","slit","slit","slitting"),
            @("smell","smelt","smelt","smelling"),
            @("sow","sowed","sown","sowing"),
            @("spin","spun","spun","spinning"),
            @("spit","spat","spat","spitting"),
            @("split","split","split","splitting"),
            @("spread","spread","spread","spreading"),
            @("spring","sprang","sprung","springing"),
            @("steal","stole","stolen","stealing"),
            @("sting","stung","stung","stinging"),
            @("stink","stank","stunk","stinking"),
            @("stride","strode","stridden","striding"),
            @("strive","strove","striven","striving"),
            @("sunbathe","sunbathed","sunbathed","sunbathing"),
            @("swear","swore","sworn","swearing"),
            @("sweat","sweat","sweat","sweating"),
            @("swell","swelled","swollen","swelling"),
            @("swim","swam","swum","swimming"),
            @("swing","swung","swung","swinging"),
            @("take","took","taken","taking"),
            @("teach","taught","taught","teaching"),
            @("tear","tore","torn","tearing"),
            @("think","thought","thought","thinking"),
            @("throw","threw","thrown","throwing"),
            @("thrust","thrust","thrust","thrusting"),
            @("tread","trod","trodden","treading"),
            @("undergo","underwent","undergone","undergoing"),
            @("understand","understood","understood","understanding"),
            @("undertake","undertook","undertaken","undertaking"),
            @("upset","upset","upset","upsetting"),
            @("wake","woke","woken","waking"),
            @("wear","wore","worn","wearing"),
            @("weave","wove","woven","weaving"),
            @("weep","wept","wept","weeping"),
            @("wet","wet","wet","wetting"),
            @("win","won","won","winning"),
            @("wind","wound","wound","winding"),
            @("withdraw","withdrew","withdrawn","withdrawing"),
            @("withstand","withstood","withstood","withstanding"),
            @("wring","wrung","wrung","wringing"),
            @("write","wrote","written","writing")
        )
        foreach ($v in $irreg) {
            $base = $v[0].ToLower()
            for ($i = 1; $i -lt $v.Length; $i++) {
                $f = $v[$i].ToLower()
                if ($f -ne $base -and !$seen.Contains($f)) { $null = $seen.Add($f); if (!$lemmas.ContainsKey($f)) { $lemmas[$f] = $base } }
            }
        }
    }
    
    foreach ($w in $wordPool) {
        if ($w.Length -lt 3) { continue }
        $base = $w
        # Add base form
        if (!$seen.Contains($base)) { $null = $seen.Add($base) }
        
        # Verb forms: -s, -ed, -ing
        $forms = @(
            $base + "s", $base + "ed", $base + "ing",
            $base + "es", $base + "d"
        )
        if ($base.EndsWith("e")) {
            $forms[3] = $base.Substring(0, $base.Length-1) + "ing"
            $forms[4] = $base + "d"
        }
        if ($base.EndsWith("y") -and $base.Length -gt 2 -and "aeiou".IndexOf($base[-2]) -lt 0) {
            $forms[0] = $base.Substring(0, $base.Length-1) + "ies"
            $forms[1] = $base.Substring(0, $base.Length-1) + "ied"
        }
        # Past tense doubling
        if ($base.Length -ge 3) {
            $last = $base[-1]; $secondLast = $base[-2]
            if ("aeiou".IndexOf($last) -lt 0 -and "aeiou".IndexOf($secondLast) -ge 0 -and "aeiou".IndexOf($base[-3]) -lt 0) {
                # CVC pattern
                $forms[1] = $base + $last + "ed"
                $forms[2] = $base + $last + "ing"
            }
        }
        
        foreach ($f in $forms) {
            if ($f -ne $base -and !$seen.Contains($f) -and $f.Length -gt 1) {
                $null = $seen.Add($f)
                if (!$lemmas.ContainsKey($f)) { $lemmas[$f] = $base }
            }
        }
        
        # Noun plural
        $plural = $base + "s"
        if ($base -match "[sxz]$" -or $base -match "[cs]h$") { $plural = $base + "es" }
        elseif ($base.EndsWith("y") -and $base.Length -gt 2 -and "aeiou".IndexOf($base[-2]) -lt 0) { $plural = $base.Substring(0, $base.Length-1) + "ies" }
        elseif ($base.EndsWith("f")) { $plural = $base.Substring(0, $base.Length-1) + "ves" }
        elseif ($base.EndsWith("fe")) { $plural = $base.Substring(0, $base.Length-2) + "ves" }
        if ($plural -ne $base -and !$seen.Contains($plural)) {
            $null = $seen.Add($plural)
            if (!$lemmas.ContainsKey($plural)) { $lemmas[$plural] = $base }
        }
        
        # Adjective forms: -er, -est, -ly
        if ($base.Length -ge 4 -and $base -notmatch "^(a|an|the|and|or|but|in|on|at|to|for|of|with|by|from|up|down|out|off|over|under|again|further|then|once|here|there|when|where|why|how|all|each|every|both|few|more|most|other|some|such|no|nor|not|only|own|same|so|than|too|very|just|because|as|until|while|about|between|through|during|before|after|above|below|between|out|off|over|under|again|further|then|once)$") {
            $adjForms = @($base + "er", $base + "est", $base + "ly")
            if ($base.EndsWith("y")) { $adjForms[0] = $base.Substring(0,$base.Length-1) + "ier"; $adjForms[1] = $base.Substring(0,$base.Length-1) + "iest" }
            if ($base.EndsWith("e")) { $adjForms[0] = $base + "r"; $adjForms[1] = $base + "st" }
            if ($base.EndsWith("ic")) { $adjForms[2] = $base + "ally" }
            foreach ($af in $adjForms) {
                if ($af -ne $base -and !$seen.Contains($af) -and $af.Length -gt 2) {
                    $null = $seen.Add($af)
                    if (!$lemmas.ContainsKey($af)) { $lemmas[$af] = $base }
                }
            }
        }
    }
    return $lemmas
}

function Write-Yaml($iso3, $freqWords, $fwSet, $lemmas, $type) {
    $sb = New-Object System.Text.StringBuilder
    $info = $LangInfo[$iso3]
    Add-Line $sb "iso3: $iso3"
    Add-Line $sb "iso1: $($info.iso1)"
    Add-Line $sb "name: $($info.name)"
    Add-Line $sb ""
    Add-Line $sb "function_words:"
    foreach ($w in $fwSet) { Add-Line $sb "  - $w" }
    Add-Line $sb ""
    Add-Line $sb "lemmas:"
    $lc = 0
    foreach ($kv in $lemmas.GetEnumerator()) {
        Add-Line $sb ("  " + $kv.Key + ": " + $kv.Value)
        $lc++
    }
    return $sb.ToString(), $lc
}

Write-Host "Downloading frequency lists..."
$freqCache = @{}
$freqUrls = @("en","it","fr","de","es","pt","nl","sv","da","no","pl","cs","hu","fi","tr","el","ru","bg","sr","hr","sk","sl","lt","lv","et","is","eu","ga","ms","id","vi","sq","hy","be","bn","ar","he","hi","mr","ur","fa","kn","ta","te","th","ja","ko","zh","kk","mk","uk")
$iso2to3 = @{}
foreach ($l in $langs) { $iso2to3[$l[1]] = $l[0] }

foreach ($iso2 in $freqUrls) {
    $iso3 = if ($iso2to3.ContainsKey($iso2)) { $iso2to3[$iso2] } else { $iso2 }
    Write-Host "  $iso3..."
    try {
        $url = "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/$iso2/${iso2}_50k.txt"
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "Mozilla/5.0")
        $content = $wc.DownloadString($url)
        $words = New-Object System.Collections.Generic.List[string]
        foreach ($line in ($content -split "`n")) {
            $parts = $line -split " "
            if ($parts.Length -ge 1 -and $parts[0] -match "^[\p{L}]+$") {
                $words.Add($parts[0].ToLower())
            }
        }
        $freqCache[$iso3] = $words.ToArray()
        Write-Host "    $($words.Count) words"
    } catch {
        Write-Host "    Not available"
    }
}

Write-Host "`nGenerating YAML files..."
foreach ($iso3 in $LangInfo.Keys) {
    Write-Host "  $iso3 ($($LangInfo[$iso3].name))..."
    $freqWords = if ($freqCache.ContainsKey($iso3)) { $freqCache[$iso3] } else { @() }
    $fw = Expand-FunctionWords $iso3 $freqWords
    $lemmas = Generate-Lemmas $iso3 $freqWords $LangInfo[$iso3].type
    $yaml, $lc = Write-Yaml $iso3 $freqWords $fw $lemmas $LangInfo[$iso3].type
    $outPath = "$OutputDir/$iso3.yaml"
    [System.IO.File]::WriteAllText($outPath, $yaml, [System.Text.Encoding]::UTF8)
    $size = (Get-Item $outPath).Length / 1MB
    Write-Host "    $lc lemmas, $('{0:N2}' -f $size) MB"
}
Write-Host "Done!"
