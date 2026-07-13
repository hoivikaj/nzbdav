---
name: daisyui-5-components
description: Builds and refactors Tailwind CSS 4 interfaces with native daisyUI 5 components, semantic colors, themes, and accessible markup. Use for frontend UI work involving daisyUI, Tailwind components, forms, navigation, overlays, feedback, data display, layout, or theming.
---

# daisyUI 5 Components

Source: [official daisyUI 5 LLM reference](https://daisyui.com/llms.txt), version 5.6.x.

Use this skill alongside the project design-language skill. Project conventions and established wrappers take precedence where they intentionally narrow daisyUI usage.

## Component discovery

Before writing UI code:

1. Identify the intended behavior, content shape, interaction model, and responsive needs.
2. Read [COMPONENTS.md](COMPONENTS.md) and shortlist the native daisyUI components that match.
3. If the choice is ambiguous, compare at least three candidate component docs. If the user names a component, read that official doc first.
4. Prefer one native component or a composition of native components over recreating its CSS.
5. For newer or behavior-heavy components, fetch the linked official documentation before implementing; do not rely on memory alone.

## Implementation rules

- daisyUI 5 requires Tailwind CSS 4. Install with `npm i -D daisyui@latest` and register it in CSS with `@plugin "daisyui";`.
- Use the required component class, then supported part and modifier classes. Never invent daisyUI class names.
- Start with the default component variant. Add semantic color, style, size, or placement modifiers only when intent requires them.
- Prefer daisyUI semantic colors (`base-*`, `primary`, `success`, `warning`, `error`) over fixed Tailwind palette colors so themes remain readable.
- Use Tailwind utilities for responsive layout, spacing, and one-off composition. Avoid custom CSS when native classes and utilities suffice.
- When a utility loses on specificity, use Tailwind's trailing `!` only as a last resort.
- Preserve native HTML semantics and accessibility: labels for controls, unique radio names, correct ARIA roles, keyboard operation, focus visibility, and real disabled attributes.
- Use responsive prefixes with layout-changing classes, for example `footer-vertical sm:footer-horizontal` or `modal-bottom sm:modal-middle`.
- Do not add `dark:` merely to support a daisyUI theme; semantic colors adapt through `data-theme`.
- Keep charts, branded artwork, and other intentionally theme-independent visuals on explicit colors when needed.

## Native behavior choices

- Modal: use the recommended `<dialog class="modal">` approach with `showModal()`/`close()`, `modal-box`, `modal-action`, and optional `modal-backdrop`.
- Dropdown and megamenu: prefer accessible popover/details patterns from the current docs; account for mobile with a dropdown or drawer.
- Tabs: buttons are appropriate when application state controls content; radio tabs are needed for daisyUI's CSS-only tab-content behavior.
- Accordion: radio inputs allow only one open item; use collapse for independently open sections.
- Filter/rating/swap: keep the native radio or checkbox control in the DOM rather than simulating selection on a generic div.
- Table: wrap in `overflow-x-auto` for small screens.
- Tooltip: use `data-tip` for plain text or `tooltip-content` for rich content.
- Validator: combine `validator` with `input`, `select`, or `textarea`, followed by `validator-hint`.
- Calendar: daisyUI only styles supported calendar libraries; it does not supply calendar behavior.

## Themes

Configure themes in the CSS plugin block and activate them with `data-theme`.

```css
@import "tailwindcss";
@plugin "daisyui" {
  themes: light --default, dark --prefersdark;
}
```

Custom themes must define all base, brand, state, radius, size, border, depth, and noise variables documented in the [theme guide](https://daisyui.com/docs/themes/).

## Verification

After UI changes:

1. Run typecheck, tests, lint, and the production build appropriate to the project.
2. Preview the affected flows in a browser at desktop and narrow widths.
3. Exercise keyboard navigation, disabled/loading/error states, modal dismissal, overflow, and theme contrast.
4. Confirm class names against the current official component page when behavior or rendering differs from expectations.

## Reference

- [Complete component catalog](COMPONENTS.md)
- [All components](https://daisyui.com/components/)
- [Configuration](https://daisyui.com/docs/config/)
- [Themes](https://daisyui.com/docs/themes/)
- [Colors](https://daisyui.com/docs/colors/)
