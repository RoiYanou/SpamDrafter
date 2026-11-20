using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Games;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Text;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 16, lot A : jour de Paris, tirage identique pour tous, énigme du jour, partie quotidienne enregistrée.</summary>
public class Phase16GamesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-games-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    /// <summary>Historique d'un jeu, enregistré dans un fichier du dossier de test (un fichier par jeu).</summary>
    private GameHistoryViewModel H(IReadOnlyList<CardItem> items, string game)
    {
        var file = Path.Combine(_dir, "history-" + game + ".json");
        return new GameHistoryViewModel(game, DailyHistory.Load(file), file, items);
    }

    private static string? KnowledgeDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return p;
        }
        return null;
    }

    [Fact]
    public void Le_jour_change_a_minuit_a_Paris()
    {
        // Hiver (UTC+1) : 23 h 30 UTC le 19 = 0 h 30 le 20 à Paris.
        Assert.Equal(new DateOnly(2026, 11, 20), DailyClock.DayOf(new DateTimeOffset(2026, 11, 19, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 19), DailyClock.DayOf(new DateTimeOffset(2026, 11, 19, 22, 30, 0, TimeSpan.Zero)));
        // Été (UTC+2) : 22 h 30 UTC le 19 juillet = 0 h 30 le 20.
        Assert.Equal(new DateOnly(2026, 7, 20), DailyClock.DayOf(new DateTimeOffset(2026, 7, 19, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 7, 19), DailyClock.DayOf(new DateTimeOffset(2026, 7, 19, 21, 30, 0, TimeSpan.Zero)));
        // Le fuseau du PC ne compte pas : même instant, même jour.
        Assert.Equal(DailyClock.DayOf(new DateTimeOffset(2026, 11, 20, 5, 0, 0, TimeSpan.FromHours(-8))),
                     DailyClock.DayOf(new DateTimeOffset(2026, 11, 20, 13, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Le_tirage_est_stable_et_ne_depend_pas_du_programme()
    {
        // Valeurs de référence : si elles changent, les joueurs n'auraient plus la même énigme le même jour.
        var rng = new SeededRandom(SeededRandom.SeedOf("riddle|0"));
        var first = Enumerable.Range(0, 3).Select(_ => rng.NextUInt64()).ToArray();
        var again = new SeededRandom(SeededRandom.SeedOf("riddle|0"));
        Assert.Equal(first, Enumerable.Range(0, 3).Select(_ => again.NextUInt64()).ToArray());
        Assert.Equal(0xE220A8397B1DCDAFUL, new SeededRandom(0).NextUInt64());   // valeur publiée de SplitMix64 pour la graine 0
        Assert.NotEqual(SeededRandom.SeedOf("riddle|0"), SeededRandom.SeedOf("riddle|1"));
    }

    [Fact]
    public void Les_enigmes_du_paquet_visent_toutes_une_carte_existante_sans_citer_son_nom_ni_ses_attaques()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var riddles = RiddleGame.LoadRiddles(dir);
        Assert.Equal(kb.Cards.Count, riddles.Count);
        Assert.Equal(riddles.Count, riddles.Select(r => r.CardName).Distinct().Count());
        foreach (var r in riddles)
        {
            var card = kb.Cards.FirstOrDefault(c => c.SanitizedName == r.CardName);
            Assert.True(card is not null, "carte inconnue : " + r.CardName);
            // Jamais le nom de la carte (ni un de ses mots de 4 lettres ou plus), jamais une de ses attaques (nom entier, ni un de ses mots de 6 lettres ou plus).
            var text = " " + TextSearch.Normalize(r.Text) + " ";
            var stop = new[] { "des", "les", "dune", "dans", "pour" };
            foreach (var w in TextSearch.Normalize(kb.CardName(card!)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4 && !stop.Contains(w)))
                Assert.False(text.Contains(" " + w), $"l'énigme de {r.CardName} cite le nom : {w}");
            foreach (var a in card!.Abilities.Select(kb.AbilityName).Select(TextSearch.Normalize))
            {
                Assert.False(a.Length > 0 && text.Contains(" " + a + " "), $"l'énigme de {r.CardName} cite l'attaque : {a}");
                foreach (var w in a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 6))
                    Assert.False(text.Contains(" " + w), $"l'énigme de {r.CardName} cite un mot d'attaque : {w}");
            }
        }
    }

    [Fact]
    public void Chaque_jour_a_une_enigme_la_meme_pour_tous_et_un_tour_complet_ne_repete_aucune_carte()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var riddles = RiddleGame.LoadRiddles(dir);
        var day = new DateOnly(2026, 11, 20);
        Assert.Equal(RiddleGame.ForDay(kb, riddles, day)!.Answer.Id, RiddleGame.ForDay(kb, riddles, day)!.Answer.Id);
        var first = DailyClock.Number(new DateOnly(2026, 1, 1));
        var seen = new HashSet<string>();
        for (var i = 0; i < riddles.Count; i++)
            Assert.True(seen.Add(RiddleGame.ForDay(kb, riddles, DailyClock.Epoch.AddDays(first + i))!.Answer.Id), "carte répétée dans un tour");
    }

    [Fact]
    public void Les_indices_sont_devoiles_un_par_erreur()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var game = RiddleGame.ForDay(kb, RiddleGame.LoadRiddles(dir), new DateOnly(2026, 11, 20))!;
        Assert.Empty(game.VisibleHints(0));
        Assert.Equal("Élément", game.VisibleHints(1).Single().Label);
        Assert.Equal(2, game.VisibleHints(2).Count);
        Assert.True(game.Hints().Count >= 4);
    }

    [Fact]
    public void La_partie_du_jour_est_enregistree_reprise_et_fermee_jusqu_au_lendemain()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var path = Path.Combine(_dir, "daily.json");
        var vm = new RiddleViewModel(kb, items, dir, DailyBook.Load(path), path, H(items, "riddle"));
        Assert.True(vm.Available);
        var answer = vm.AnswerItem!;
        var wrong = items.First(i => i.Card.Id != answer.Card.Id);
        vm.GuessCommand.Execute(wrong);
        Assert.Single(vm.Guesses);
        Assert.True(vm.IsPlaying);

        // On ferme et rouvre l'application : la partie reprend là où elle était.
        var again = new RiddleViewModel(kb, items, dir, DailyBook.Load(path), path, H(items, "riddle"));
        Assert.Single(again.Guesses);

        // La bonne réponse ferme le jeu du jour ; plus aucune réponse n'est acceptée.
        again.GuessCommand.Execute(again.AnswerItem);
        Assert.True(again.IsFinished && again.Won && !again.IsPlaying);
        again.GuessCommand.Execute(items.Last(i => i.Card.Id != answer.Card.Id && i.Card.Id != wrong.Card.Id));
        Assert.Equal(2, again.Guesses.Count);
        var reopened = new RiddleViewModel(kb, items, dir, DailyBook.Load(path), path, H(items, "riddle"));
        Assert.True(reopened.IsFinished && reopened.Won);

        // Un autre jour : nouvelle partie.
        var book = DailyBook.Load(path);
        Assert.False(book.For(RiddleGame.GameId, new DateOnly(2030, 1, 1)).Finished);
    }

    [Fact]
    public void L_enigme_n_a_pas_de_limite_d_essais_et_l_abandon_donne_la_reponse()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var path = Path.Combine(_dir, "daily.json");
        var vm = new RiddleViewModel(kb, items, dir, DailyBook.Load(path), path, H(items, "riddle"));
        Assert.True(vm.CanGiveUp && vm.ShowGiveUp);
        foreach (var w in items.Where(i => i.Card.Id != vm.AnswerItem!.Card.Id).Take(25)) vm.GuessCommand.Execute(w);   // bien plus que les 6 essais d'avant
        Assert.True(vm.IsPlaying && !vm.IsFinished);
        Assert.Equal(25, vm.Guesses.Count);
        Assert.Equal("Essai 26", vm.AttemptText);

        vm.GiveUpCommand.Execute(null);                                  // abandon : la réponse est donnée
        Assert.True(vm.IsFinished && !vm.Won && vm.GaveUp && !vm.ShowGiveUp);
        Assert.Equal("Tu as abandonné", vm.ResultTitle);
        Assert.Contains(vm.AnswerItem!.Name, vm.ResultText);
        vm.GuessCommand.Execute(vm.AnswerItem);                          // plus de réponse acceptée
        Assert.Equal(25, vm.Guesses.Count);
        var again = new RiddleViewModel(kb, items, dir, DailyBook.Load(path), path, H(items, "riddle"));
        Assert.True(again.IsFinished && again.GaveUp);

        // L'abandon est dans l'historique avec la date.
        var row = Assert.Single(vm.History.Rows);
        Assert.Equal(DailyClock.Key(vm.Day), row.Entry.Day);
        Assert.StartsWith("Abandon", row.Headline);
        Assert.Equal(25, row.Lines.Count);
        Assert.False(row.Entry.Good);
    }

    [Fact]
    public void Le_detail_n_a_ni_limite_d_essais_ni_dezoom_et_l_abandon_donne_la_reponse()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var path = Path.Combine(_dir, "daily.json");
        var detail = new DetailViewModel(kb, items, DailyBook.Load(path), path, H(items, "detail"));
        Assert.True(detail.CanGiveUp && detail.ShowGiveUp && detail.ShowAttempts);
        Assert.Equal("Essai 1", detail.AttemptText);
        var zone = detail.Window;
        foreach (var w in items.Where(i => i.Card.Id != detail.AnswerItem!.Card.Id).Take(20)) detail.GuessCommand.Execute(w);
        Assert.True(detail.IsPlaying);
        Assert.Equal(zone, detail.Window);                          // pas de dézoom, quel que soit le nombre d'erreurs
        detail.GiveUpCommand.Execute(null);
        Assert.True(detail.IsFinished && !detail.Won && detail.GaveUp);
        Assert.Equal(new DetailWindow(0, 0, 1, 1), detail.Window);  // l'image entière apparaît
        var row = Assert.Single(detail.History.Rows);
        Assert.StartsWith("Abandon", row.Headline);
        // Gagner : « Trouvé en n essais », avec la carte.
        var path2 = Path.Combine(_dir, "daily2.json");
        var win = new DetailViewModel(kb, items, DailyBook.Load(path2), path2, H(items, "detail2"));
        win.GuessCommand.Execute(win.AnswerItem);
        var r2 = Assert.Single(win.History.Rows);
        Assert.Equal("Trouvé en 1 essai", r2.Headline);
        Assert.True(r2.Entry.Good && r2.HasItem && r2.CardName == win.AnswerItem!.Name);
        Assert.Single(H(items, "detail").Rows);
    }

    [Fact]
    public void Enigme_et_detail_partagent_le_carnet_sans_s_ecraser_et_le_detail_se_reprend()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var path = Path.Combine(_dir, "daily.json");
        var book = DailyBook.Load(path);
        var riddle = new RiddleViewModel(kb, items, dir, book, path, H(items, "riddle"));
        var detail = new DetailViewModel(kb, items, book, path, H(items, "detail"));
        Assert.True(detail.Available);
        var wide0 = detail.Window.Width;
        riddle.GuessCommand.Execute(items.First(i => i.Card.Id != riddle.AnswerItem!.Card.Id));
        var wrong = items.First(i => i.Card.Id != detail.AnswerItem!.Card.Id);
        detail.GuessCommand.Execute(wrong);
        Assert.Equal(wide0, detail.Window.Width);   // le zoom ne change plus

        var again = DailyBook.Load(path);
        var riddle2 = new RiddleViewModel(kb, items, dir, again, path, H(items, "riddle"));
        var detail2 = new DetailViewModel(kb, items, again, path, H(items, "detail"));
        Assert.Single(riddle2.Guesses);
        Assert.Single(detail2.Guesses);
        detail2.GuessCommand.Execute(detail2.AnswerItem);
        Assert.True(detail2.IsFinished && detail2.Won);
        Assert.Equal(new DetailWindow(0, 0, 1, 1), detail2.Window);
        Assert.False(riddle2.IsFinished);
    }

    // ---------------------------------------------------------------- Lot C : Quizz du jour
    [Fact]
    public void La_banque_du_quizz_est_bien_formee_et_ses_reponses_sont_vraies()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var bank = QuizGame.Load(dir);
        Assert.True(bank.Count >= 200, "au moins 200 questions");
        Assert.Equal(bank.Count, bank.Select(q => q.Id).Distinct().Count());
        Assert.True(bank.Select(q => q.Category).Distinct().Count() >= 8);
        foreach (var q in bank) Assert.Equal(4, q.Options.Distinct().Count());
        // Les réponses de deux types de questions sont recalculées à partir des données du jeu.
        foreach (var q in bank.Where(q => q.Category is "element" or "pv"))
        {
            var card = kb.Cards.Single(c => c.SanitizedName == q.Id[(q.Id.IndexOf('-') + 1)..]);
            var expected = q.Category == "element" ? kb.ElementName(card.Element) : card.Hp.ToString();
            Assert.Equal(expected, q.Options[q.Answer]);
        }
        foreach (var q in bank.Where(q => q.Category == "qui-competence"))
        {
            var card = kb.Cards.Single(c => kb.CardName(c) == q.Options[q.Answer]);
            Assert.Contains(card.Abilities, a => q.Question.Contains("« " + kb.AbilityName(a) + " »"));
        }
    }

    [Fact]
    public void Le_quizz_du_jour_est_le_meme_pour_tous_avec_cinq_categories_differentes()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var bank = QuizGame.Load(dir);
        var day = new DateOnly(2026, 11, 20);
        var a = QuizGame.ForDay(bank, day)!; var b = QuizGame.ForDay(bank, day)!;
        Assert.Equal(a.Questions.Select(q => q.Id), b.Questions.Select(q => q.Id));
        Assert.Equal(QuizGame.Count, a.Questions.Select(q => q.Category).Distinct().Count());
        Assert.NotEqual(a.Questions.Select(q => q.Id), QuizGame.ForDay(bank, day.AddDays(1))!.Questions.Select(q => q.Id));
        Assert.Equal(3, a.Score([a.Questions[0].Answer.ToString(), a.Questions[1].Answer.ToString(), a.Questions[2].Answer.ToString(), ((a.Questions[3].Answer + 1) % 4).ToString(), "x"]));
    }

    [Fact]
    public void Le_quizz_demande_l_accord_puis_verrouille_les_onglets_jusqu_a_la_fin()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var path = Path.Combine(_dir, "daily.json");
        var vm = new QuizViewModel(dir, items, DailyBook.Load(path), path, H(items, "quiz"));
        var locks = 0;
        vm.LockChanged += () => locks++;
        Assert.True(vm.ShowIntro && !vm.IsLocked && !vm.CanAnswer);
        vm.AnswerCommand.Execute(vm.Options[0]);                       // avant l'accord : rien ne se passe
        Assert.Equal(0, vm.Score);
        vm.StartCommand.Execute(null);
        Assert.True(vm.IsLocked && !vm.ShowIntro && vm.CanAnswer);
        Assert.Equal(1, locks);
        Assert.Equal(4, vm.Options.Count);
        var q0 = vm.Current!;
        vm.AnswerCommand.Execute(vm.Options[q0.Answer]);               // bonne réponse : validée tout de suite, sans correction
        Assert.False(vm.ShowFeedback);
        Assert.True(vm.CanAnswer && vm.IsLocked);
        Assert.Equal("Question 2 sur 5", vm.ProgressText);
        Assert.Equal(1, vm.Score);
        for (var i = 1; i < QuizGame.Count; i++)
        {
            Assert.True(vm.IsLocked);
            Assert.False(vm.IsFinished);
            vm.AnswerCommand.Execute(vm.Options[(vm.Current!.Answer + 1) % 4]);   // toutes fausses
        }
        Assert.True(vm.IsFinished && !vm.IsLocked);
        Assert.Equal(2, locks);
        Assert.Equal(1, vm.Score);
        Assert.Equal(QuizGame.Count, vm.Recap.Count);
        Assert.Equal(4, vm.Recap.Count(r => !r.Right));
        Assert.True(new QuizViewModel(dir, items, DailyBook.Load(path), path, H(items, "quiz")).IsFinished);
        var quizRow = Assert.Single(vm.History.Rows);
        Assert.Equal("1 sur 5", quizRow.Headline);
        Assert.Equal(QuizGame.Count, quizRow.Lines.Count);
        Assert.Equal(1, quizRow.Lines.Count(l => l.Mark == "✔"));
    }

    [Fact]
    public void Quitter_le_quizz_ou_fermer_l_application_compte_les_questions_restantes_comme_fausses()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var path = Path.Combine(_dir, "daily.json");
        var vm = new QuizViewModel(dir, items, DailyBook.Load(path), path, H(items, "quiz"));
        vm.StartCommand.Execute(null);
        vm.AnswerCommand.Execute(vm.Options[vm.Current!.Answer]);      // 1 bonne réponse
        vm.AbandonCommand.Execute(null);                                  // on quitte à la question 2
        Assert.True(vm.IsFinished && !vm.IsLocked);
        Assert.Equal(1, vm.Score);
        Assert.Contains(vm.Recap, r => r.Chosen == "Pas de réponse");
        Assert.Equal(4, vm.Recap.Count(r => !r.Right));

        // Application fermée en plein quizz (autre fichier) : à la réouverture, c'est fini et les restantes sont fausses.
        var path2 = Path.Combine(_dir, "daily2.json");
        var a = new QuizViewModel(dir, items, DailyBook.Load(path2), path2, H(items, "quiz"));
        a.StartCommand.Execute(null);
        a.AnswerCommand.Execute(a.Options[a.Current!.Answer]);
        var b = new QuizViewModel(dir, items, DailyBook.Load(path2), path2, H(items, "quiz"));
        Assert.True(b.IsFinished && !b.IsLocked && !b.ShowIntro);
        Assert.Equal(1, b.Score);
    }

    [Fact]
    public void Les_questions_sur_une_carte_connaissent_leur_carte()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var bank = QuizGame.Load(dir);
        var withCard = bank.Where(q => q.Card is not null).ToList();
        Assert.True(withCard.Count > 200);
        foreach (var q in withCard) Assert.Contains(kb.Cards, c => c.SanitizedName == q.Card);
        Assert.All(bank.Where(q => q.Category is "plus-de-pv" or "intrus" or "qui-competence"), q => Assert.Null(q.Card));
    }
}
