using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>Inclinaison 3D d'une carte selon la souris (±14°), comme la grande carte de la fiche ; coupée par « Réduire les animations ».</summary>
public static class Tilt
{
    public static void Attach(Control box)
    {
        var rotation = new Rotate3DTransform { Depth = 700 };
        box.RenderTransform = rotation;
        box.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        box.PointerMoved += (_, e) =>
        {
            if (Motion.Reduce || box.Bounds.Width < 1) return;
            var p = e.GetPosition(box);
            rotation.AngleY = (p.X / box.Bounds.Width - 0.5) * 28;
            rotation.AngleX = -(p.Y / box.Bounds.Height - 0.5) * 28;
        };
        box.PointerExited += (_, _) => { rotation.AngleX = 0; rotation.AngleY = 0; };
    }
}
