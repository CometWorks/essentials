using System;
using System.IO;
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
