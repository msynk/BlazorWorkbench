# ADR 0005: Built-in PNG codec and perceptual comparison

- **Status**: Accepted
- **Date**: 2026-11-02

## Context
Playwright for .NET captures screenshots but, unlike the Node test runner, does not compare them. The imaging libraries either bring native binaries (SkiaSharp) or licensing constraints (the split license of ImageSharp).

## Decision
Implement a minimal PNG decoder and an RGBA encoder on top of `ZLibStream` from the BCL. The decoder supports non-interlaced 8-bit images (gray, RGB, palette, gray with alpha, RGBA) and every filter type. Compare in YIQ color space with a per-pixel `threshold` and a `maxDiffPixelRatio` tolerance, like pixelmatch.

## Consequences
- No extra dependencies. Unit tests cover the codec and the diff, including every filter type.
- Interlaced and 16-bit PNGs are not supported; browsers do not produce them for screenshots.
- There is no anti-aliasing detection yet (it is on the backlog), so the default threshold is 0.1.
