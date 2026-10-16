using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DdsRecolor.Gui;

public sealed class CheckerboardSurface : Control
{
    private static readonly IBrush Light = new SolidColorBrush(Color.Parse("#D9DDD7"));
    private static readonly IBrush Dark = new SolidColorBrush(Color.Parse("#BBC2BA"));

    public override void Render(DrawingContext context)
    {
        const int tileSize = 16;
        for (int y = 0; y < Bounds.Height; y += tileSize)
        {
            for (int x = 0; x < Bounds.Width; x += tileSize)
            {
                bool alternate = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                context.FillRectangle(alternate ? Light : Dark, new Rect(x, y, tileSize, tileSize));
            }
        }
    }
}