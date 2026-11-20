using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ServerPlugin.Regions;
using VRageMath;
using Xunit;

public class RegionTests
{
    private static readonly Vector3D Center = new Vector3D(1e9, -2e9, 3e9);
    private const double Size = 250_000;

    private static CombatRegion Region(bool sphere) =>
        sphere
            ? new CombatRegion("Harbor", RegionRules.Peace, Center, Center, Size)
            : new CombatRegion("Harbor", RegionRules.Peace, Center + Size, Center - Size, null);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EntryAndFiringUseClosedBoundaries(bool sphere)
    {
        var region = Region(sphere);
        var edge = Center + new Vector3D(Size, 0, 0);
        var outside = edge + Vector3D.UnitX;
        Assert.True(region.Contains(edge));
        Assert.False(region.Contains(outside));
        Assert.True(region.Intersects(new BoundingBoxD(edge, outside)));
        Assert.False(region.Intersects(new BoundingBoxD(outside, outside + 1)));
        Assert.True(region.Entry(outside, edge).HasValue);
        Assert.True(region.Entry(outside, Center - new Vector3D(Size + 1, 0, 0)).HasValue);
        Assert.False(region.Entry(Center, outside).HasValue);
        Assert.False(region.Entry(outside, outside).HasValue);
        Assert.False(region.Entry(outside, outside + 100).HasValue);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PenetrationStopsAtFirstFace(bool sphere)
    {
        var region = Region(sphere);
        Assert.Equal(
            0.25,
            region.Entry(
                Center - new Vector3D(2 * Size, 0, 0),
                Center + new Vector3D(2 * Size, 0, 0)
            )
        );
    }

    [Fact]
    public void SphereSeparatesTangentFromNearMiss()
    {
        var sphere = Region(true);
        var a = Center + new Vector3D(-10, Size, 0);
        var b = Center + new Vector3D(10, Size, 0);
        Assert.True(sphere.Entry(a, b).HasValue);
        Assert.False(sphere.Entry(a + Vector3D.UnitY, b + Vector3D.UnitY).HasValue);
        Assert.False(sphere.Intersects(new BoundingBoxD(Center + Size, Center + Size + 1)));
    }

    [Fact]
    public void NarrowBoxBetweenEndpointsStillIntercepts()
    {
        var box = new CombatRegion(
            "lane",
            RegionRules.Incoming,
            new Vector3D(-1),
            new Vector3D(1),
            null
        );
        Assert.True(
            box.Entry(new Vector3D(-2, 1.00000001, 0), new Vector3D(2, .99999999, 0)).HasValue
        );
        Assert.False(
            box.Entry(new Vector3D(-2, 1.00000001, 0), new Vector3D(2, 1.00000002, 0)).HasValue
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DistanceAndNoticesFollowTheBorder(bool sphere)
    {
        var region = Region(sphere);
        var edge = Center + Vector3D.UnitX * Size;
        Assert.Equal(-Size, region.BoundaryDistance(Center));
        Assert.Equal(0, region.BoundaryDistance(edge));
        if (!sphere)
            Assert.Equal(5, region.BoundaryDistance(Center + Size + new Vector3D(0, 3, 4)));
        var observer = new RegionProximity();
        var notices = new List<string>();
        var snapshot = new[] { region };
        void Visit(double distance) =>
            observer.Sample(snapshot, edge + Vector3D.UnitX * distance, notices.Add);

        Visit(50000);
        Assert.Empty(observer.Nearby);
        Visit(1990);
        Visit(2010);
        Visit(1990);
        Assert.Same(region, Assert.Single(observer.Nearby));
        Visit(0);
        Visit(3);
        Visit(-5);
        Visit(20);
        Visit(2400);
        Visit(1900);
        Assert.Equal(4, notices.Count);
        Assert.StartsWith("Near Harbor", notices[0]);
        Assert.Contains("Firing and incoming fire blocked", notices[0]);
        Assert.StartsWith("Entered Harbor", notices[1]);
        Assert.Equal("Left Harbor", notices[2]);
        Assert.StartsWith("Near Harbor", notices[3]);
    }

    [Fact]
    public void ActiveRulesAndNearestSurfacesStayCurrentWhenRegionsOverlapOrChange()
    {
        var observer = new RegionProximity();
        var notices = new List<string>();
        var regions = Enumerable
            .Range(1, 6)
            .Select(i => new CombatRegion($"Port {i}", RegionRules.Firing, Center, Center, i * 100))
            .Reverse()
            .ToArray();
        observer.Sample(regions, Center, notices.Add);
        Assert.Equal(4, notices.Count);
        Assert.Equal("+3 more region notices", notices.Last());
        Assert.Equal(new[] { 100d, 200, 300, 400 }, observer.Nearby.Select(r => r.Radius.Value));
        observer.Sample(new[] { regions[0].WithRules(RegionRules.None) }, Center, notices.Add);
        Assert.Empty(observer.Nearby);
        Assert.Equal(4, notices.Count);
        observer.Sample(new[] { regions[0].WithRules(RegionRules.Incoming) }, Center, notices.Add);
        Assert.Contains("Incoming fire blocked", notices.Last());
        observer.Sample(Array.Empty<CombatRegion>(), Center, notices.Add);
        Assert.Empty(observer.Nearby);
    }

    [Fact]
    public void CompanionSnapshotUsesWorldMetresAndAnExplicitEmptyList()
    {
        var packet = RegionProximity.Encode(new[] { Region(true), Region(false) });
        Assert.Equal(114, packet.Length);
        using var reader = new BinaryReader(new MemoryStream(packet));
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(2, reader.ReadByte());
        foreach (var shape in new[] { Region(true), Region(false) })
        {
            foreach (
                var expected in new[]
                {
                    shape.First.X,
                    shape.First.Y,
                    shape.First.Z,
                    shape.Second.X,
                    shape.Second.Y,
                    shape.Second.Z,
                    shape.Radius ?? 0,
                }
            )
                Assert.Equal(expected, reader.ReadDouble());
        }
        Assert.Equal(new byte[] { 1, 0 }, RegionProximity.Encode(Array.Empty<CombatRegion>()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VisualRangeTracksTheNearestBorderIndependentlyOfHud(bool sphere)
    {
        var region = Region(sphere);
        var regions = new[] { region };
        var observer = new RegionProximity();
        var notices = new List<string>();
        var edge = Center + Vector3D.UnitX * Size;
        foreach (int sign in new[] { 1, -1 })
        {
            observer.Sample(regions, edge + Vector3D.UnitX * sign * 50000, notices.Add);
            Assert.Empty(observer.Nearby);
            observer.Sample(regions, edge + Vector3D.UnitX * sign * 49990, notices.Add);
            Assert.Same(region, Assert.Single(observer.Nearby));
            observer.Sample(regions, edge + Vector3D.UnitX * sign * 10001, notices.Add, 10);
            Assert.Empty(observer.Nearby);
        }
        notices.Clear();
        observer = new RegionProximity();
        observer.Sample(regions, edge + Vector3D.UnitX * 1000, notices.Add, 0);
        Assert.Empty(observer.Nearby);
        Assert.StartsWith("Near Harbor", Assert.Single(notices));
    }

    [Fact]
    public void SkinEditsRoundTripWithoutRearmingEntryOrChangingRules()
    {
        var legacy = Region(false).ToXml();
        legacy.Attribute("skin").Remove();
        legacy.Attribute("patternMeters").Remove();
        var before = CombatRegion.FromXml(legacy);
        Assert.Equal(BarrierSkin.Grid, before.Skin);
        Assert.Equal(500, before.PatternMeters);
        var after = before.WithSkin(BarrierSkin.Hexagon, 750);
        var observer = new RegionProximity();
        var notices = new List<string>();
        observer.Sample(new[] { before }, Center, notices.Add);
        observer.Sample(new[] { after }, Center, notices.Add);
        Assert.Single(notices);
        var restored = CombatRegion.FromXml(after.ToXml()).WithRules(RegionRules.Firing);
        Assert.Equal(BarrierSkin.Hexagon, restored.Skin);
        Assert.Equal(750, restored.PatternMeters);
        Assert.Equal(before.Rules, after.Rules);
        var packet = RegionProximity.Encode(new[] { after }, 40);
        Assert.Equal(63, packet.Length);
        Assert.Equal(new byte[] { 2, 1, 40, 0 }, packet.Take(4));
        Assert.Equal((byte)BarrierSkin.Hexagon, packet[60]);
        Assert.Equal(750, BitConverter.ToUInt16(packet, 61));
        Assert.Throws<ArgumentException>(() => before.WithSkin((BarrierSkin)200, 200));
        Assert.Throws<ArgumentException>(() => before.WithSkin(BarrierSkin.Dots, 0));
    }

    [Fact]
    public void InvalidRegionsCannotBeCreated()
    {
        Assert.Throws<ArgumentException>(() =>
            new CombatRegion("bad", RegionRules.Peace, Center, Center, double.NaN)
        );
        Assert.Throws<ArgumentException>(() =>
            new CombatRegion("bad", RegionRules.Peace, Center, Center, -1)
        );
        Assert.Throws<ArgumentException>(() =>
            new CombatRegion("bad", RegionRules.Peace, Center, Center, null)
        );
        Assert.Throws<ArgumentException>(() => Region(true).WithRules((RegionRules)8));
    }

    [Fact]
    public void ChangesSurviveRestartAndSaveAsAndStayWithTheirWorld()
    {
        var temporary = Directory.CreateTempSubdirectory();
        try
        {
            string template = Path.Combine(temporary.FullName, "template");
            string world = Path.Combine(temporary.FullName, "world");
            var store = new RegionStore(template);
            store.FollowSave(world);
            Assert.Equal("Created region 'Harbor'.", store.Change("Harbor", Region(false)));
            Assert.Empty(new RegionStore(template).Snapshot);
            Assert.Contains("already exists", store.Change("HARBOR", Region(true)));
            var copy = Path.Combine(temporary.FullName, "copy");
            store.FollowSave(copy);
            store.Change("harbor", edit: RegionRules.Incoming);
            Assert.Equal(RegionRules.Peace, Assert.Single(new RegionStore(world).Snapshot).Rules);
            var reloaded = Assert.Single(new RegionStore(copy).Snapshot);
            Assert.Equal(RegionRules.Incoming, reloaded.Rules);
            Assert.True(reloaded.Contains(Center));
            store.Change("HARBOR");
            Assert.Empty(new RegionStore(copy).Snapshot);
        }
        finally
        {
            temporary.Delete(true);
        }
    }

    [Fact]
    public void ReopeningTemplatePreservesTheLiveWorldsOwnRules()
    {
        var temporary = Directory.CreateTempSubdirectory();
        try
        {
            string template = Path.Combine(temporary.FullName, "template");
            string live = Path.Combine(temporary.FullName, "live");
            var source = new RegionStore(template);
            source.Change("Harbor", Region(true));
            source.FollowSave(live);
            source.Change("Harbor", edit: RegionRules.Incoming);
            var reboot = new RegionStore(template);
            reboot.FollowSave(live);
            Assert.Equal(RegionRules.Incoming, Assert.Single(reboot.Snapshot).Rules);
            reboot.Change("Harbor");
            new RegionStore(template).FollowSave(live);
            Assert.Empty(new RegionStore(live).Snapshot);
        }
        finally
        {
            temporary.Delete(true);
        }
    }

    [Fact]
    public void FailedSaveKeepsPreviousEffectiveRules()
    {
        var temporary = Directory.CreateTempSubdirectory();
        try
        {
            var store = new RegionStore(temporary.FullName);
            store.Change("Harbor", Region(true));
            string path = Path.Combine(temporary.FullName, "Storage", "Essentials", "Regions.xml");
            Directory.CreateDirectory(path + ".tmp");
            Assert.ThrowsAny<UnauthorizedAccessException>(() => store.Change("Harbor"));
            Assert.Equal(RegionRules.Peace, Assert.Single(store.Snapshot).Rules);
            Assert.Equal(Size, Assert.Single(new RegionStore(temporary.FullName).Snapshot).Radius);
            File.WriteAllText(path, "<Regions version='99'/>");
            Assert.Throws<InvalidDataException>(() => new RegionStore(temporary.FullName));
        }
        finally
        {
            temporary.Delete(true);
        }
    }
}
