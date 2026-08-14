# Runtime topology decision

The native Runtime nupkg design was rejected. The reviewed closure is too large and overlaps ROCm assets already governed by AMD's package repository; splitting it would make this project own native version, dependency, license, RPATH, and cross-package upgrade policy.

M7 therefore selects managed-only distribution and requires users to install a coherent MIGraphX/ROCm family through AMD's official system repository. No component split, Runtime package ID, staging directory, promotion receipt, or RPATH rewrite remains planned.
