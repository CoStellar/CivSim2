# Multiplayer: pomysły na przyszłość

Rzeczy, których teraz nie budujemy, ale nie chcemy ich zablokować architekturą. Zapisane na prośbę Patryka (2026-11-25).

## Kooperacja: kilku graczy prowadzi jedną cywilizację

**Skąd:** decyzja P3 z 2026-11-25: teraz każdy gracz ma swoją cywilizację; kooperacja jedną cywilizacją kiedyś jako dodatkowy tryb.

**Jak mogłoby to działać:**

- W lobby dwóch lub więcej graczy siada na tym samym miejscu (ludzie).
- Wszyscy widzą tę samą mgłę wojny i ten sam magazyn; każdy może wydać każdy rozkaz (najprostsza wersja),
  albo cywilizacja dzieli się na **role** (np. gospodarka i budowy / wojsko / badania i dyplomacja) i gracz widzi przyciski swojej roli.
- Rozkazy niosą już dziś identyfikator gracza i cywilizacji (ADR 0008), więc serwer może pilnować ról bez zmian w symulacji.
- Znaczniki na mapie („tu buduję”, „tam atakuję”) i czat cywilizacji, żeby gracze się nie dublowali.
- Pauzy: wspólna pula cywilizacji (3 na cywilizację, nie na gracza).
- Gdy jeden gracz się rozłączy, cywilizację prowadzą pozostali; AI przejmuje cywilizację dopiero, gdy wyjdą wszyscy.

**Co trzeba zostawić otwarte już teraz:** miejsce w lobby jako lista graczy (nie jeden gracz), widok wysyłany „dla cywilizacji”, a nie „dla gracza”
(już tak jest: `PlayHost.View(civId)`), zapis gry z listą graczy na cywilizację.

## Inne

- **Drużyny** (np. 2 na 2) z wspólnym celem partii i wspólną mgłą sojuszników.
- **Lista serwerów** na pośredniku (zamiast kodu pokoju).
- **Połączenie wprost po zapoznaniu przez pośrednika** (UDP hole punching) jako oszczędność transferu, z pośrednikiem jako zapasem.
- **Powtórki partii** dla widzów (zapis = lista rozkazów, więc powtórka to wczytanie z odtwarzaniem).
- **Steam** (lobby, zaproszenia, pośrednik Valve) przy wydaniu gry, za tym samym `IConnection`.
