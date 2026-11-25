# ADR 0030: Pokój z negocjacji

**Status:** przyjęta, 2026-11-23. Punkty 1–6: decyzje Patryka z kwestionariusza (wszystkie rekomendacje). Punkt 7: decyzja Patryka
z 2026-11-23, wszystkie rekomendacje (7.1A, 7.2A, 7.3A, 7.4B, 7.5B). Wdrożenie: PR `feat/peace-negotiation`, opis w `docs/sim/walka.md`. Roadmapa: zadanie 8.

## Kontekst

Do tej pory pokój proponowało się z daniną w **procentach wszystkich dóbr** przegranego (jedzenie najwyżej 10%, danina jedzie karawaną).
Danina była możliwa dopiero po 2 minutach wojny i po co najmniej 5 zabitych. Gra podpowiadała procent z wyniku wojny
(`SuggestedTribute`, od −30% do +30%). Po pokoju zawsze był rozejm na 3 minuty, a złamanie rozejmu kosztowało reputację.
Osad nie dało się oddać w pokoju.

AI szukała pokoju tylko wtedy, gdy przeciwnik prowadził w wyniku wojny o ponad 20 punktów (albo losowo, 3% na myślenie).
Wojna nic nie kosztowała z samego trwania: cywilizacja mogła walczyć bez końca, póki miała ludzi.

## Decyzja (kwestionariusz z 2026-11-21, odpowiedzi Patryka z 2026-11-23)

1. **Żądanie w dobrach (1B):** wybrane dobra i ilości, jak przy wymianie handlowej (np. 80 brązu + 40 koni). Okno pokoju pokazuje,
   ile przegrany ma każdego dobra, które znamy.
2. **Osada w pokoju (2B):** można jej żądać, ale nie stolicy i tylko takiej, którą nasze wojsko teraz oblega albo która leży
   najwyżej 12 heksów od naszej ziemi. Przechodzi z mieszkańcami i budynkami, jak przy zajęciu.
3. **Okres pokoju (3B):** do wyboru w propozycji: 3, 6 albo 10 minut. AI chętniej przyjmuje dłuższy pokój, gdy przegrywa,
   a krótszy, gdy wygrywa. Złamanie pokoju kosztuje reputację jak dziś, a przy 10 minutach dwa razy więcej.
4. **Podpowiedź (4A):** przycisk „Suggest terms”: gra wypełnia propozycję warunkami, które przegrany według przebiegu wojny
   powinien przyjąć. Gracz może je zmienić.
5. **Kiedy AI przyjmuje (5A):** AI liczy wartość żądania: dobra po cenie z `worth`, osadę jako 10 × mieszkańcy + połowa ceny
   jej budynków. Przyjmuje, gdy wartość nie przekracza tego, co według przebiegu wojny „powinna” oddać, z zapasem 20% dla
   spokojnych charakterów. Tak samo liczy, czego sama żąda od gracza.
6. **Kto żąda (6A):** tylko strona z lepszym wynikiem wojny. Przegrywający może proponować biały pokój albo sam coś oddać.

## 7. Zmęczenie wojną (decyzja Patryka z 2026-11-23: 7.1A, 7.2A, 7.3A, 7.4B, 7.5B)

Pytanie Patryka (2026-11-23): negocjuje się wtedy, gdy jedno z państw nie może już udźwignąć wojny, a zaatakowanemu, który wygrywa,
zwycięstwo musi się opłacić. Jak to wyglądało w historii?

### Co mówi historia

