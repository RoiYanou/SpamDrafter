namespace SpamDrafter.Simulator;

/// <summary>
/// Calculs lourds en parallèle (duels simulés) sans figer l'interface : un cœur reste libre pour l'application et les threads de calcul
/// passent en priorité « sous la normale », pour que l'affichage garde la main (demande de l'utilisateur, 17/11/2026).
/// </summary>
public static class ComputeLimits
{
    /// <summary>Un quart des cœurs logiques (au moins un) reste libre : l'interface, le son et le système gardent de quoi respirer.</summary>
    public static int Workers => Math.Max(1, Environment.ProcessorCount - Math.Max(1, Environment.ProcessorCount / 4));

    public static void For(int count, Action<int> body, CancellationToken cancel = default)
    {
        // Les duels créent beaucoup d'objets : en mode « faible latence », le ramasse-miettes évite les grosses pauses qui figeaient l'affichage.
        var mode = System.Runtime.GCSettings.LatencyMode;
        try
        {
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Workers, CancellationToken = cancel },
                () => { var t = Thread.CurrentThread; var old = t.Priority; t.Priority = ThreadPriority.BelowNormal; return old; },
                (i, _, old) => { body(i); return old; },
                old => Thread.CurrentThread.Priority = old);
        }
        finally { System.Runtime.GCSettings.LatencyMode = mode; }
    }
}
