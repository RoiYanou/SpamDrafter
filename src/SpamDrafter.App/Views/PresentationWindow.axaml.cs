using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

/// <summary>
/// Mode présentation d'un deck (phase 13) : les cartes en éventail, en grand, avec leur nom en haut et leur talent (« T1 » puis son nom) ;
/// le bouton enregistre la scène en image PNG à partager. Rien du jeu n'est ajouté à l'image : ce sont les illustrations que l'application affiche déjà.
/// Chaque carte est d'abord dessinée à plat (atelier « Bake ») puis transformée en image avant d'être inclinée : incliner directement
/// une carte rognée faisait des cartes noires et des illustrations déplacées dans l'image enregistrée.
/// </summary>
public partial class PresentationWindow : Window
{
    private readonly string _title = "";
    private readonly List<Viewbox> _faces = new();
    private readonly List<Image> _cards = new();
    private const double W = 225, H = 322;

    public PresentationWindow() => InitializeComponent();

    public PresentationWindow(string title, IReadOnlyList<DeckChip> chips) : this()
    {
        _title = title;
        TitleText.Text = title;
        var withCards = chips.Where(c => c.HasCard).ToList();
        foreach (var chip in withCards)
        {
            // « Nom · Talent 1 : Boule de feu » → « Boule de feu »
            var talent = chip.HasTalent ? chip.TalentName.Split(" : ", 2).Last() : "";
            var face = new CardFace
            {
                Width = 300, Height = 430, Item = chip.Card, ImageOverride = chip.Card!.LoadFullImage(),
                ShowName = true, ShowElement = true, ShowRarity = true, ShowIncome = true, HoverZoom = false, Holo = false,
                TalentTag = chip.TalentTag, TalentName = talent, TalentAtBottom = true,
            };
            // La carte est dessinée en 300 × 430 puis agrandie ×2 (traits et textes compris) dans une boîte de 600 × 860.
            var box = new Viewbox { Width = 600, Height = 860, Stretch = Stretch.Uniform, Child = face };
            _faces.Add(box);
            Bake.Children.Add(box);
            Canvas.SetLeft(box, -5000);
        }
        Opened += (_, _) => Dispatcher.UIThread.Post(BakeCards, DispatcherPriority.Background);
        Fan.SizeChanged += (_, _) => Layout();
    }

    /// <summary>Transforme chaque carte (dessinée à plat) en image, puis les dispose en éventail.</summary>
    private void BakeCards()
    {
        var n = _faces.Count;
        for (var i = 0; i < n; i++)
        {
            var face = _faces[i];
            face.Measure(new Size(600, 860)); face.Arrange(new Rect(0, 0, 600, 860));
            var bmp = new RenderTargetBitmap(new PixelSize(600, 860), new Vector(96, 96));   // 1 pixel = 1 unité : carte dessinée en 600 × 860
            bmp.Render(face);
            var angle = n == 1 ? 0 : -14 + 28.0 * i / (n - 1);
            var image = new Image
            {
                Source = bmp, Width = W, Height = H, Stretch = Stretch.Uniform,
                RenderTransform = new RotateTransform(angle), RenderTransformOrigin = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
            };
            _cards.Add(image);
            Fan.Children.Add(image);
        }
        Bake.Children.Clear();
        Layout();
        // Essai automatique (variable d'environnement SPAMDRAFTER_PRESENTATION_PNG) : enregistre l'image sans fenêtre de choix.
        if (Environment.GetEnvironmentVariable("SPAMDRAFTER_PRESENTATION_PNG") is { Length: > 0 } auto)
            DispatcherTimer.RunOnce(() => { using var fs = File.Create(auto); RenderStage(fs); }, TimeSpan.FromSeconds(1));
    }

    /// <summary>Centre l'éventail dans la scène.</summary>
    private void Layout()
    {
        if (_cards.Count == 0) return;
        var step = Math.Min(170, Math.Max(60, (Fan.Bounds.Width - 260) / Math.Max(1, _cards.Count - 1)));
        var total = W + step * (_cards.Count - 1);
        var left = (Fan.Bounds.Width - total) / 2;
        var top = Math.Max(20, (Fan.Bounds.Height - H) / 2);
        for (var i = 0; i < _cards.Count; i++)
        {
            Canvas.SetLeft(_cards[i], left + step * i);
            Canvas.SetTop(_cards[i], top + Math.Abs(i - (_cards.Count - 1) / 2.0) * 10);
        }
    }

    private void RenderStage(Stream stream)
    {
        var size = new PixelSize((int)Stage.Bounds.Width * 2, (int)Stage.Bounds.Height * 2);
        using var bitmap = new RenderTargetBitmap(size, new Vector(192, 192));
        bitmap.Render(Stage);
        bitmap.Save(stream);
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer l'image du deck",
            SuggestedFileName = string.Concat(_title.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' ? c : '_')).Trim() + ".png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("Image PNG") { Patterns = ["*.png"] }],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            RenderStage(stream);
            Message.Text = "Image enregistrée.";
            Sounds.Play(SoundKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message.Text = "Impossible d'enregistrer l'image : " + ex.Message;
            Sounds.Play(SoundKind.Error);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
