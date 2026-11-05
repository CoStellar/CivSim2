const fs=require("fs");
const L="file:src/CivSim.Data/ContentLoader.cs";
const C=[
["biomes.json","Definicje biomów lądowych i wodnych: zakresy temperatury i wilgotności, koszt ruchu, żyzność bazowa, żywność, materiały zbierane (drewno) i kolory podglądu.",["konfiguracja","dane-gry","biomy","generator-świata"],"simple",["goods.json"]],
["effects.json","Typy efektów (GDD 10.1) nakładanych przez zdarzenia, technologie i budynki: cel (pole, osada, cywilizacja), zmieniana statystyka i sposób kumulacji.",["konfiguracja","dane-gry","efekty","modyfikatory"],"simple",[]],
["eras.json","Lista er od epoki kamienia do ery kosmicznej z długością tury w miesiącach i technologią progową otwierającą erę.",["konfiguracja","dane-gry","ery","długość-tury"],"simple",["technologies.json"]],
["events.json","Losowe zdarzenia (GDD 10.2, etap 3h) z częstością na 10 000 lat, warunkami, łagodzącymi technologiami, efektami, zyskiem żyzności i rozprzestrzenianiem w sieci handlu.",["konfiguracja","dane-gry","zdarzenia","losowość"],"moderate",["effects.json","biomes.json","technologies.json"]],
["goods.json","Dobra przechowywane w magazynach osad: era, kategoria magazynowa, psucie na rok i wartość bazowa w jednostkach wymiany.",["konfiguracja","dane-gry","ekonomia","dobra"],"simple",[]],
["perception.json","Zasady widzenia i pamięci cywilizacji (GDD 8.2): zasięg wzroku osad, zwiadowcy, pamięć ustna w latach i maksymalna długość wypraw.",["konfiguracja","dane-gry","mgła-wojny","pamięć-mapy"],"simple",["population.json"]],
["planets.json","Presety planet: nachylenie osi, mimośród orbity, gwiazda i orbita, atmosfera z cyklem węglowym, woda, rzeźba oraz zasady pór roku i cykli klimatycznych.",["konfiguracja","dane-gry","planety","klimat","generator-świata"],"moderate",[]],
["population.json","Zasady populacji: wzrost, głód, zdrowie, zbieractwo, odrastanie dzikiej żywności, strefy wpływów, migracja plemion, komfort klimatyczny i uproszczony podbój.",["konfiguracja","dane-gry","populacja","migracja","ai-plemion"],"moderate",["strata.json","goods.json"]],
["recipes.json","Jednoetapowe receptury produkcji er 1-2 (np. narzędzia kamienne, ceramika, chleb) z wejściami, wyjściami, liczbą pracowników i czasem.",["konfiguracja","dane-gry","produkcja","receptury"],"simple",["goods.json"]],
["resources.json","Złoża naturalne rozmieszczane przez generator świata: dopuszczalne biomy i rzeźba, gęstość, zasobność, głębokość, odnawialność i plony zbieractwa.",["konfiguracja","dane-gry","zasoby","generator-świata"],"simple",["biomes.json","goods.json"]],
["settlements.json","Poziomy osad (populacja, pojemność magazynu, mobilność, porzucenie), udźwig na osobę oraz zapasy i warunki startowe nowego plemienia.",["konfiguracja","dane-gry","osady","magazyn"],"simple",["goods.json","technologies.json"]],
["strata.json","Warstwy społeczne (producenci, rzemieślnicy, wojownicy) z docelowymi udziałami, potrzebami podstawowymi i wyższymi oraz punktami nauki.",["konfiguracja","dane-gry","warstwy-społeczne","potrzeby"],"moderate",["goods.json"]],
["technologies.json","Graf technologii er 1-2: koszty, wymagania, inspiracje, dyfuzja od sąsiadów i odblokowania (receptury, zbieractwo, efekty).",["konfiguracja","dane-gry","technologie","nauka"],"simple",["recipes.json","effects.json","resources.json","biomes.json","goods.json"]],
["trade.json","Zasady sieci handlu bez fizycznego transportu: zasięg połączeń, strata na pole, rezerwa żywności i zakres cen wewnętrznych.",["konfiguracja","dane-gry","handel","ekonomia"],"simple",["goods.json"]]];
const nodes=[{id:"document:data/README.md",type:"document",name:"README.md",filePath:"data/README.md",summary:"Opis katalogu z zawartością gry w JSON: zasada danych walidowanych przy ładowaniu oraz tabela planowanych plików z ich treścią i etapem wprowadzenia.",tags:["dokumentacja","dane-gry","przegląd"],complexity:"simple"}];
const edges=[{source:"document:data/README.md",target:L,type:"documents",direction:"forward",weight:0.5}];
for(const [f,s,t,c,deps] of C){const id="config:data/"+f;
nodes.push({id,type:"config",name:f,filePath:"data/"+f,summary:s,tags:t,complexity:c});
edges.push({source:id,target:L,type:"configures",direction:"forward",weight:0.6});
edges.push({source:"document:data/README.md",target:id,type:"documents",direction:"forward",weight:0.5});
for(const d of deps)edges.push({source:id,target:"config:data/"+d,type:"depends_on",direction:"forward",weight:0.6});}
nodes.find(n=>n.id==="config:data/planets.json").languageNotes="Gwiazda i orbita liczone z rzeczywistych wzorów (prawo Keplera); wystarczy podać dwie z czterech wielkości.";
fs.writeFileSync("intermediate/batch-3.json",JSON.stringify({nodes,edges},null,1));
console.log(nodes.length,edges.length);
