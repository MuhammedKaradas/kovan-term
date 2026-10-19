using System.Windows;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class NodePlacementTests
{
    private static readonly Size NodeSize = new(560, 374);

    [Fact]
    public void BosAlanda_IstenenKonumuDoner()
    {
        Assert.Equal(new Point(100, 100), NodePlacement.FindFreeSpot([], new Point(100, 100), NodeSize));
    }

    [Fact]
    public void DoluKonumda_KesismeyenEnYakinYeriDoner()
    {
        var occupied = new List<Rect> { new(new Point(100, 100), NodeSize) };

        var spot = NodePlacement.FindFreeSpot(occupied, new Point(150, 120), NodeSize);

        Assert.NotEqual(new Point(150, 120), spot);
        var placed = new Rect(spot, NodeSize);
        Assert.False(placed.IntersectsWith(occupied[0]));
    }

    [Fact]
    public void KalabalikIzgarada_HicbirDugumleKesismez()
    {
        // 3x3 dolu ızgara, ortasına yerleştirmeye çalış
        var occupied = new List<Rect>();
        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
            occupied.Add(new Rect(new Point(x * 600, y * 414), NodeSize));

        var spot = NodePlacement.FindFreeSpot(occupied, new Point(600, 414), NodeSize);
        var placed = new Rect(spot, NodeSize);

        Assert.All(occupied, o => Assert.False(placed.IntersectsWith(o)));
    }

    [Fact]
    public void GorunurAlanVerilince_IzgaradakiBoslugaYerlestirir()
    {
        // 2x2 ızgaranın sağ alt hücresi boş; görünür alan ızgarayı tam kapsıyor
        var occupied = new List<Rect>
        {
            new(new Point(40, 40), NodeSize), new(new Point(640, 40), NodeSize), new(new Point(40, 454), NodeSize),
        };
        var viewport = new Rect(0, 0, 1260, 880);

        var spot = NodePlacement.FindFreeSpot(occupied, new Point(350, 250), NodeSize, viewport);

        Assert.Equal(new Point(640, 454), spot);
    }

    [Fact]
    public void GorunurAlandaYerYoksa_YineDeKesismeyenBirYerBulur()
    {
        var occupied = new List<Rect> { new(new Point(0, 0), NodeSize) };
        var tinyViewport = new Rect(0, 0, 600, 400);

        var spot = NodePlacement.FindFreeSpot(occupied, new Point(0, 0), NodeSize, tinyViewport);

        Assert.False(new Rect(spot, NodeSize).IntersectsWith(occupied[0]));
    }
}
