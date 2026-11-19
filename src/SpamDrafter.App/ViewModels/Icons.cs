using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une icône suivie d'un texte (compteurs du deck, résistances).</summary>
public sealed record IconText(Bitmap? Icon, string Text);

/// <summary>
/// Icônes du jeu (éléments, raretés, Tox) : chargées à la demande depuis la base de connaissances, décodées à la taille d'affichage
/// et gardées en mémoire. Une icône absente (base plus ancienne, fichier manquant) donne null : l'écran s'affiche sans.
/// </summary>
public static class Icons
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    public static Bitmap? Load(KnowledgeBase kb, string? relative, int height = 64)
    {
        if (string.IsNullOrEmpty(relative) || kb.ImagePath(relative) is not { } path) return null;
        return Cache.GetOrAdd($"{path}|{height}", _ =>
        {
            try { using var s = File.OpenRead(path); return Bitmap.DecodeToHeight(s, height); }
            catch (Exception) { return null; }
        });
    }

    private static readonly ConcurrentDictionary<(Bitmap, Color), Bitmap?> TintCache = new();

    /// <summary>Copie d'une icône peinte d'une seule couleur (l'icône sert de masque : sa forme est gardée, sa couleur remplacée).</summary>
    public static Bitmap? Tinted(Bitmap source, Color color) => TintCache.GetOrAdd((source, color), k =>
    {
        try
        {
            var rt = new Avalonia.Media.Imaging.RenderTargetBitmap(source.PixelSize, new Avalonia.Vector(96, 96));
            var rect = new Avalonia.Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height);
            using (var ctx = rt.CreateDrawingContext())
            using (ctx.PushOpacityMask(new ImageBrush(source) { Stretch = Stretch.Uniform }, rect))
                ctx.DrawRectangle(new SolidColorBrush(color), null, rect);
            return rt;
        }
        catch (Exception) { return null; }
    });

    /// <summary>
    /// Illustration d'une capacité : icônes de ses éléments, ou, sans élément, logo de chance de critique (jaune, comme la fiche) et/ou de précision.
    /// </summary>
    public static (IReadOnlyList<Bitmap> Elements, IReadOnlyList<Bitmap> Logos) AbilityPictures(KnowledgeBase kb, string abilityId)
    {
        var visual = kb.Descriptions.AbilityVisual(abilityId);
        var elements = visual.Elements.Select(e => Element(kb, e, 64)).OfType<Bitmap>().ToList();
        var logos = new List<Bitmap>();
        if (visual.Crit && CritChanceIcon(kb) is { } yellow) logos.Add(yellow);
        if (visual.Precision && Load(kb, "images/icons/stats/accuracy.webp", 64) is { } prec) logos.Add(prec);
        return (elements, logos);
    }

    private static readonly ConcurrentDictionary<Bitmap, (Bitmap Badge, Bitmap Mask)?> BadgeCache = new();

    /// <summary>
    /// Icône avec un contour noir qui colle à la forme du PNG (silhouette noire décalée dans 16 directions, icône par-dessus), et la même image
    /// sans contour (même taille, pour masquer le reflet métallique). Null si l'image manque.
    /// </summary>
    public static (Bitmap Badge, Bitmap Mask)? OutlinedBadge(Bitmap? icon)
    {
        if (icon is null) return null;
        return BadgeCache.GetOrAdd(icon, src =>
        {
            try
            {
                const int pad = 5;
                var size = new Avalonia.PixelSize(src.PixelSize.Width + 2 * pad, src.PixelSize.Height + 2 * pad);
                var from = new Avalonia.Rect(0, 0, src.PixelSize.Width, src.PixelSize.Height);
                var inner = new Avalonia.Rect(pad, pad, src.PixelSize.Width, src.PixelSize.Height);
                var black = Tinted(src, Colors.Black);
                var badge = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96));
                using (var ctx = badge.CreateDrawingContext())
                {
                    if (black is not null)
                        for (var i = 0; i < 16; i++)
                        {
                            var a = 2 * Math.PI * i / 16;
                            ctx.DrawImage(black, from, inner.Translate(new Avalonia.Vector(pad * 0.8 * Math.Cos(a), pad * 0.8 * Math.Sin(a))));
                        }
                    ctx.DrawImage(src, from, inner);
                }
                var mask = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96));
                using (var ctx = mask.CreateDrawingContext()) ctx.DrawImage(src, from, inner);
                return (badge, mask);
            }
            catch (Exception) { return null; }
        });
    }

    /// <summary>Icône d'élément d'une carte avec contour noir et masque (voir <see cref="OutlinedBadge"/>).</summary>
    public static (Bitmap Badge, Bitmap Mask)? ElementBadge(KnowledgeBase kb, string? elementId) =>
        elementId is null ? null : OutlinedBadge(Element(kb, elementId, 96));

    /// <summary>Spamton avec contour noir et masque.</summary>
    public static (Bitmap Badge, Bitmap Mask)? SpamtonBadge(KnowledgeBase kb) => OutlinedBadge(Spamton(kb, 96));

    private static readonly ConcurrentDictionary<Bitmap, Bitmap?> CritCache = new();

    /// <summary>
    /// Logo « chance de coup critique » : le logo du jeu (étoile bleue au liseré blanc) repeint, l'intérieur en jaune et le liseré blanc en noir.
    /// Le liseré garde son épaisseur d'origine et reste à l'intérieur de la forme : le logo ne grandit pas. Null sans image.
    /// </summary>
    public static Bitmap? CritChanceIcon(KnowledgeBase kb)
    {
        if (Load(kb, "images/icons/stats/critical.webp", 128) is not { } src) return null;
        return CritCache.GetOrAdd(src, s =>
        {
            try
            {
                int w = s.PixelSize.Width, h = s.PixelSize.Height, stride = w * 4;
                var buf = new byte[stride * h];
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { s.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), buf.Length, stride); }
                finally { handle.Free(); }
                var format = s.Format ?? Avalonia.Platform.PixelFormat.Bgra8888;
                int ri = format == Avalonia.Platform.PixelFormat.Rgba8888 ? 0 : 2, bi = 2 - ri;
                for (var i = 0; i < buf.Length; i += 4)
                {
                    int alpha = buf[i + 3];
                    if (alpha == 0) continue;
                    var red = Math.Min(255.0, buf[i + ri] * 255.0 / alpha);          // couleur d'origine (non prémultipliée)
                    var rim = Math.Clamp((red - 127) / (255 - 127.0), 0, 1);        // 0 : intérieur bleu, 1 : liseré blanc
                    double r = 255 + (0x16 - 255) * rim, g = 0xD8 + (0x0E - 0xD8) * rim, b = 0x4A + (0x2A - 0x4A) * rim;
                    buf[i + ri] = (byte)(r * alpha / 255); buf[i + 1] = (byte)(g * alpha / 255); buf[i + bi] = (byte)(b * alpha / 255);
                }
                var wb = new Avalonia.Media.Imaging.WriteableBitmap(s.PixelSize, new Avalonia.Vector(96, 96), format, Avalonia.Platform.AlphaFormat.Premul);
                using (var fb = wb.Lock())
                    for (var y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(buf, y * stride, fb.Address + y * fb.RowBytes, stride);
                return wb;
            }
            catch (Exception) { return null; }
        });
    }

    /// <summary>Version en noir et blanc d'une icône (logos de filtre désactivés) ; null si l'image ne peut pas être lue.</summary>
    public static Bitmap? Gray(Bitmap? s)
    {
        if (s is null) return null;
        try
        {
            int w = s.PixelSize.Width, h = s.PixelSize.Height, stride = w * 4;
            var buf = new byte[stride * h];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { s.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), buf.Length, stride); }
            finally { handle.Free(); }
            var format = s.Format ?? Avalonia.Platform.PixelFormat.Bgra8888;
            for (var i = 0; i < buf.Length; i += 4)
            {
                // Image prémultipliée : le gris se calcule directement sur les composantes (l'opacité ne change pas).
                var gray = (byte)Math.Min(255, (buf[i] + buf[i + 1] + buf[i + 2]) / 3);
                buf[i] = buf[i + 1] = buf[i + 2] = gray;
            }
            var wb = new Avalonia.Media.Imaging.WriteableBitmap(s.PixelSize, new Avalonia.Vector(96, 96), format, Avalonia.Platform.AlphaFormat.Premul);
            using (var fb = wb.Lock())
                for (var y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(buf, y * stride, fb.Address + y * fb.RowBytes, stride);
            return wb;
        }
        catch (Exception) { return null; }
    }

    public static Bitmap? Element(KnowledgeBase kb, string? elementId, int height = 40) =>
        elementId is null ? null : Load(kb, kb.Element(elementId)?.Icon, height);

    public static Bitmap? Rarity(KnowledgeBase kb, string? rarityId, int height = 40) =>
        rarityId is null ? null : Load(kb, kb.Rarity(rarityId)?.Icon, height);

    public static Bitmap? Tox(KnowledgeBase kb, ToxEntry tox, int height = 96) => Load(kb, tox.Image, height);

    /// <summary>Couleur « #RRGGBB » d'une rareté (jeu : <c>_primaryColor</c>) ; gris neutre si la base n'en donne pas.</summary>
    public static IBrush RarityBrush(KnowledgeBase kb, string? rarityId) => Brush(kb.Rarity(rarityId ?? "")?.Color, "#666666");

    /// <summary>Couleur de texte d'une rareté (jeu : <c>_primaryTextColor</c>).</summary>
    public static IBrush RarityTextBrush(KnowledgeBase kb, string? rarityId) => Brush(kb.Rarity(rarityId ?? "")?.TextColor, "#CCCCCC");

    /// <summary>
    /// Couleur du cadre d'une carte : celle de la rareté du jeu, éclaircie (le bleu des cartes rares était trop sombre sur le fond violet) ;
    /// la commune prend le gris clair du jeu.
    /// </summary>
    public static IBrush FrameBrush(KnowledgeBase kb, string? rarityId)
    {
        var order = kb.Rarity(rarityId ?? "")?.Order ?? 1;
        if (order <= 1) return RarityTextBrush(kb, rarityId);
        var c = ((SolidColorBrush)RarityBrush(kb, rarityId)).Color;
        double t = 0.42;
        return new SolidColorBrush(Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t)));
    }

    /// <summary>Icône du Spamton (la monnaie du jeu), à côté du revenu d'une carte ; null si la base ne la contient pas.</summary>
    public static Bitmap? Spamton(KnowledgeBase kb, int height = 40) => Load(kb, "images/icons/spamton.webp", height);

    /// <summary>Couleur d'interface d'un élément (choix de présentation, absent de nos données) ; blanc si inconnu.</summary>
    public static IBrush ElementAccent(string? elementId) => new SolidColorBrush(Color.Parse(elementId switch
    {
        "fire" => "#FF7043", "water" => "#4FB3FF", "earth" => "#C08A55", "nature" => "#6FD65A",
        "light" => "#FFD84A", "shadow" => "#A57BFF", "divine" => "#FF9AE6", _ => "#FFFFFF",
    }));

    /// <summary>La même couleur avec une transparence donnée (0 à 255).</summary>
    public static IBrush WithAlpha(IBrush brush, byte alpha) =>
        brush is ISolidColorBrush b ? new SolidColorBrush(Color.FromArgb(alpha, b.Color.R, b.Color.G, b.Color.B)) : brush;

    private static IBrush Brush(string? hex, string fallback) =>
        new SolidColorBrush(Color.TryParse(hex ?? fallback, out var c) ? c : Color.Parse(fallback));
}
