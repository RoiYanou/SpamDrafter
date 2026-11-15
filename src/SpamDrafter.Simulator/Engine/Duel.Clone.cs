namespace SpamDrafter.Simulator.Engine;

// Copie directe d'une partie (phase 7, lot B) : bien plus rapide que le rejeu (Copy), pour que le Grand Joueur
// puisse essayer beaucoup d'actions. Chaque morceau de l'état est recopié ; les tests comparent Clone et Copy
// (même empreinte StateKey, même suite de partie) pour vérifier que rien n'est oublié (KI-033).
public sealed partial class Duel
{
    private Duel(Duel o)
    {
        _kb = o._kb;
        _s = o._s;
        _rng = o._rng.Clone();
        _seed = o._seed;
        _decks = o._decks;
        _steps.AddRange(o._steps);
        _choices.AddRange(o._choices);
        Log.Enabled = false;

        // Cartes : d'abord les copies, puis les liens entre cartes.
        var map = new Dictionary<CardState, CardState>(ReferenceEqualityComparer.Instance);
        Players = new PlayerState[2];
        for (var i = 0; i < 2; i++)
        {
            var p = o.Players[i].CloneEmpty();
            foreach (var c in o.Players[i].Board) p.Board.Add(map[c] = new CardState(c));
            foreach (var c in o.Players[i].Discard) p.Discard.Add(map[c] = new CardState(c));
            Players[i] = p;
        }
        CardState? Map(CardState? c) => c is null ? null : map.TryGetValue(c, out var m) ? m : c;
        foreach (var c in map.Values) c.FixLinks(Map);

        Round = o.Round;
        CurrentPlayer = o.CurrentPlayer;
        FirstPlayer = o.FirstPlayer;
        Phase = o.Phase;
        Winner = o.Winner;

        _lastTarget = Map(o._lastTarget);
        _lastPlayed = Map(o._lastPlayed);
        _vengeurKiller = Map(o._vengeurKiller);
        _castAbility = o._castAbility;
        _insertNext = o._insertNext;
        _lastTeamEffect = o._lastTeamEffect;
        _revancheDepth = o._revancheDepth;
        _sub = o._sub;
        _settlingDeaths = o._settlingDeaths;
        foreach (var (c, t) in o._chosenTalents) _chosenTalents[Map(c)!] = t;

        ShopItems.AddRange(o.ShopItems);
        foreach (var b in o.Banned) Banned.Add(b);
        Array.Copy(o._addedRerollCost, _addedRerollCost, 2);
        Array.Copy(o._banDone, _banDone, 2);
        Array.Copy(o._boughtThisWindow, _boughtThisWindow, 2);
        foreach (var e in o.AppliedEffectClasses) AppliedEffectClasses.Add(e);
    }

    /// <summary>
    /// Copie directe et rapide de la partie (sans journal ni décideurs), à faire entre deux actions.
    /// Avec <paramref name="reseed"/>, la suite de la copie utilise un autre hasard que la partie réelle.
    /// </summary>
    public Duel Clone(ulong? reseed = null)
    {
        if ((_queue.Count > 0 && Phase != DuelPhase.Ended) || _castCaster is not null || _slotTarget is not null || _replay is not null)
            throw new InvalidOperationException($"Copie impossible pendant la résolution d'un lancer (file {_queue.Count}, lanceur {_castCaster}, emplacement {_slotTarget}, rejeu {_replay is not null}, phase {Phase}).");
        var copy = new Duel(this);
        if (reseed is { } s) copy.Reseed(s);
        return copy;
    }
}
