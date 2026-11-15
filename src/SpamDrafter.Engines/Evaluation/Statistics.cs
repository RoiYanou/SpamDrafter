namespace SpamDrafter.Engines.Evaluation;

/// <summary>Petits outils de statistiques pour les mesures des moteurs.</summary>
public static class Statistics
{
    /// <summary>
    /// Corrélation de rang de Spearman (−1 à 1) : 1 = les deux listes classent les éléments dans le même ordre,
    /// 0 = aucun lien. Les ex æquo reçoivent leur rang moyen.
    /// </summary>
    public static double Spearman(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count != b.Count || a.Count < 2) throw new ArgumentException("Deux listes de même taille (au moins 2) sont nécessaires.");
        return Pearson(Ranks(a), Ranks(b));
    }

    public static double Pearson(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        double ma = a.Average(), mb = b.Average(), num = 0, da = 0, db = 0;
        for (var i = 0; i < a.Count; i++)
        {
            num += (a[i] - ma) * (b[i] - mb);
            da += (a[i] - ma) * (a[i] - ma);
            db += (b[i] - mb) * (b[i] - mb);
        }
        return da == 0 || db == 0 ? 0 : num / Math.Sqrt(da * db);
    }

    public static double[] Ranks(IReadOnlyList<double> v)
    {
        var order = Enumerable.Range(0, v.Count).OrderBy(i => v[i]).ToArray();
        var ranks = new double[v.Count];
        for (var i = 0; i < order.Length;)
        {
            var j = i;
            while (j + 1 < order.Length && v[order[j + 1]] == v[order[i]]) j++;
            for (var k = i; k <= j; k++) ranks[order[k]] = (i + j) / 2.0 + 1;
            i = j + 1;
        }
        return ranks;
    }

    /// <summary>Écart type (population).</summary>
    public static double StdDev(IReadOnlyList<double> v)
    {
        var m = v.Average();
        return Math.Sqrt(v.Average(x => (x - m) * (x - m)));
    }
}
