---
name: Obsidian Flux
colors:
  surface: '#12131a'
  surface-dim: '#12131a'
  surface-bright: '#383941'
  surface-container-lowest: '#0d0e15'
  surface-container-low: '#1a1b22'
  surface-container: '#1e1f27'
  surface-container-high: '#292931'
  surface-container-highest: '#34343c'
  on-surface: '#e3e1ec'
  on-surface-variant: '#c7c4d7'
  inverse-surface: '#e3e1ec'
  inverse-on-surface: '#2f3038'
  outline: '#908fa0'
  outline-variant: '#464554'
  surface-tint: '#c0c1ff'
  primary: '#c0c1ff'
  on-primary: '#1000a9'
  primary-container: '#8083ff'
  on-primary-container: '#0d0096'
  inverse-primary: '#494bd6'
  secondary: '#ffb2b7'
  on-secondary: '#67001b'
  secondary-container: '#b50036'
  on-secondary-container: '#ffc2c4'
  tertiary: '#d0bcff'
  on-tertiary: '#3c0091'
  tertiary-container: '#a078ff'
  on-tertiary-container: '#340080'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#e1e0ff'
  primary-fixed-dim: '#c0c1ff'
  on-primary-fixed: '#07006c'
  on-primary-fixed-variant: '#2f2ebe'
  secondary-fixed: '#ffdadb'
  secondary-fixed-dim: '#ffb2b7'
  on-secondary-fixed: '#40000d'
  on-secondary-fixed-variant: '#92002a'
  tertiary-fixed: '#e9ddff'
  tertiary-fixed-dim: '#d0bcff'
  on-tertiary-fixed: '#23005c'
  on-tertiary-fixed-variant: '#5516be'
  background: '#12131a'
  on-background: '#e3e1ec'
  surface-variant: '#34343c'
typography:
  display-lg:
    fontFamily: Geist
    fontSize: 48px
    fontWeight: '700'
    lineHeight: 56px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: Geist
    fontSize: 32px
    fontWeight: '600'
    lineHeight: 40px
    letterSpacing: -0.01em
  headline-md:
    fontFamily: Geist
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  label-md:
    fontFamily: Geist
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
    letterSpacing: 0.05em
  label-sm:
    fontFamily: Geist
    fontSize: 11px
    fontWeight: '500'
    lineHeight: 14px
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  unit: 4px
  container-margin: 24px
  panel-gap: 16px
  control-stack: 8px
  sidebar-width: 260px
  toolbar-height: 48px
---

## Brand & Style

The design system is engineered for high-performance desktop productivity, specifically tailored for screen recording and content creation. It adopts a **Soft Dark** aesthetic—a refined evolution of dark mode that prioritizes visual comfort and depth over pure black-and-white contrast.

The style is a hybrid of **Minimalism** and **Glassmorphism**, emphasizing structural clarity and atmospheric depth. The interface should feel "expensive" and precision-engineered, evoking the same sense of tool-quality found in high-end developer utilities or creative suites. 

Key visual pillars include:
- **Atmospheric Depth:** Using translucent layers and background blurs to create a sense of physical space.
- **Precision Detailing:** 1px "micro-borders" and subtle inner glows that define edges without adding bulk.
- **Non-Intrusive Presence:** The UI stays out of the user's way during recording, utilizing muted secondary tones to reduce eye fatigue.

## Colors

The palette is built on a foundation of deep, cool slates to prevent the "starkness" of pure black. 

- **Foundation:** The base (`#0D0E15`) provides a grounded environment. Surface panels (`#1A1A1A`) or semi-transparent whites (`rgba(255, 255, 255, 0.05)`) are used to create hierarchical lift.
- **Accents:** The Primary Indigo (`#6366F1`) signifies action and state. The secondary accent is an "Active Record" gradient, transitioning from a sophisticated Red (`#F43F5E`) to a vibrant Orange (`#FB923C`), used sparingly for high-alert recording states.
- **Typography:** Contrast is strictly managed. Use `#F3F4F6` for primary headlines and `#9CA3AF` for labels and secondary metadata to maintain a clean, balanced information hierarchy.

## Typography

This design system utilizes a dual-sans serif approach. **Geist** is employed for headlines and labels to provide a technical, monospaced-adjacent precision, while **Inter** is used for body copy to ensure maximum legibility at smaller sizes.

- **Scaling:** Headlines use tight letter-spacing to appear more cohesive on large desktop displays. 
- **Hierarchy:** Use weight over color to denote importance. Use the `label-md` role for metadata and non-interactive status text, always in the muted silver color palette.
- **Functional Type:** For timecodes or file sizes, use Geist's tabular figures to prevent layout "jitter" during active recording.

## Layout & Spacing

The layout is built on a **4px baseline grid**, ensuring all components align with mathematical rigor. 

- **The Floating Model:** Instead of edge-to-edge containers, this design system favors "Floating Panels." These panels should have a minimum margin of 24px from the screen edge.
- **Dynamic Adaptability:** While primarily a desktop experience, the layout should handle narrow "Recording Bar" states and expansive "Library" states. 
- **Internal Spacing:** Use 16px (`4 units`) for standard component separation and 8px (`2 units`) for grouped controls (e.g., Play/Pause/Stop).

## Elevation & Depth

Hierarchy is established through **Z-axis layering** rather than heavy shadows.

- **Level 0 (Base):** The core canvas background.
- **Level 1 (Panels):** `bg-white/5` with a `backdrop-filter: blur(12px)`. This creates the frosted glass effect.
- **Edges:** Every elevated panel must have a `1px` solid border using `rgba(255, 255, 255, 0.1)`. This catches the "light" and defines the shape against the dark background.
- **Shadows:** Use large, ultra-soft ambient shadows for floating menus (e.g., `box-shadow: 0 20px 40px rgba(0,0,0,0.4)`).
- **Glows:** For active states (like a recording indicator), apply a soft outer bloom using the primary indigo or recording red with a blur radius of 15px and 0.3 opacity.

## Shapes

The shape language is consistently **Rounded**, striking a balance between approachable software and professional hardware.

- **Panels & Cards:** Use `rounded-lg` (16px) for main application windows and secondary panels.
- **Buttons & Controls:** Small controls use `rounded-md` (8px), while primary action buttons and the main toolbar follow a **Pill-shaped** (full radius) convention to stand out as interactive objects.
- **Icons:** Use thin-stroke (1.5pt) wireframe icons with slight corner rounding to match the typography.

## Components

- **Buttons:** 
  - *Primary:* Solid Indigo background with white text.
  - *Secondary:* Ghost style with the 1px translucent border and a subtle hover fill (`white/10`).
  - *Recording:* Uses the Red-to-Orange gradient, featuring a pulsating "Inner Glow" animation.
- **Input Fields:** Minimalist design with only a bottom border or a very subtle `white/5` fill. Focus states are indicated by the 1px border brightening to the primary indigo.
- **Pill Controls:** Used for mode switching (e.g., "Screen" vs "Window"). The active state should physically "slide" a background highlight between options.
- **Floating Toolbar:** A narrow, pill-shaped bar that sits at the bottom of the screen. It should use maximum glassmorphism (`blur: 20px`) to remain legible over any desktop wallpaper.
- **Chips/Badges:** Used for status (e.g., "4K", "60 FPS"). These should be compact, using `label-sm` typography and a dark semi-transparent fill.
- **Cards:** For video thumbnails in the library, use a 16px corner radius and an inner 1px border to ensure the thumbnail doesn't bleed into the dark UI.