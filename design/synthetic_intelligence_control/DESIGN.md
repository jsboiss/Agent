---
name: Synthetic Intelligence Control
colors:
  surface: '#111317'
  surface-dim: '#111317'
  surface-bright: '#37393e'
  surface-container-lowest: '#0c0e12'
  surface-container-low: '#1a1c20'
  surface-container: '#1e2024'
  surface-container-high: '#282a2e'
  surface-container-highest: '#333539'
  on-surface: '#e2e2e8'
  on-surface-variant: '#c0c7d5'
  inverse-surface: '#e2e2e8'
  inverse-on-surface: '#2f3035'
  outline: '#8a919e'
  outline-variant: '#404753'
  surface-tint: '#a6c8ff'
  primary: '#a6c8ff'
  on-primary: '#00315f'
  primary-container: '#3192fc'
  on-primary-container: '#002a53'
  inverse-primary: '#005fb0'
  secondary: '#51df8e'
  on-secondary: '#00391d'
  secondary-container: '#00b266'
  on-secondary-container: '#003c1e'
  tertiary: '#cabeff'
  on-tertiary: '#320099'
  tertiary-container: '#957dff'
  on-tertiary-container: '#2b0087'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#d5e3ff'
  primary-fixed-dim: '#a6c8ff'
  on-primary-fixed: '#001c3b'
  on-primary-fixed-variant: '#004786'
  secondary-fixed: '#70fda7'
  secondary-fixed-dim: '#51df8e'
  on-secondary-fixed: '#00210e'
  on-secondary-fixed-variant: '#00522c'
  tertiary-fixed: '#e6deff'
  tertiary-fixed-dim: '#cabeff'
  on-tertiary-fixed: '#1d0061'
  on-tertiary-fixed-variant: '#491ac6'
  background: '#111317'
  on-background: '#e2e2e8'
  surface-variant: '#333539'
typography:
  h1:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 24px
    letterSpacing: -0.01em
  h2:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '600'
    lineHeight: 20px
    letterSpacing: '0'
  body-sm:
    fontFamily: Inter
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 18px
  mono-code:
    fontFamily: Space Grotesk
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
  label-caps:
    fontFamily: Inter
    fontSize: 11px
    fontWeight: '700'
    lineHeight: 12px
    letterSpacing: 0.05em
spacing:
  unit: 4px
  container-padding: 12px
  component-gap: 8px
  tight-stack: 4px
  panel-margin: 1px
---

## Brand & Style

The design system is engineered for professional environments where high information density and cognitive clarity are paramount. It targets senior developers and AI engineers managing complex agentic workflows. The brand personality is clinical, precise, and utilitarian, evoking the feel of a mission-critical terminal rather than a consumer application.

The design style is a hybrid of **Modern Minimalism** and **Industrial Brutalism**. It eschews decorative elements like shadows or gradients in favor of structural integrity. Depth is communicated through tonal shifts in background fills and crisp, single-pixel borders. The interface prioritizes data readability above all, using a compact layout rhythm to ensure the operator can monitor multiple data streams simultaneously without excessive scrolling.

## Colors

The color palette is built on a foundation of "Deep Charcoal" and "Slate Gray." The background hierarchy uses decreasing luminosity to signify nesting: the primary workspace uses the darkest hex, while panels and popovers use progressively lighter slates.

Accents are applied with surgical precision:
- **Electric Blue (#2E90FA):** Used for primary actions, active selection states, and logic flow indicators.
- **Emerald Green (#12B76A):** Reserved for successful agent execution, healthy status nodes, and memory commits.
- **Violet (#7A5AF8):** Dedicated to neural weights, token utilization, and high-level abstract reasoning processes.
- **Warning/Error:** Use a desaturated amber and a crisp crimson only when manual intervention is required.

Borders utilize slate grays to define the UI structure, replacing shadows entirely to maintain a flat, professional aesthetic.

## Typography

This design system utilizes **Inter** for all UI controls and body text to ensure maximum legibility at small sizes. **Space Grotesk** is employed as the functional monospace companion for agent IDs, JSON payloads, and code execution blocks.

The type scale is intentionally tight. The difference between a heading and body text is signaled through font weight and tracking rather than drastic size increases. All labels for data points (e.g., "Latency", "Tokens") should use the `label-caps` style in a muted slate color to differentiate metadata from actual values.

## Layout & Spacing

The layout follows a **Fixed Grid** philosophy built on a strict 4px base unit. This design system emphasizes high density; margins are kept to a minimum to maximize the "above the fold" data visibility. 

Panels are separated by 1px borders rather than wide gutters, creating a tiled appearance reminiscent of a sophisticated IDE. Padding within components like cards or table cells is restricted to 8px or 12px. Content should be aligned to the top-left of containers, following a logical scanning pattern for technical documentation.

## Elevation & Depth

In this design system, depth is purely structural. We use **Tonal Layering** combined with **Low-Contrast Outlines**.

1.  **Level 0 (Base):** The darkest neutral (`#0C0E12`). Used for the main application background.
2.  **Level 1 (Panels):** A slightly lighter slate (`#161B22`). Used for sidebar panels, terminal areas, and main content cards.
3.  **Level 2 (Interactions):** Used for hovered states or active tooltips. 

Borders are the primary separators. Every container must have a 1px solid border (`#30363D`). No shadows are permitted, as they suggest a physical layering that contradicts the utilitarian digital nature of the tool.

## Shapes

The design system employs **Sharp (0)** roundedness. All buttons, input fields, cards, and panels feature 90-degree corners. This reinforces the "engineered" feel and allows for seamless tiling of UI elements without gaps. 

Rare exceptions: Circular indicators are permitted only for status "pills" or user avatars to distinguish biological/system status from structural UI elements.

## Components

- **Buttons:** Rectangular with 1px borders. Primary buttons use a solid Electric Blue fill with white text. Secondary buttons use a ghost style with Slate Gray borders and no fill.
- **Input Fields:** Darker than their parent container with a 1px bottom border that turns Electric Blue on focus. Use monospaced font for technical inputs.
- **Data Chips:** Compact, using the accent colors (Emerald, Violet) with 10% opacity fills and 100% opacity borders for memory segments.
- **The "Node" Card:** A specialized component for AI logic steps. It features a header with `label-caps` typography and a monospaced "ID" in the top right. 
- **Terminal View:** A dedicated area using `mono-code` for real-time agent logs. Syntax highlighting should follow the primary/secondary/tertiary accent palette.
- **Status Indicators:** Small 8x8px squares (not circles) that pulse subtly when an agent is "thinking" or processing a request.