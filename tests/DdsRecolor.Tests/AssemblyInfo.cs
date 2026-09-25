// Tests in this assembly are independent:
//   * in-memory fixture builders allocate their own buffers,
//   * committed fixtures are read-only,
//   * every test constructs its own DdsFile and HueShift,
//   * nothing mutates shared static state.
//
// Parallel collection execution is therefore safe, and keeps the whole suite
// well under a second. xUnit runs test *classes* in parallel by default, so this
// simply makes the intent explicit rather than relying on the default.
[assembly: CollectionBehavior(DisableTestParallelization = false, MaxParallelThreads = 0)]
