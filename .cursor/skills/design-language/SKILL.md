---
name: design-language
description: NzbDav frontend design language and styling guidelines, derived from the dmbdb (Debrid Media Bridge Dashboard) project. Use when refactoring, restyling, or building any frontend UI — pages, components, buttons, cards, badges, forms, tabs, themes, colors, spacing, or typography in frontend/app.
---

# NzbDav Design Language

Visual and styling guidelines for the NzbDav frontend. Apply these when refactoring or building UI in `frontend/app`.

## Core principles

1. **Dark-first.** The document uses the custom daisyUI `nzbdav` theme via `data-theme="nzbdav"`.
2. **daisyUI-native components.** Use daisyUI component classes and supported markup for buttons, forms, toggles, modals, alerts, badges, tabs, tooltips, and loading indicators. Prefer the wrappers in `app/components/ui`; direct daisyUI classes are also allowed.
3. **Semantic colors.** New code uses daisyUI semantic utilities such as `bg-base-100`, `text-base-content`, `btn-primary`, and `text-error`, not raw slate/blue palette utilities.
4. **Tailwind for layout.** Continue using Tailwind utilities for spacing, responsive layout, typography, and one-off composition around daisyUI components.
5. **Density with breathing room.** Prefer native daisyUI size modifiers (`btn-xs`, `btn-sm`, `input-sm`) for dense controls inside generously spaced pages (`gap-8`, `p-4 md:px-8`).

## Theme token vocabulary

The `nzbdav` theme in `app/app.css` is the source of truth. Its primary daisyUI variables are:

| Token | Role |
|-------|------|
| `--color-base-100/200/300` | Surfaces through deepest page background |
| `--color-base-content` | Default foreground |
| `--color-primary` / `--color-primary-content` | Primary actions and their foreground |
| `--color-secondary` / `--color-accent` | Secondary semantic accents |
| `--color-neutral` / `--color-neutral-content` | Neutral surfaces and muted content |
| `--color-info/success/warning/error` | Status colors |

The old `--app-*` variables remain compatibility aliases for existing CSS modules. Do not use them as the source of truth for new components.

## Color semantics

- **Accent / interactive:** blue (`blue-400` text and links, `blue-500/600` fills, `focus:border-blue-500`). Active tab = `text-blue-400 border-blue-400`.
- **Status dots** (small `rounded-full` circles, `h-2 w-2` in lists, `w-3 h-3 md:w-4 md:h-4` on cards):
  - running/healthy → `bg-green-400` or `bg-emerald-400`
  - stopped/error → `bg-red-400` or `bg-rose-400`
  - degraded/unknown → `bg-yellow-400` or `bg-amber-400`
  - inactive → `bg-slate-500`
- **Action button intents:** start/save = green (`bg-green-500 hover:bg-green-600`), stop/delete = red (`bg-red-500 hover:bg-red-600`), restart = yellow (`bg-yellow-400 hover:bg-yellow-500`), apply/download = blue (`bg-blue-500 hover:bg-blue-600`).
- **Alerts:** tinted translucent panels, e.g. warning = `rounded border border-amber-600/50 bg-amber-500/10 text-amber-200 px-3 py-2 text-xs`.

## Surfaces and structure

- **Card:** `bg-gray-800 rounded-lg shadow-md p-2.5 md:p-3`, hover `hover:bg-gray-800/70`. Whole card may be clickable.
- **Panel / grouped section:** `rounded border border-slate-700/70` with an internal header row and `border-t` divider.
- **Sidebar:** fixed-width (`max-w-[250px]`), `bg-gray-900 border-r border-slate-800`, collapsible on mobile (overlays as `absolute`, toggle pinned bottom-left as a `rounded-full` tab).
- **Modal / overlay:** backdrop `fixed inset-0 z-50 bg-slate-900/80`; dialog `rounded border border-slate-700 bg-slate-900 shadow-xl max-w-xl`.
- **Ghost icon button:** `px-2 py-1.5 rounded bg-white/10 hover:bg-white/20`.
- **Badge / pill:** `text-[10px] px-1.5 py-0.5 rounded-full border border-slate-600/60 bg-slate-700/40 text-slate-200`; use `font-mono` for numeric metrics.

