# DSN0.2.3 — Diverse Factory Bank

The original bank used sixteen family templates plus small numeric variations.
This checkpoint gives all 128 GM programs an explicit DSN-like patch recipe.

Goals:
- preserve GM program semantics while sounding like DSN, not a SoundFont;
- make program changes audibly meaningful;
- expose dual-VCO, pulse width, filter-envelope motion, LFO/PWM, FM, hard sync and drive;
- keep acoustic families comparatively restrained and let synth/FX families expose the architecture;
- use official GM program names in the editor instead of generic family labels.

No user bank format change. Existing user patches and GM assignments remain valid.
Channel 10 remains handled by the dedicated DSN drumkit.
