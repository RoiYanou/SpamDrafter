using System.IO.Compression;
using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Settings;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 12, lot A : mémoire de l'interface, sauvegarde et restauration des decks.</summary>
public class SettingsAndBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spamdrafter-set-" + Guid.NewGuid());

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static Deck MakeDeck(string name, DateTime? modified = null, string card = "carte") =>
        new() { Name = name, GameVersion = "1.30", ModifiedUtc = modified, Slots = { new DeckSlot(card, null) } };

    // ----- Réglages -----

    [Fact]
    public void Les_reglages_font_l_aller_retour()
    {
        var path = Path.Combine(_root, "settings.json");
        var s = new AppSettings
        {
            SelectedTab = 2, WindowWidth = 1111, WindowHeight = 777, WindowX = 40, WindowY = 50, Maximized = true,
            DeckSort = "recent", DeckOrigin = "generated", FavoritesOnly = true,
        };
        s.Browsers["library"] = new BrowserSettings { Element = "fire", Rarity = "rarity-epic" };
        Assert.True(s.TrySave(path));
        var back = AppSettings.Load(path);
        Assert.Equal(2, back.SelectedTab);
        Assert.Equal(1111, back.WindowWidth);
        Assert.True(back.Maximized);
        Assert.Equal("recent", back.DeckSort);
        Assert.True(back.FavoritesOnly);
        Assert.Equal("fire", back.Browsers["library"].Element);
    }

    [Fact]
    public void Un_fichier_de_reglages_absent_ou_abime_donne_les_reglages_par_defaut()
    {
        var missing = AppSettings.Load(Path.Combine(_root, "nope.json"));
        Assert.Equal(0, missing.SelectedTab);
        Assert.Equal("name", missing.DeckSort);

        Directory.CreateDirectory(_root);
        var bad = Path.Combine(_root, "bad.json");
        File.WriteAllText(bad, "{ pas du json");
        Assert.Equal(0, AppSettings.Load(bad).SelectedTab);
        File.WriteAllText(bad, "{\"SelectedTab\": 3, \"Inconnu\": 1}");   // champ inconnu ignoré
        Assert.Equal(3, AppSettings.Load(bad).SelectedTab);
    }

    [Fact]
    public void Enregistrer_ne_leve_jamais_d_erreur()
    {
        // Le « dossier » du fichier est en réalité un fichier : l'écriture échoue, sans exception.
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "bloque");
        File.WriteAllText(blocker, "x");
        Assert.False(new AppSettings().TrySave(Path.Combine(blocker, "settings.json")));
    }

    // ----- Sauvegarde et restauration -----

    [Fact]
    public void Sauvegarde_puis_restauration_dans_un_dossier_vide_retrouve_tous_les_decks()
    {
        var decks = Path.Combine(_root, "decks");
        var store = new DeckStore(decks);
        var a = MakeDeck("A"); var b = MakeDeck("B");
        store.Save(a); store.Save(b);
        var zip = Path.Combine(_root, "sauvegarde.zip");
        Assert.Equal(2, DeckBackup.Create(decks, zip));

        var other = Path.Combine(_root, "autre");
        var r = DeckBackup.Restore(zip, other);
        Assert.Equal(2, r.Added);
        Assert.Empty(r.Problems);
        Assert.Equal(new[] { "A", "B" }, new DeckStore(other).LoadAll().Select(d => d.Name));
    }

    [Fact]
    public void La_restauration_ne_supprime_rien_et_garde_la_version_la_plus_recente()
    {
        var decks = Path.Combine(_root, "decks");
        var store = new DeckStore(decks);
        var old = MakeDeck("Ancien nom", new DateTime(2026, 1, 1));
        store.Save(old);
        var zip = Path.Combine(_root, "s.zip");
        DeckBackup.Create(decks, zip);

        // Depuis la sauvegarde : le deck a été renommé (plus récent) et un deck a été créé.
        var renamed = MakeDeck("Nouveau nom", new DateTime(2026, 3, 1)); renamed.Id = old.Id;
        store.Save(renamed);
        store.Save(MakeDeck("Créé après"));
        var r = DeckBackup.Restore(zip, decks);
        Assert.Equal(0, r.Added);
        Assert.Equal(0, r.Updated);
        Assert.Equal(1, r.Skipped);
        Assert.Equal(new[] { "Créé après", "Nouveau nom" }, store.LoadAll().Select(d => d.Name));   // la copie récente est gardée

        // Inversement : une sauvegarde plus récente met le deck à jour.
        var newer = MakeDeck("Depuis la sauvegarde", new DateTime(2026, 9, 1)); newer.Id = old.Id;
        var zip2 = Path.Combine(_root, "s2.zip");
        var tmp = Path.Combine(_root, "tmp"); new DeckStore(tmp).Save(newer);
        DeckBackup.Create(tmp, zip2);
        Assert.Equal(1, DeckBackup.Restore(zip2, decks).Updated);
        Assert.Contains(store.LoadAll(), d => d.Name == "Depuis la sauvegarde");
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void Un_fichier_qui_n_est_pas_une_sauvegarde_est_refuse_sans_rien_casser()
    {
        Directory.CreateDirectory(_root);
        var notZip = Path.Combine(_root, "faux.zip");
        File.WriteAllText(notZip, "pas un zip");
        var decks = Path.Combine(_root, "decks");
        new DeckStore(decks).Save(MakeDeck("Reste"));
        var r = DeckBackup.Restore(notZip, decks);
        Assert.Equal(0, r.Added + r.Updated);
        Assert.NotEmpty(r.Problems);
        Assert.Single(new DeckStore(decks).LoadAll());
    }

    [Fact]
    public void Un_zip_avec_des_fichiers_qui_ne_sont_pas_des_decks_les_signale_et_ignore()
    {
        Directory.CreateDirectory(_root);
        var zipPath = Path.Combine(_root, "mixte.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            void Put(string name, string text) { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(text); }
            Put("abime.json", "{ pas du json");
            Put("vide.json", "{}");                 // pas d'identifiant : pas un deck
            Put("dossier/ailleurs.json", "{}");     // dans un sous-dossier : ignoré (aucune écriture hors du dossier des decks)
            Put("note.txt", "bonjour");
            var good = MakeDeck("Bon");
            Put(good.Id + ".json", System.Text.Json.JsonSerializer.Serialize(good));
        }
        var decks = Path.Combine(_root, "decks");
        var r = DeckBackup.Restore(zipPath, decks);
        Assert.Equal(1, r.Added);
        Assert.Equal(2, r.Problems.Count);
        Assert.Equal("Bon", Assert.Single(new DeckStore(decks).LoadAll()).Name);
    }

    [Fact]
    public void Le_nom_propose_contient_la_date()
    {
        Assert.Equal("SpamDrafter-decks-2026-11-16.zip", DeckBackup.SuggestedName(new DateTime(2026, 11, 16)));
    }

    [Fact]
    public void Le_deck_builder_sauvegarde_et_restaure_par_la_bibliotheque()
    {
        var decks = Path.Combine(_root, "decks");
        var lib = new DeckLibrary(new DeckStore(decks));
        lib.Save(MakeDeck("Un"));
        var zip = Path.Combine(_root, "b.zip");
        Assert.Equal(1, lib.BackupTo(zip));
        lib.Delete(lib.Decks[0].Id);
        Assert.Empty(lib.Decks);
        var r = lib.RestoreFrom(zip);
        Assert.Equal(1, r.Added);
        Assert.Single(lib.Decks);
    }
}
