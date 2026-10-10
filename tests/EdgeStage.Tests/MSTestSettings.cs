// Unit tests are independent and run in parallel per class. Tests that touch the real desktop
// (foreground window, global hotkeys, DWM) are marked [DoNotParallelize].
[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]
