# caveman

It is the version that is inspired by the token saving algorithm of Caveman plugin for Claude, but it was conceived without doing any porting from the original, it is a code born from scratch

# 🦴 Caveman: NLP Prompt Compressor for LLMs

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
```bash
dotnet add package Catalyst
dotnet add package Mosaik.Core
CavemanNLP License Agreement v1.0
Copyright (c) 2026 Francesco Paolo Passaro
Con la presente si concede il permesso di utilizzare, copiare e modificare questo software ("CavemanNLP") esclusivamente per scopi Open Source e NON Commerciali, alle seguenti condizioni:
Attribuzione: Il nome dell'autore originale, Francesco Paolo Passaro, e i riferimenti al progetto "Caveman Compression" devono essere mantenuti in ogni copia o parte sostanziale del software.
Uso Non Commerciale: È severamente vietato l'uso del software, dei suoi derivati o dei risultati da esso prodotti per fini di lucro, vendita, o integrazione in prodotti commerciali a pagamento senza previo accordo scritto.
Divieto di Ridistribuzione Pubblica: Il software non può essere caricato su repository pubblici, specchi (mirror) o distribuito a terzi al di fuori del contesto originale senza l'espresso consenso scritto dell'autore.
Open Source "As-Is": Il software è fornito "così com'è", senza garanzie di alcun tipo. L'autore non è responsabile per eventuali danni derivanti dall'uso del software.
Qualsiasi violazione dei punti sopra indicati comporterà la revoca immediata della licenza d'uso.
Per richieste di autorizzazione alla divulgazione o usi commerciali, contattare: passaroweb@gmail.com