| Wojna | Jak się skończyła | Wniosek dla gry |
| --- | --- | --- |
| Wojna peloponeska, pokój Nikiasza (421 p.n.e.) | Po 10 latach obie strony wyczerpane (zaraza, spustoszone pola, koszt floty). Pokój bez zwycięzcy, powrót do stanu sprzed wojny. Nie przetrwał. | Gdy **obie** strony są zmęczone, łatwo o biały pokój. |
| I wojna punicka (241 p.n.e.) | Kartagina straciła flotę i pieniądze. Oddała Sycylię, którą Rzym już trzymał, i płaciła 3200 talentów **w ratach przez 10 lat**. | Oddaje się ziemię, którą wróg **już trzyma** (2B). Danina w ratach wiąże pokój. |
| II wojna punicka (201 p.n.e.) | Twarde warunki: 10 000 talentów w ratach przez 50 lat, prawie cała flota, zakaz wojny bez zgody Rzymu. | Przegrany, który nie może walczyć, przyjmie dużo. |
| Wojna stuletnia (1337–1453) | Wiele rozejmów z braku pieniędzy i przez dżumę (rozejm w Calais, 1347). Pokój w Brétigny (1360): okup za króla Jana II i ziemie w zamian za zrzeczenie się korony. | Wojny gasną, gdy kończą się pieniądze i ludzie, nie tylko po przegranej bitwie. Krótkie rozejmy przeplatają długą wojnę. |
| Wojna trzydziestoletnia, pokój westfalski (1648) | Rokowania trwały latami, a walki szły dalej. Szwecja i Francja dostały ziemie, które zajęły. Niemcy wyludnione. | Zasada „kto co trzyma, ten to ma” (*uti possidetis*). Wojna rujnuje gospodarkę obu stron. |
| Kongres wiedeński (1815) | Pokonana Francja dostała łagodne warunki i wróciła do równowagi sił. Długi pokój. | Umiarkowane warunki dają trwały pokój. |
| Wojna francusko-pruska, pokój frankfurcki (1871) | Alzacja i Lotaryngia (zajęte) i 5 mld franków. Niemcy okupowały Francję do spłaty. Francja chciała odwetu. | Twarde warunki rodzą chęć odwetu. Zaborca łatwo bierze zajęte osady. |
| Wojna brytyjsko-amerykańska, traktat gandawski (1814) | Obie strony zmęczone, nikt nie wygrał: powrót do stanu sprzed wojny. | Remis plus zmęczenie = biały pokój. |
| Wojny obronne (np. Rosja 1812–1815) | Zaatakowany, który wygrał, dostał ziemie, odszkodowania i pas ochronny. Agresorzy, którzy przegrali, płacili odszkodowania (Francja 1815, Niemcy 1919). | **Zaatakowany, który wygrywa, powinien dostać więcej** niż zwykły zwycięzca. |

Wspólny wniosek: wojna kończy się, gdy dalsza walka kosztuje bardziej, niż obiecuje zysk, i gdy obie strony zgadzają się, kto jest
silniejszy. Wynik wojny (zabici, zajęte osady) mówi tylko, kto wygrywa. Nie mówi, kto już nie może walczyć.

### Propozycja mechaniki

**Zmęczenie wojną** (0–100) dla każdej cywilizacji w każdej wojnie, widoczne w oknie dyplomacji. Rośnie z:
- czasem wojny (np. +1 na minutę),
- stratami (ludzie zabici przez wroga, w procentach ludności),
- głodem (racje niewydane z powodu braku jedzenia),
- osadami oblężonymi, złupionymi, zajętymi i karawanami przechwyconymi przez wroga.

Spada powoli w pokoju. Proponowane skutki:

| | Wariant | Opis |
| --- | --- | --- |
| 7.1 | **A (rekomendacja)** | Zmęczenie wchodzi do „tego, co strona powinna oddać” (5A): wynik wojny + różnica zmęczenia. Przy wysokim zmęczeniu AI sama szuka pokoju, a przy obustronnie wysokim chętnie przyjmuje biały pokój. |
| | B | Tylko informacja na ekranie, bez wpływu na AI. |
| 7.2 | **A (rekomendacja)** | Zmęczenie kosztuje: od 50 wzwyż mniej pracy w warsztatach (do −20% przy 100), bo ludzie są w wojsku i w żałobie. Gracz to odczuwa, nie tylko widzi. |
| | B | Bez kar, tylko wpływ na AI. |
| 7.3 | **A (rekomendacja)** | Kto wypowiedział wojnę i przegrał, oddaje o 25% więcej (odszkodowanie dla zaatakowanego). Zaatakowany, który wygrywa, może żądać osady także 12 heksów od **każdej** swojej oblężonej wcześniej osady (pas ochronny). *Przy wdrożeniu: oblężona osada to nasza ziemia, więc pas ochronny już obejmuje zasada 2B (12 heksów od naszej ziemi); nie ma osobnej reguły.* |
| | B | Bez premii dla zaatakowanego. |
| 7.4 | A | Danina w ratach przez okres pokoju (3, 6 albo 10 minut). Złamanie pokoju zatrzymuje raty. |
| | **B (rekomendacja na demo)** | Danina od razu, jak dziś (jedna karawana). Raty to dobry pomysł na po demo. |
| 7.5 | A | Odwet: przegrany, który oddał osadę, nie traci reputacji za wypowiedzenie wojny zwycięzcy po końcu pokoju. |
| | **B (rekomendacja na demo)** | Bez tego. Do rozważenia po testach demo. |

## Konsekwencje

- Nowa komenda pokoju z listą dóbr, osadą i długością pokoju zastępuje `ProposePeaceCommand` z procentem. Przy implementacji trzeba
  sprawdzić zgodność starych zapisów gier i w razie potrzeby podnieść wersję zapisu.
- Okno pokoju w kliencie, AI (proponowanie, ocena i podpowiedź), testy, pomiar wojen w `--sim-report`: liczba pokojów, średnia
  danina, przejęte osady, a przy 7 także średnie zmęczenie na końcu wojny.
- Scenariusz 03 nie testuje już pokoju (`noPeace`, decyzja Patryka 2026-11-23). Pokój dostanie własny scenariusz.
