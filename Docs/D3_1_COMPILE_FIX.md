# 0.12.4D3.1-alpha compile fix

Fixes CS0841/CS0136 in BassRestorationProcessor.SoftLimit introduced in D3.
The method parameter `x` was accidentally shadowed by a local `float x`.
Renamed the local saturation variable to `drive` and its square to `drive2`.
No DSP behavior or coefficients changed.
