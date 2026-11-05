const fs=require('fs');const d=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
const L=[
['layer:core-foundation','Fundamenty rdzenia','Deterministyczne podstawy CivSim.Core: moduł HexGlobe i mapa w płaskich tablicach, liczby fixed-point i typ Amount, własny PRNG ze strumieniami, model stanu gry (GameState, cywilizacje, osady), definicje zawartości i hashowanie stanu do testów determinizmu.'],
['layer:simulation','Symulacja tury i systemy gry','Pętla tury (TurnRunner, fazy, migawki w double bufferze), komendy ICommand oraz systemy ekonomii, populacji i migracji, badań, zdarzeń, percepcji i AI plemion, które co turę zmieniają stan.'],
['layer:worldgen','Generowanie świata i klimat','Generator planety-kuli: orbita gwiazdy, bilans energii, cykl węglowy, pory roku, epoki lodowe, poziom morza, szum całkowitoliczbowy oraz walidacja i raport wygenerowanego świata.'],
['layer:content','Zawartość gry w danych','Pliki JSON w data/ (biomy, technologie, dobra, receptury, ery, zdarzenia, warstwy społeczne itd.) wraz z projektem CivSim.Data, który je ładuje i waliduje przy starcie.'],
['layer:server-protocol','Serwer i protokół','Autorytatywny serwer gry (sprawdzanie komend, filtrowanie widoku przez mgłę wojny, zapis gry) oraz wersjonowany protokół MessagePack z transportem w pamięci, przez który klient rozmawia z serwerem.'],
['layer:client','Klient Godot','Projekt Godot w C# (game/): scena główna, widok mapy heksowej, nakładki, znaczniki osad, HUD i wykres tur; zależy wyłącznie od CivSim.Protocol.'],
['layer:headless','Uruchamianie headless','Aplikacja konsolowa CivSim.Headless do przebiegów bez okna, benchmarków czasu tury, raportów CSV i eksportu mapy do PNG.'],
['layer:test','Testy','Zestaw testów xUnit w tests/CivSim.Tests: testy jednostkowe systemów, determinizmu (hash stanu), serwera, zapisu i serializacji protokołu.'],
['layer:documentation','Dokumentacja projektu','Dokumenty po polsku: GDD, ROADMAP, CLAUDE.md, decyzje architektoniczne (ADR) w docs/decisions, opisy systemów w docs/systems, prompty sesji i instrukcja instalacji.'],
['layer:build-config','Build, konfiguracja i CI','Ustawienia budowania i repozytorium: solucja .NET, global.json, Directory.Build.props, .gitattributes, ustawienia Claude Code oraz workflow GitHub Actions.'],
];
const found=new Set(['Map','Numerics','Random','State','Content','Hashing']);
function pick(n){const p=n.filePath;
 if(p.startsWith('src/CivSim.Core/')){const s=p.split('/')[2];if(s==='WorldGen')return'layer:worldgen';if(found.has(s))return'layer:core-foundation';return'layer:simulation';}
 if(p.startsWith('data/')||p.startsWith('src/CivSim.Data/'))return'layer:content';
 if(p.startsWith('src/CivSim.Protocol/')||p.startsWith('src/CivSim.Server/'))return'layer:server-protocol';
 if(p.startsWith('src/CivSim.Headless/'))return'layer:headless';
 if(p.startsWith('tests/'))return'layer:test';
 if(p.startsWith('game/'))return'layer:client';
 if(n.type==='document')return'layer:documentation';
 return'layer:build-config';}
const m={};d.fileNodes.forEach(n=>{const l=pick(n);(m[l]=m[l]||[]).push(n.id)});
const out=L.filter(([id])=>m[id]).map(([id,name,description])=>({id,name,description,nodeIds:m[id]}));
const tot=out.reduce((s,l)=>s+l.nodeIds.length,0);if(tot!==d.fileNodes.length)throw Error('count '+tot);
fs.writeFileSync(process.argv[3],JSON.stringify(out,null,2));out.forEach(l=>console.log(l.id,l.nodeIds.length));console.log('total',tot);
