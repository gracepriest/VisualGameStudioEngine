# BibleHub GridPanel sample

A full home-page mock rebuilt from the earlier BibleGateway canvas sample, now using
nested `GridPanel` containers instead of absolute positioning.

## What the grid does

- `PageGrid` owns the vertical page rhythm: top bar, header, promo, hero, verse,
  study cards, articles, newsletter, footer.
- Each section is a nested `GridPanel` with its own explicit columns and rows.
- The three study cards and three article cards are `GridPanel`s in a nested
  three-column `StudyGrid` / `ArticleGrid`.
- Card kickers span the card; their title, body, and link sit in the middle column
  with 18px gutters.
- The article image strips are nested `GridPanel`s with a fixed 120px row.
- On a narrow screen the CSS overrides the grid columns to one column so the
  page remains readable; the panel structure stays intact.

## Run it

1. Open `BibleHub.blproj` in Visual Game Studio.
2. Press F5, or build and open `bin\Debug\Home.html`.

The form uses a DOM-style code-behind, so the original search, subscribe, and
audio-mock handlers still work. The page is a mock and is not affiliated with
Bible Gateway or BibleHub.
