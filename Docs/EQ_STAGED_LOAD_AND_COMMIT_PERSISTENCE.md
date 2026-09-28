# EQ staged load + commit persistence — 0.11.5-alpha

- EqualizerPage InitializeComponent now builds only the modal shell/header/loading state.
- Heavy EQ controls are created after OnAppearing in three stages: core EQ, Bass Restoration, custom presets, yielding between stages so Android can present frames instead of blocking the complete modal open.
- Live slider movement still updates DSP immediately.
- Disk persistence is no longer executed on every ValueChanged / tiny dB movement.
- EQ curve persists once on band drag completion.
- Preamp persists on Slider.DragCompleted.
- Bass sliders persist on DragCompleted; switch/reset commit immediately.
- Preset/reset operations commit immediately because they are discrete actions.
