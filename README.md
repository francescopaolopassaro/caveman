#🦴 Caveman: Prompt Compressor for LLMs

<img width="1197" height="766" alt="caveman_splash" src="https://github.com/user-attachments/assets/4b534140-c519-423f-b918-e705565a039f" />
It is the version that is inspired by the token saving algorithm of Caveman plugin for Claude, but it was conceived without doing any porting from the original, it is a code born from scratch


**Caveman** è una libreria C# basata su **Catalyst** che riduce drasticamente il numero di token nei tuoi prompt per LLM (come Gemma 3, Llama o GPT-4). Utilizza tecniche di Natural Language Processing (NLP) per rimuovere il "rumore" grammaticale (articoli, preposizioni, congiunzioni) mantenendo intatto il valore semantico.

> "Perché usare molti token quando pochi token fanno lavoro uguale?" — Un uomo delle caverne (e il tuo portafoglio).

## 🚀 Caratteristiche
- **Riduzione Token fino al 70%**: Abbatti i costi delle API e velocizza l'inferenza locale.
- **Multilingua**: Supporto per oltre 50 lingue (Italiano, Inglese, Francese, ecc.) tramite i modelli Catalyst.
- **Livelli di Compressione**: Scegli tra `Light`, `Semantic` o `Aggressive` (Lemmatizzazione).
- **Integrazione LLM con Semantic Kernel**: Ottimizzato per modelli di nuova generazione che comprendono perfettamente il linguaggio contratto.

---

## 🛠️ Installazione

### 1. Pacchetto Base
Installa la libreria core e il gestore dei modelli:

dotnet add package Catalyst
dotnet add package Mosaik.Core

### 2. Modelli Linguistici
Installa i pacchetti per le lingue che intendi supportare:
dotnet add package Catalyst.Models.Italian
dotnet add package Catalyst.Models.English
O meglio lancia lo script powershell Install-CatalystModels.ps1 (vengono aggiornati in automatico al project tutte le librerie)

### 3. Utilizzo rapido
var compressor = new CavemanCompressionService();

string input = "Buongiorno, vorrei sapere se fosse possibile ricevere informazioni sui ristoranti a Roma.";

// Comprime il testo rimuovendo stopwords e mantenendo il senso
string compressed = await compressor.CompressAsync(input, Language.Italian, CompressionLevel.Semantic);

Console.WriteLine(compressed); 
// Output: "Buongiorno sapere possibile ricevere informazioni ristoranti Roma"

### 5. Livelli di Compressione
### 📊 Livelli di Compressione NLP

| Livello | Logica Applicata | Tag POS Rimossi (Filtri) | Risparmio |
| :--- | :--- | :--- | :--- |
| **Light** | *Stopword Removal* | `DET`, `ADP`, `CCONJ`, `SCONJ`, `PRON`, `PUNCT` | **~25-30%** |
| **Semantic** | *Key Content Selection* | Mantiene solo `NOUN`, `VERB`, `ADJ`, `PROPN`, `ADV` | **~50%** |
| **Aggressive** | *Lemmatization* | Mantiene solo `NOUN`, `VERB`, `PROPN` (forma base) | **~70%** |

### 🔍 Dettaglio Tecnico dei Tag (Mapping Catalyst)

| Tag POS | Categoria | Esempi (ITA/ENG) | Trattamento |
| :--- | :--- | :--- | :--- |
| **DET** | Articoli | il, lo, la, un / the, a | **Rimosso** (da Light) |
| **ADP** | Preposizioni | di, a, da, in / of, at, from | **Rimosso** (da Light) |
| **CCONJ** | Cong. Coordinanti | e, o, ma / and, or, but | **Rimosso** (da Light) |
| **SCONJ** | Cong. Subordinanti | che, se, perché / that, if | **Rimosso** (da Light) |
| **PRON** | Pronomi | io, tu, mi, lo / I, you, it | **Rimosso** (da Light) |
| **NOUN** | Sostantivi | casa, pizza / house, pizza | **Mantenuto Sempre** |
| **VERB** | Verbi | mangiare, corre / eat, runs | **Mantenuto Sempre** |
| **ADV** | Avverbi | non, molto / not, quickly | **Mantenuto in Semantic** |

### 💡 Esempio di Trasformazione

| Stato | Testo del Prompt | Token / Caratteri |
| :--- | :--- | :--- |
| **Originale** | "Vorrei sapere se è possibile avere una pizza margherita subito." | 100% (62 ch) |
| **Light** | "Vorrei sapere possibile avere pizza margherita subito" | ~75% (48 ch) |
| **Semantic** | "sapere possibile avere pizza margherita subito" | ~55% (42 ch) |
| **Aggressive**| "sapere possibile avere pizza margherita" | **~40% (36 ch)** |


🤝 Contribuire
Le pull request sono benvenute! Per modifiche importanti, apri prima un'issue per discutere cosa vorresti cambiare.


Caveman License Agreement v1.0
Copyright (c) 2026 Francesco Paolo Passaro
Con la presente si concede il permesso di utilizzare, copiare e modificare questo software ("Caveman") esclusivamente per scopi Open Source e NON Commerciali, alle seguenti condizioni:
Attribuzione: Il nome dell'autore originale, Francesco Paolo Passaro, e i riferimenti al progetto "Caveman Compression" devono essere mantenuti in ogni copia o parte sostanziale del software.
Uso Non Commerciale: È severamente vietato l'uso del software, dei suoi derivati o dei risultati da esso prodotti per fini di lucro, vendita, o integrazione in prodotti commerciali a pagamento senza previo accordo scritto.
Divieto di Ridistribuzione Pubblica: Il software non può essere caricato su repository pubblici, specchi (mirror) o distribuito a terzi al di fuori del contesto originale senza l'espresso consenso scritto dell'autore.
Open Source "As-Is": Il software è fornito "così com'è", senza garanzie di alcun tipo. L'autore non è responsabile per eventuali danni derivanti dall'uso del software.
Qualsiasi violazione dei punti sopra indicati comporterà la revoca immediata della licenza d'uso.
Per richieste di autorizzazione alla divulgazione o usi commerciali, contattare: passaroweb@gmail.com
