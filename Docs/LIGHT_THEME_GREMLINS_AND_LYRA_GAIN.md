# 0.11.9-alpha — Light theme gremlins + Lyra gain parity

## Shuffle
- Added `icon_shuffle_light.svg` with a dark Light-theme stroke.
- Shuffle background/border now come from `ThemePalette` instead of Dark hardcoded colors.
- Theme changes refresh shuffle state immediately.

## Settings navigation strip
- Removed hardcoded Dark `NavigationPage` bar colors.
- The settings modal now takes `SurfaceBackground` and `TextPrimary` from the active palette.

## Lyra MIDI volume
Classic/Legacy already renders channel/voice gain without the old global `0.18` attenuation and controls peaks with its rational soft limiter. Lyra still had both an explicit `0.18` voice attenuation and the more aggressive `x/(1+abs(x))` output curve.

Lyra now follows the Legacy gain staging:
- removed the `0.18` attenuation from both Lyra voice render paths;
- changed Lyra output soft clipping to the same rational tanh approximation used by Classic/Legacy.

This preserves velocity, MIDI CC volume/expression and per-channel user gain while restoring useful output level.
