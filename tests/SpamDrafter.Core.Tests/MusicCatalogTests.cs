using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;

namespace SpamDrafter.Core.Tests;

/// <summary>Musique du jeu : liste lue dans le paquet de données ; absente ou abîmée, elle est vide (jamais d'erreur).</summary>
public class MusicCatalogTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "spamdrafter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Sans_dossier_music_la_liste_est_vide() => Assert.Empty(MusicCatalog.Load(TempDir()));

    [Fact]
    public void Lit_les_musiques_et_ignore_les_fichiers_manquants_ou_hors_du_dossier()
    {
        var kb = TempDir();
        var music = Directory.CreateDirectory(Path.Combine(kb, "music")).FullName;
        File.WriteAllBytes(Path.Combine(music, "a.ogg"), [1]);
        File.WriteAllText(Path.Combine(music, "music.json"),
            """[{"id":"a","title":"Menu","file":"a.ogg"},{"id":"b","title":"Absente","file":"b.ogg"},{"id":"c","title":"Evasion","file":"../manifest.json"}]""");
        var list = MusicCatalog.Load(kb);
        var track = Assert.Single(list);
        Assert.Equal("Menu", track.Title);
        Assert.True(File.Exists(track.Path));
    }

    [Fact]
    public void Un_index_illisible_donne_une_liste_vide()
    {
        var kb = TempDir();
        Directory.CreateDirectory(Path.Combine(kb, "music"));
        File.WriteAllText(Path.Combine(kb, "music", "music.json"), "pas du json");
        Assert.Empty(MusicCatalog.Load(kb));
    }

    [Fact]
    public void La_musique_est_desactivee_par_defaut() => Assert.False(new AppSettings().MusicEnabled);
}