## Typography

- Page title: `text-4xl font-bold`, with optional subtitle `text-xs text-slate-400 mt-1`.
- Section heading: `text-xl font-semibold`. Sidebar/nav group: `text-lg font-bold`.
- Group micro-label: `text-[11px] uppercase tracking-wide text-slate-500`.
- Body/meta hierarchy: `text-white` → `text-slate-300` → `text-slate-400` → `text-slate-500`.
- Metrics and numbers: `font-mono`.

## Buttons

- Use `btn` plus a semantic modifier: `btn-primary`, `btn-success`, `btn-error`, `btn-warning`, `btn-outline`, or `btn-ghost`.
- Use the native size scale: `btn-xs`, `btn-sm`, default, and `btn-lg`. Use `btn-circle` for icon-only circular buttons.
- Prefer the shared `Button` wrapper when its variant API fits.
- Icons inside buttons still use explicit Material Symbol sizes such as `!text-[18px]`.

## Icons

Use **Material Symbols Rounded** (variable font, weight 300, `FILL 0` default; `FILL 1` for emphasized/filled states like play/stop). Size icons explicitly with `!text-[Npx]` rather than relying on the inherited font size. Icon names as text content, e.g. `play_arrow`, `refresh`, `expand_more`, `save`, `close`.

## Forms

- Use daisyUI `fieldset`, `fieldset-legend`, and `label` for grouped fields.
- Controls use `input`, `select`, `textarea`, `checkbox`, `radio`, and `toggle`.
- Use native semantic states such as `input-error` and native size modifiers instead of recreating borders/focus rings.
- Do not add global element rules for inputs, selects, or checkboxes; they override daisyUI component styling.

## Tabs

Use `tabs tabs-border` with `tab`, `tab-active`, and `tab-disabled`. Tabs may pair a Material Symbol with a label.

## Layout

- App shell: full-height (`h-dvh`) flex row — sidebar + `flex-1 min-h-0 overflow-y-auto` main pane. Guard flex children with `min-w-0`.
- Page container: `px-4 py-4 md:px-8` with `flex flex-col gap-8` between page-level blocks.
- Card grids: `grid grid-cols-1 lg:grid-cols-2 gap-4`.
- Mobile-first with `sm:`/`md:`/`lg:` steps; card internals stack on mobile (`flex-col`) and go horizontal at `sm:` (`sm:flex-row sm:items-center sm:justify-between`).

## Motion

- Standard transition: `transition-all ease-in-out duration-200`.
- Loading: `animate-spin` on a refresh/`cached` icon.
- Chevron rotation for expand/collapse: `rotate-180` toggle with `transform transition ease-in-out`.
- Hover micro-scale on grouped icons: `group-hover:scale-105`.
- Drag feedback: `opacity-75 scale-[0.99]`, `cursor-grab active:cursor-grabbing`.

## Scrollbars

Hide by default in dense panes (`.no-scrollbar`), or show a thin styled one (`.yes-scrollbar`: 6px wide, rounded accent-colored thumb).

## Applying themes

Set `data-theme="nzbdav"` on the document root. The legacy `data-appearance-theme="dark"` attribute and utility remaps remain temporarily for existing pages; new code should use daisyUI semantic classes and must not depend on those remaps.

## NzbDav-specific notes

- The frontend is React Router 8 with Tailwind 4, daisyUI 5, and CSS modules (not Vue/Nuxt).
- Existing route CSS modules remain supported through the `--app-*` compatibility aliases and can migrate incrementally.
- Keep Material Symbols Rounded for icons; daisyUI does not provide an icon set.
- Run `npm run typecheck` in `frontend/` after styling refactors.
