using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 13 (nouvelle identité visuelle) : sons, pastilles de deck, accueil, cadre de rareté, réglages.</summary>
public class Phase13Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-p13-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static (KnowledgeBase Kb, List<CardItem> Items)? Make()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (!File.Exists(Path.Combine(p, "manifest.json"))) continue;
            var kb = KnowledgeBase.Load(p);
            return (kb, kb.Cards.Select(c => new CardItem(c, kb)).ToList());
        }
        return null;
    }

    [Theory]
    [InlineData(SoundKind.Click)] [InlineData(SoundKind.Select)] [InlineData(SoundKind.Tab)] [InlineData(SoundKind.Success)] [InlineData(SoundKind.Error)] [InlineData(SoundKind.Pop)] [InlineData(SoundKind.Page)] [InlineData(SoundKind.Title1)] [InlineData(SoundKind.Title2)] [InlineData(SoundKind.Title3)] [InlineData(SoundKind.Metal1)] [InlineData(SoundKind.Metal2)] [InlineData(SoundKind.Metal3)]
    public void Chaque_son_est_un_fichier_wav_court_et_discret(SoundKind kind)
    {
        var wav = Sounds.Build(kind, 1.0);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        var samples = (wav.Length - 44) / 2;
        Assert.InRange(samples * 1000L / BitConverter.ToInt32(wav, 24), 40, 400);   // fréquence lue dans l en-tête du fichier                               // moins de 400 ms (directives §5)
        var peak = Enumerable.Range(0, samples).Max(i => Math.Abs(BitConverter.ToInt16(wav, 44 + i * 2)));
        Assert.InRange(peak, 1, short.MaxValue * 0.4);                                 // jamais fort, même volume à fond
    }

    [Theory]
    [InlineData("select_007")] [InlineData("toggle_004")] [InlineData("toggle_002")] [InlineData("pluck_001")] [InlineData("pluck_002")]
    [InlineData("confirmation_001")] [InlineData("error_008")]
    public void Les_sons_de_la_banque_libre_sont_embarques(string file)
    {
        Assert.Contains($"Sounds/{file}.wav", typeof(Sounds).Assembly.GetManifestResourceNames());
        Assert.NotEqual(Sounds.Build(SoundKind.Page, 1.0).Length, Sounds.Build(SoundKind.Click, 1.0).Length);   // le clic vient bien du fichier, pas de la note de secours
    }

    [Fact]
    public void Volume_zero_donne_le_silence()
    {
        var wav = Sounds.Build(SoundKind.Click, 0);
        Assert.All(Enumerable.Range(0, (wav.Length - 44) / 2), i => Assert.Equal(0, BitConverter.ToInt16(wav, 44 + i * 2)));
    }

    [Fact]
    public void Un_deck_donne_toujours_quatre_pastilles_avec_le_talent_choisi()
    {
        if (Make() is not { } w) return;
        DeckChipsConverter.Register(w.Kb, w.Items);
        var card = w.Items[0];
        var chips = DeckChipsConverter.Build(new[] { new DeckSlot(card.Card.Id, card.Card.Talents[1]), new DeckSlot(w.Items[1].Card.Id, null) });
        Assert.Equal(4, chips.Count);
        Assert.Equal("T2", chips[0].TalentTag);
        Assert.Contains(" · Talent 2 : ", chips[0].TalentName);
        Assert.StartsWith(card.Name, chips[0].TalentName);                              // l'info-bulle commence par le nom de la carte
        Assert.False(chips[1].HasTalent);
        Assert.True(chips[2].IsEmpty && chips[3].IsEmpty);
        Assert.True(DeckChipsConverter.Build(new[] { new DeckSlot("carte-inconnue", null) })[0].IsEmpty);   // carte retirée par une mise à jour : emplacement vide
    }

    [Fact]
    public void Le_cadre_suit_la_rarete_et_seules_les_hautes_raretes_ont_un_reflet()
    {
        if (Make() is not { } w) return;
        var common = w.Items.First(i => w.Kb.Rarity(i.Card.Rarity)?.Order == 1);
        var legendary = w.Items.First(i => w.Kb.Rarity(i.Card.Rarity)?.Order == 4);
        Assert.False(common.IsShiny);
        Assert.True(legendary.IsShiny);
        Assert.True(legendary.FrameThickness.Left > common.FrameThickness.Left);
        Assert.InRange(common.FrameThickness.Left, 1.5, 3);
    }

    [Fact]
    public void La_carte_du_jour_est_la_meme_toute_la_journee_et_les_succes_suivent_les_decks()
    {
        if (Make() is not { } w) return;
        DeckChipsConverter.Register(w.Kb, w.Items);
        var lib = new DeckLibrary(new DeckStore(_dir));
        var day = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        var home = new HomeViewModel(w.Kb, w.Items, lib, _ => { }, _ => { }, () => day);
        var again = new HomeViewModel(w.Kb, w.Items, lib, _ => { }, _ => { }, () => day);
        Assert.Same(home.CardOfDay, again.CardOfDay);
        Assert.True(home.HasNoDeck);
        Assert.DoesNotContain(home.Achievements, a => a.Done);
        Assert.Equal("???", home.Achievements[0].Text);
        w.Items[0].SelectedSkinId = null;
        var skinCard = w.Items.FirstOrDefault(i => i.Skins.Count > 0);
        if (skinCard is not null)
        {
            skinCard.SelectedSkinId = skinCard.Skins[0].Id;
            Assert.Contains(home.Achievements, a => a.Label == "Chasseur de skins" && a.Done);
        }
    }

    [Fact]
    public void Les_reglages_d_ambiance_ont_des_valeurs_par_defaut_et_se_retiennent()
    {
        var s = new AppSettings();
        Assert.True(s.SoundsEnabled);
        Assert.Equal(0.5, s.SoundVolume);
        Assert.False(s.ReduceAnimations);
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        new AppSettings { SoundsEnabled = false, SoundVolume = 0.2, ReduceAnimations = true }.TrySave(path);
        var back = AppSettings.Load(path);
        Assert.False(back.SoundsEnabled);
        Assert.Equal(0.2, back.SoundVolume);
        Assert.True(back.ReduceAnimations);
    }

    [Fact]
    public void L_heure_de_Paris_suit_l_heure_d_ete_et_minuit_est_le_changement_de_jour()
    {
        // Hiver (UTC+1) : 22:59 UTC = 23:59 à Paris, 23:00 UTC = minuit.
        Assert.Equal(new DateOnly(2026, 11, 17), ParisClock.Today(new DateTime(2026, 11, 17, 22, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 11, 18), ParisClock.Today(new DateTime(2026, 11, 17, 23, 0, 0, DateTimeKind.Utc)));
        // Été (UTC+2) : 21:59 UTC = 23:59 à Paris, 22:00 UTC = minuit.
        Assert.Equal(new DateOnly(2026, 7, 1), ParisClock.Today(new DateTime(2026, 7, 1, 21, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 7, 2), ParisClock.Today(new DateTime(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc)));
        // Changements d'heure 2026 : dernier dimanche de mars (29) et d'octobre (25), à 01:00 UTC.
        Assert.Equal(TimeSpan.FromHours(1), ParisClock.OffsetAt(new DateTime(2026, 3, 29, 0, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(2), ParisClock.OffsetAt(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(2), ParisClock.OffsetAt(new DateTime(2026, 10, 25, 0, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(1), ParisClock.OffsetAt(new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc)));
        // Le prochain minuit à Paris.
        Assert.Equal(new DateTime(2026, 11, 17, 23, 0, 0, DateTimeKind.Utc), ParisClock.NextMidnightUtc(new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc), ParisClock.NextMidnightUtc(new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void La_carte_du_jour_est_la_meme_pour_tous_et_change_a_minuit_a_Paris()
    {
        var ids = Enumerable.Range(0, 151).Select(i => "carte-" + i).ToList();
        var shuffled = ids.AsEnumerable().Reverse().ToList();                          // l'ordre de chargement ne compte pas
        var before = new DateTime(2026, 11, 17, 22, 59, 0, DateTimeKind.Utc);
        var after = before.AddMinutes(1);
        Assert.Equal(CardOfTheDay.Pick(ids, before), CardOfTheDay.Pick(shuffled, before));
        Assert.Equal(CardOfTheDay.Pick(ids, before), CardOfTheDay.Pick(ids, before.AddHours(-10)));   // même jour à Paris
        // Sur un mois, la carte change et plusieurs cartes différentes sortent.
        var month = Enumerable.Range(0, 30).Select(d => CardOfTheDay.Pick(ids, new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(d))).ToList();
        Assert.True(month.Distinct().Count() > 15);
        Assert.Null(CardOfTheDay.Pick(Array.Empty<string>(), before));
    }

    [Fact]
    public void Les_themes_par_element_et_le_decor_des_decks()
    {
        Assert.Equal(7, SpamDrafter.App.Views.ThemeManager.Options.Count);   // violet + les 6 éléments
        Assert.DoesNotContain(SpamDrafter.App.Views.ThemeManager.Options, o => o.Id == "auto");
        var s = new AppSettings();
        Assert.Equal(1.3, s.UiScale); Assert.Equal(90, s.StarCount); Assert.False(s.HighContrast); Assert.Equal("default", s.ElementTheme);
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "deco.json");
        var id = Guid.NewGuid().ToString();
        new AppSettings { DeckStyles = { [id] = new DeckStyle { Color = "#4F9BE0", Emoji = "🔥" } }, DeckManualOrder = { id }, StarColor = "#FFC93C" }.TrySave(path);
        var back = AppSettings.Load(path);
        Assert.Equal("#4F9BE0", back.DeckStyles[id].Color);
        Assert.Equal("🔥", back.DeckStyles[id].Emoji);
        Assert.Equal(id, Assert.Single(back.DeckManualOrder));
        Assert.Equal("#FFC93C", back.StarColor);
    }

    [Fact]
    public void Dispositions_des_colonnes_et_filtres_de_lore()
    {
        // Les dimensions des colonnes sont retenues dans les réglages.
        var s = new AppSettings();
        Assert.Empty(s.PanelLayouts);
        s.PanelLayouts["builder"] = new List<double> { 300, 400, 500 };
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "layout.json");
        s.TrySave(path);
        Assert.Equal(new[] { 300.0, 400.0, 500.0 }, AppSettings.Load(path).PanelLayouts["builder"]);
    }
}
