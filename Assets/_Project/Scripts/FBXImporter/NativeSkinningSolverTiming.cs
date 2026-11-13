namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 표면 솔버 커널 구간별 누적 계측 — 워커 병렬 호출에서 Interlocked로만 쓰는 진단 전용 싱크.
    /// </summary>
    internal sealed class NativeSkinningSolverTiming
    {
        internal long PairEnergyTicks;
        internal long BuildSystemTicks;
        internal long CgSolveTicks;
        internal long ProjectAcceptTicks;
        internal long SetupTicks;
        internal long LgIterations;
        internal long CgCalls;
        internal long CgIterations;

        internal static long Stamp() =>
            System.Diagnostics.Stopwatch.GetTimestamp();

        internal void AddPairEnergy(long since) =>
            System.Threading.Interlocked.Add(
                ref PairEnergyTicks, Stamp() - since);

        internal void AddBuildSystem(long since) =>
            System.Threading.Interlocked.Add(
                ref BuildSystemTicks, Stamp() - since);

        internal void AddCgSolve(long since) =>
            System.Threading.Interlocked.Add(
                ref CgSolveTicks, Stamp() - since);

        internal void AddProjectAccept(long since) =>
            System.Threading.Interlocked.Add(
                ref ProjectAcceptTicks, Stamp() - since);

        internal void AddSetup(long since) =>
            System.Threading.Interlocked.Add(
                ref SetupTicks, Stamp() - since);

        internal void AddLgIteration() =>
            System.Threading.Interlocked.Increment(ref LgIterations);

        internal void AddCgCall() =>
            System.Threading.Interlocked.Increment(ref CgCalls);

        internal void AddCgIteration() =>
            System.Threading.Interlocked.Increment(ref CgIterations);

        internal void AddCgIterations(int iterations) =>
            System.Threading.Interlocked.Add(ref CgIterations, iterations);

        internal string Describe()
        {
            double frequency = System.Diagnostics.Stopwatch.Frequency;
            return $"pairEnergy={PairEnergyTicks * 1000.0 / frequency:F0}ms " +
                $"buildSystem={BuildSystemTicks * 1000.0 / frequency:F0}ms " +
                $"cgSolve={CgSolveTicks * 1000.0 / frequency:F0}ms " +
                $"projectAccept={ProjectAcceptTicks * 1000.0 / frequency:F0}ms " +
                $"setup={SetupTicks * 1000.0 / frequency:F0}ms " +
                $"lgIter={LgIterations} cgCalls={CgCalls} " +
                $"cgIter={CgIterations}";
        }
    }
}
