# ADR 0001: Silnik: Godot 4 + C#

**Status:** przyjęta, 2026-11-03

## Kontekst

Potrzebujemy silnika do wyświetlania mapy i UI oraz języka wydajnego przy bardzo długich symulacjach. Patryk robi grafikę i chce ją sam wstawiać w edytorze.

## Decyzja

Godot 4 w wersji .NET, cała logika w C# na aktualnym .NET LTS.

## Rozważane alternatywy

Python był za wolny przy tysiącach jednostek i długich przebiegach. Java bez edytora gier. C++ daje przewagę wydajności, której tu nie potrzebujemy, kosztem trudnych błędów pamięci. Unity: cięższy i z licencją.

## Konsekwencje

Edytor wizualny dla Patryka, wydajność bliska Javy. Eksport do przeglądarki odpada, bo Godot nie eksportuje oficjalnie C# do web. Platformy: Windows, Linux, macOS.
