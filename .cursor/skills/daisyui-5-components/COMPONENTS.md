# daisyUI 5 Component Catalog

Official catalog: [daisyui.com/components](https://daisyui.com/components/). This reference covers all 68 components available in daisyUI 5.6.x. Follow each link for current syntax, examples, and constraints.

## Actions, state, and progress

1. **[Button](https://daisyui.com/components/button/)** — actions and choices. `btn`; colors `btn-primary|success|warning|error`; styles `btn-outline|dash|soft|ghost|link`; sizes `btn-xs` through `btn-xl`; shapes `btn-square|circle`.
2. **[Badge](https://daisyui.com/components/badge/)** — compact labels and state. `badge`; outline/dash/soft/ghost, semantic colors, and `badge-xs` through `badge-xl`.
3. **[Status](https://daisyui.com/components/status/)** — tiny visual state indicator. `status`; semantic color and `status-xs` through `status-xl`.
4. **[Loading](https://daisyui.com/components/loading/)** — indeterminate activity. `loading` plus `loading-spinner|dots|ring|ball|bars|infinity` and size modifiers.
5. **[Progress](https://daisyui.com/components/progress/)** — linear determinate progress. `progress` plus semantic color modifiers.
6. **[Radial progress](https://daisyui.com/components/radial-progress/)** — circular determinate progress. `radial-progress`; set `--value`, `--size`, and `--thickness`.
7. **[Skeleton](https://daisyui.com/components/skeleton/)** — loading placeholders. `skeleton`, `skeleton-text`; set dimensions with Tailwind utilities.
8. **[Divider](https://daisyui.com/components/divider/)** — labeled or unlabeled separation. `divider`; semantic colors, horizontal/vertical direction, and start/end placement.

## Forms and selection

9. **[Text Input](https://daisyui.com/components/input/)** — single-line input. `input`; ghost, semantic state/color, and `input-xs` through `input-xl`.
10. **[Textarea](https://daisyui.com/components/textarea/)** — multiline input. `textarea`; ghost, semantic state/color, and size modifiers.
11. **[Select](https://daisyui.com/components/select/)** — choose from options. `select`; ghost, semantic state/color, and size modifiers.
12. **[Checkbox](https://daisyui.com/components/checkbox/)** — independent boolean selection. `checkbox`; semantic colors and size modifiers.
13. **[Radio](https://daisyui.com/components/radio/)** — one choice from a named set. `radio`; semantic colors and size modifiers.
14. **[Toggle](https://daisyui.com/components/toggle/)** — boolean switch. Use a real `<input type="checkbox" class="toggle">`; semantic colors and sizes.
15. **[Range](https://daisyui.com/components/range/)** — numeric slider. `range`; semantic colors, sizes, and `range-vertical`.
16. **[Rating](https://daisyui.com/components/rating/)** — radio-based rating. `rating`; `rating-half`, `rating-hidden`, and size modifiers; pair with mask classes.
17. **[File Input](https://daisyui.com/components/file-input/)** — file chooser. `file-input`; ghost, semantic state/color, and size modifiers.
18. **[Fieldset](https://daisyui.com/components/fieldset/)** — related form controls. `fieldset`, `fieldset-legend`, and `label`; use native `<fieldset>`/`<legend>`.
19. **[Label](https://daisyui.com/components/label/)** — control labels. `label` and `floating-label`; preserve `for`/`id` relationships.
20. **[Validator](https://daisyui.com/components/validator/)** — native-validity feedback. Add `validator` to an input/select/textarea and follow with `validator-hint`.
21. **[Filter](https://daisyui.com/components/filter/)** — radio-button filtering with reset. `filter`, `filter-reset`; give each set a unique radio `name`.
22. **[OTP](https://daisyui.com/components/otp/)** — one-time code entry. `otp`; `otp-joined`, semantic colors, and `otp-xs` through `otp-xl`.

## Navigation and grouped controls

23. **[Breadcrumbs](https://daisyui.com/components/breadcrumbs/)** — hierarchical location. `breadcrumbs`; use a list of links.
24. **[Link](https://daisyui.com/components/link/)** — styled anchor. `link`; `link-hover` and semantic colors.
25. **[Menu](https://daisyui.com/components/menu/)** — vertical/horizontal actions or links. `menu`; `menu-title`, dropdown parts, active/disabled/focus states, sizes, and direction.
26. **[Navbar](https://daisyui.com/components/navbar/)** — top navigation. `navbar` with `navbar-start`, `navbar-center`, `navbar-end`.
27. **[Dock](https://daisyui.com/components/dock/)** — fixed bottom navigation. `dock`, `dock-label`, `dock-active`, and sizes.
28. **[Drawer](https://daisyui.com/components/drawer/)** — page layout with collapsible sidebar. `drawer`, `drawer-toggle`, `drawer-content`, `drawer-side`, `drawer-overlay`; `drawer-end|open`.
29. **[Dropdown](https://daisyui.com/components/dropdown/)** — anchored menu/content. `dropdown`, `dropdown-content`; placement plus hover/open/close modifiers. Prefer current accessible docs patterns.
30. **[Megamenu](https://daisyui.com/components/megamenu/)** — large desktop navigation popover. `megamenu`, `megamenu-active`; wide/full, vertical, and sizes. Provide drawer/dropdown navigation on small screens.
31. **[Tabs](https://daisyui.com/components/tab/)** — tabbed navigation/content. `tabs`, `tab`, `tab-content`; `tabs-box|border|lift`, sizes, `tab-active|disabled`, top/bottom placement.
32. **[Pagination](https://daisyui.com/components/pagination/)** — page navigation. Compose `join` and `join-item` buttons.
33. **[Steps](https://daisyui.com/components/steps/)** — process stages. `steps`, `step`, `step-icon`; semantic active colors and horizontal/vertical direction.
34. **[Join](https://daisyui.com/components/join/)** — visually group controls. `join` with `join-item`; horizontal or vertical.
35. **[FAB / Speed Dial](https://daisyui.com/components/fab/)** — corner action and related actions. `fab`, `fab-close`, `fab-main-action`, optional `fab-flower`; use accessible buttons/tooltips.

## Content and data display

36. **[Accordion](https://daisyui.com/components/accordion/)** — one expandable section open at a time. Radio-controlled `collapse`, `collapse-title`, `collapse-content`; arrow/plus modifiers.
37. **[Collapse](https://daisyui.com/components/collapse/)** — independently expandable content. `collapse`, title/content parts; arrow/plus/open/close modifiers.
38. **[Card](https://daisyui.com/components/card/)** — grouped readable content. `card`, `card-title`, `card-body`, `card-actions`; border/dash, side/image-full, and sizes.
39. **[Stat](https://daisyui.com/components/stat/)** — metrics. `stats` containing `stat`, `stat-title`, `stat-value`, `stat-desc`, figure/actions; horizontal/vertical.
40. **[Table](https://daisyui.com/components/table/)** — tabular data. `table`; zebra, pinned rows/columns, and sizes. Wrap with `overflow-x-auto`.
41. **[List](https://daisyui.com/components/list/)** — information rows. `list`, `list-row`; `list-col-grow`, `list-col-wrap`.
42. **[Timeline](https://daisyui.com/components/timeline/)** — chronological events. `timeline` with start/middle/end parts; snap, box, compact, and direction modifiers.
43. **[Chat bubble](https://daisyui.com/components/chat/)** — conversation lines. `chat`, image/header/footer/bubble parts; `chat-start|end` and semantic bubble colors.
44. **[Avatar](https://daisyui.com/components/avatar/)** — person/business thumbnail. `avatar`, `avatar-group`; online/offline/placeholder.
45. **[Indicator](https://daisyui.com/components/indicator/)** — place status/content at a corner or edge. `indicator`, `indicator-item`; start/center/end and top/middle/bottom placement.
46. **[Countdown](https://daisyui.com/components/countdown/)** — animated number changes from 0–999. `countdown`; update `--value`.
47. **[Kbd](https://daisyui.com/components/kbd/)** — keyboard shortcuts. `kbd` and size modifiers.
48. **[Mask](https://daisyui.com/components/mask/)** — crop content into common shapes. `mask` with circle, squircle, heart, polygon, star, triangle, and half modifiers.
49. **[Carousel](https://daisyui.com/components/carousel/)** — scrollable content/images. `carousel`, `carousel-item`; start/center/end and horizontal/vertical.
50. **[Diff](https://daisyui.com/components/diff/)** — before/after comparison. `diff`, `diff-item-1`, `diff-item-2`, `diff-resizer`.
51. **[Calendar](https://daisyui.com/components/calendar/)** — styles supported calendar libraries. `cally` for Cally, `react-day-picker` for DayPicker, or `vc` for Vanilla Calendar Pro; behavior comes from that library.

## Layout, effects, and presentation

52. **[Hero](https://daisyui.com/components/hero/)** — prominent title/description region. `hero`, `hero-content`, `hero-overlay`.
53. **[Footer](https://daisyui.com/components/footer/)** — site footer. `footer`, `footer-title`; center, horizontal, and vertical modifiers.
54. **[Stack](https://daisyui.com/components/stack/)** — visually layer items. `stack`; top/bottom/start/end modifiers and explicit dimensions.
55. **[Aura](https://daisyui.com/components/aura/)** — highlighted border-light effect. `aura`; dual/rainbow/holo/gold/silver/glow styles and sizes. Reserve for high-priority content.
56. **[Hover 3D Card](https://daisyui.com/components/hover-3d/)** — pointer-driven tilt wrapper. `hover-3d`; follow the required child structure from docs.
57. **[Hover Gallery](https://daisyui.com/components/hover-gallery/)** — hover-scrub image set, up to 10 images. `hover-gallery`.
58. **[Text Rotate](https://daisyui.com/components/text-rotate/)** — loop through 2–6 lines. `text-rotate`; nested wrapper is required, default loop 10 seconds.
59. **[Browser mockup](https://daisyui.com/components/mockup-browser/)** — browser frame. `mockup-browser`, `mockup-browser-toolbar`.
60. **[Code mockup](https://daisyui.com/components/mockup-code/)** — terminal/editor-style code block. `mockup-code`; use `pre` lines and optional data prefixes.
61. **[Phone mockup](https://daisyui.com/components/mockup-phone/)** — phone frame. `mockup-phone`, `mockup-phone-camera`, `mockup-phone-display`.
62. **[Window mockup](https://daisyui.com/components/mockup-window/)** — desktop window frame. `mockup-window`.

## Feedback, overlays, and state switching

63. **[Alert](https://daisyui.com/components/alert/)** — important feedback. `alert`; outline/dash/soft, info/success/warning/error, and vertical/horizontal.
64. **[Modal](https://daisyui.com/components/modal/)** — blocking dialog. `modal`, `modal-box`, `modal-action`, `modal-backdrop`; prefer native `<dialog>` over popover/legacy checkbox/hash methods.
65. **[Toast](https://daisyui.com/components/toast/)** — positioned stack of notices. `toast`; start/center/end and top/middle/bottom placement. Put alerts inside.
66. **[Tooltip](https://daisyui.com/components/tooltip/)** — hover/focus explanation. `tooltip`, optional `tooltip-content`; open, placement, and semantic color modifiers.
67. **[Swap](https://daisyui.com/components/swap/)** — toggle two visual states. `swap`, `swap-on`, `swap-off`, `swap-indeterminate`; active, rotate, and flip modifiers.
68. **[Theme Controller](https://daisyui.com/components/theme-controller/)** — switch theme from a checked radio/checkbox. `theme-controller`; input value must be a valid theme name.
