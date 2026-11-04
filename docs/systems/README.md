# Opisy systemów

Każdy system rdzenia symulacji ma tu własny plik `<system>.md`. Pliki pisze i aktualizuje Claude Code
w tym samym PR, który zmienia system. Graf wiedzy z Understand Anything pokazuje kod; te pliki
tłumaczą, **po co** system istnieje i jak przez niego płyną dane.

## Szablon

~~~markdown
# <Nazwa systemu>

**W jednym zdaniu:** co ten system robi.

**Faza tury:** w której fazie (rozdział 11.1 GDD) działa.

## Dane wejściowe i wyjściowe
- Czyta: …
- Zmienia: …
- Publikuje w migawce: …

## Przepływ

```mermaid
flowchart LR
  A[Wejście] --> B[Krok] --> C[Wyjście]
```

## Zasady i wzory
Najważniejsze reguły i wzory, z odwołaniem do rozdziału GDD.

## Pliki danych
Które pliki z `data/` konfigurują ten system.

## Testy
Które testy pilnują tego systemu.

## Wydajność
Ostatni pomiar (czas na turę, pamięć) i znane wąskie gardła.
~~~
