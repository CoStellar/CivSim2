# Multiplayer: pomysły na przyszłość

Rzeczy, których teraz nie budujemy, ale nie chcemy ich zablokować architekturą. Zapisane na prośbę Patryka (2026-11-25).

## Kooperacja: kilku graczy prowadzi jeden lud

**Skąd:** decyzja P3 z 2026-11-25: teraz każdy gracz ma swój lud; kooperacja jednym ludem kiedyś jako dodatkowy tryb.

**Jak mogłoby to działać:**

- W lobby dwóch lub więcej graczy siada na tym samym miejscu (ludzie).
- Wszyscy widzą tę samą mgłę wojny i ten sam magazyn; każdy może wydać każdy rozkaz (najprostsza wersja),
  albo lud dzieli się na **role** (np. gospodarka i budowy / wojsko / badania i dyplomacja) i gracz widzi przyciski swojej roli.
- Rozkazy niosą już dziś identyfikator gracza i ludu (ADR 0008), więc serwer może pilnować ról bez zmian w symulacji.
- Znaczniki na mapie („tu buduję”, „tam atakuję”) i czat ludu, żeby gracze się nie dublowali.
- Pauzy: wspólna pula ludu (3 na lud, nie na gracza).
- Gdy jeden gracz się rozłączy, lud prowadzą pozostali; AI przejmuje lud dopiero, gdy wyjdą wszyscy.

**Co trzeba zostawić otwarte już teraz:** miejsce w lobby jako lista graczy (nie jeden gracz), widok wysyłany „dla ludu”, a nie „dla gracza”
(już tak jest: `PlayHost.View(civId)`), zapis gry z listą graczy na lud.

## Inne

- **Drużyny** (np. 2 na 2) z wspólnym celem partii i wspólną mgłą sojuszników.
- **Lista serwerów** na pośredniku (zamiast kodu pokoju).
- **Połączenie wprost po zapoznaniu przez pośrednika** (UDP hole punching) jako oszczędność transferu, z pośrednikiem jako zapasem.
- **Powtórki partii** dla widzów (zapis = lista rozkazów, więc powtórka to wczytanie z odtwarzaniem).
- **Steam** (lobby, zaproszenia, pośrednik Valve) przy wydaniu gry, za tym samym `IConnection`.
