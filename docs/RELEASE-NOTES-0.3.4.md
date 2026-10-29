# SoopaDriveTool 0.3.4

- Select partitions directly in the disk map. Right-click for Move, Shrink, Expand, Format, Erase contents, Rename, and Delete. The list and selected map segment stay in sync.
- Replaced the large partition toolbar with compact Partition actions and Disk actions menus. Disabled actions explain why they are unavailable.
- Added partition-content erasure with zero, random, and three-pass overwrite options and full read-back verification. It keeps the partition-table entry and does not erase neighboring partitions.
- Apply now opens an inline Are you sure? review with Yes and No. No is selected by default. No and Escape preserve the queue. Only an unchanged, explicitly approved chain can start.
- Removed typed confirmation phrases from Windows tools and recovery. Direct destructive dialogs also default to No.
- Operation summaries use readable action names.

The installers are unsigned. See [test results](VALIDATION-0.3.4.md) for checked behavior and remaining hardware limits.
