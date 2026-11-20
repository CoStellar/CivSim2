# Znane problemy (nowa gra)

Problemy, o których wiemy, a których jeszcze nie naprawiono. Gdy naprawisz punkt, przenieś go do „Naprawione” z numerem PR;
gdy znajdziesz nowy, dopisz go z datą, źródłem i tym, co już wiadomo. Problemy gry legacy: `docs/legacy/known-issues.md` (nie zajmujemy się nimi).

## Otwarte

| # | Problem | Skąd wiemy | Co wiadomo | Co dalej |
| --- | --- | --- | --- | --- |
| 1 | **FPS klienta nie jest zmierzony.** | — | W chmurze nie da się uruchomić Godota z oknem. | Pomiar na komputerze Patryka. |
| 2 | **Skoki czasu taktu.** Średnio 0,9 ms, najgorszy takt 212 ms. | `--sim-scenario 10_siege_supply --minutes 6`, 2026-11-20 | Przyczyny jeszcze nie szukaliśmy. | Pomiar, który system trwa długo (ROADMAP, zadanie 3). |
| 3 | **Niestabilny test serwera** `ObserverLayerTests.ObserverGetsLayersAndTheFogOfAWatchedCivilization`: raz na kilka przebiegów pada w całym zestawie, sam przechodzi. | pamięć projektu | W przebiegach z 2026-11-20 nie padł. | Gdy padnie: przyczyna (zależność od czasu?), nie wyłączać testu. |
| 4 | **AI nie korzysta ze statków.** Nie wsiada na transportowce i nie szuka handlu morskiego. | przegląd 2026-11-18 | U gracza statki działają. | Po demo; do demo mapy kontynentalne. |
| 5 | **AI trochę głoduje**, głównie w świeżych obozach. | `--sim-report`, 2026-11-20: 7–8% próbek z głodem | Lepiej od zapasu osadników i chaty zbieraczy na start (#107). | Ocena Patryka po graniu. |
| 6 | **Civil service nie daje prowincji ani limitu administracji.** | plan technologii, 2026-11-18 | Daje urząd prowincji, awans do city (z Construction) i +3 do limitu oddziałów. | Etap „zarządca i prowincje” (po demo). |
| 7 | **Wolny neolit.** Pierwszy lud wchodzi w brąz ok. 33. minuty. | sonda „kiedy pierwszy raz”, 2026-11-20 | Pierwszy narzędziarz powstaje ok. 13. minuty, bo na starcie brakuje drewna; kamieniarz staje ok. 6 min po kamieniarstwie. Obniżenie `research.pace` prawie nic nie daje. | ROADMAP, zadanie 6. |
| 8 | **Mało kopalni cyny.** Tylko 4 z 32 ludów ją stawiają, odlewnie brązu stoją bez cyny. | sonda, 2026-11-20 | Ten sam mechanizm co miedź (dowóz, osadnicy przy złożu), ale cyna leży dalej. | Przy strojeniu AI. |
| 9 | **AI głównie łupi, rzadko zajmuje** (2 zajęte, 11 złupionych na grę). | `--sim-report`, 2026-11-20 | Próg 20 heksów od najbliższej osady AI; obce osady leżą zwykle dalej. | Ocena Patryka (runda testów 2). |
| 10 | **Głód oddziału w polu nie jest widoczny w panelu** (tylko w kronice). | przegląd #111 | Panel pokazuje juki, ale ukrywa linię racji dla oddziału w polu. | ROADMAP, zadanie 9. |

## Naprawione

| Problem | PR |
| --- | --- |
| AI nie stawiało kopalni miedzi w młodych obozach (złoże w osadzie bez towarów) | #109 |
| Atak na heks spoza mapy rzucał wyjątek | #111 |
| Ludy AI ginęły w wojnach w pierwszych 20–40 minutach (teraz 0,1–0,2 na grę) | #110 (oblężenie) |
| Gra stawała po odmowie ruchu, armia AI stała całą bitwę, wyjątki dla pustych list, rozwiązany oddział gubił broń | #106 |
| AI wybierało miejsca osad z wiedzy o całej mapie | #104 |
