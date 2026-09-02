# MiniBrowser Performance Baseline

Measured on 2026-09-01 using the x64 portable Release build after a 10-second
warmup. `scripts/Measure-PortableResources.ps1` walks the complete process tree,
including every WebView2 renderer, GPU, network, and utility process.

| Restored tabs | Initialized tabs | Total working set | Total private memory |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 536.5 MB | 264.9 MB |
| 5 | 1 | 535.9 MB | 263.5 MB |

The five-tab startup restores only the active WebView2. Inactive tabs remain
model-only until selected, so restoring a larger session has no meaningful
startup memory increase.

Working-set totals double-count Chromium shared pages across processes. Private
memory is the primary comparison metric. MiniBrowser intentionally keeps the
native WebView2 user agent and avoids aggressive renderer flags because those
flags previously harmed authentication and verification compatibility.

Run a repeatable measurement with:

```powershell
.\scripts\Measure-PortableResources.ps1 -WarmupSeconds 10 -TabCount 1
.\scripts\Measure-PortableResources.ps1 -WarmupSeconds 10 -TabCount 5
```

Future performance changes must report both values, use the same Release build
shape, and include the full descendant process tree.
