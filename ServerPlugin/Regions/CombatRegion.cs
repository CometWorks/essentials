using System;
using System.IO;
using System.Xml.Linq;
using VRageMath;

namespace ServerPlugin.Regions;

[Flags]
public enum RegionRules
{
    None = 0,
    Firing = 1,
    Incoming = 2,
    Peace = Firing | Incoming,
}

// Stable byte IDs in the companion's region snapshot.
public enum BarrierSkin : byte
{
    Grid,
    Default,
    Aura,
    Organic,
    Rain,
    Dots,
    Disco,
    Noise,
    Hexagon,
    Lines,
    Digital,
    Gloura,
    Clang,
    Voronoi,
    Restricted,
    KeenSWH,
    Disabled,
}

// Coordinates and radii are world metres. A missing radius denotes a box.
internal sealed class CombatRegion
{
    public string Name { get; }
    public RegionRules Rules { get; }
    public Vector3D First { get; }
    public Vector3D Second { get; }
    public double? Radius { get; }
    public BarrierSkin Skin { get; }
    public int PatternMeters { get; }

    // Appearance-only edits retain the observer's approach/entry state.
    public object NoticeKey { get; private set; } = new object();

    public CombatRegion(
        string name,
        RegionRules rules,
        Vector3D first,
        Vector3D second,
        double? radius,
        BarrierSkin skin = BarrierSkin.Grid,
        int patternMeters = 500
    )
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Trim() != name)
            throw new ArgumentException(
                "Use a region name of 1 to 100 characters without outer spaces."
            );
        System.Xml.XmlConvert.VerifyXmlChars(name);
        if (!Enum.IsDefined(typeof(RegionRules), rules))
            throw new ArgumentException("Choose firing, incoming, peace, or none.");
        if (!Finite(first) || !Finite(second))
            throw new ArgumentException("Coordinates must be finite world positions.");
        if (
            radius.HasValue
            && (!(radius.Value > 0) || double.IsInfinity(radius.Value * radius.Value))
        )
            throw new ArgumentException("The sphere needs a finite positive radius.");
        if (!radius.HasValue && (first.X == second.X || first.Y == second.Y || first.Z == second.Z))
            throw new ArgumentException("Box corners must differ on all three axes.");

        if (!Enum.IsDefined(typeof(BarrierSkin), skin))
            throw new ArgumentException("Unknown barrier skin.");
        if (patternMeters < 64 || patternMeters > 8192)
            throw new ArgumentException("Pattern size must be 64 to 8192 metres.");
        Skin = skin;
        PatternMeters = patternMeters;
        Name = name;
        Rules = rules;
        Radius = radius;
        First = radius.HasValue ? first : Vector3D.Min(first, second);
        Second = radius.HasValue ? first : Vector3D.Max(first, second);
    }

    private static bool Finite(Vector3D position) =>
        !double.IsNaN(position.LengthSquared()) && !double.IsInfinity(position.LengthSquared());

    public CombatRegion WithRules(RegionRules rules) =>
        new CombatRegion(Name, rules, First, Second, Radius, Skin, PatternMeters);

    public CombatRegion WithSkin(BarrierSkin skin, int? meters) =>
        new CombatRegion(Name, Rules, First, Second, Radius, skin, meters ?? PatternMeters)
        {
            NoticeKey = this.NoticeKey,
        };

    public bool Contains(Vector3D point) =>
        Radius is double radius
            ? Vector3D.DistanceSquared(point, First) <= radius * radius
            : new BoundingBoxD(First, Second).Contains(point) != ContainmentType.Disjoint;

    // Negative inside, zero on the border, positive outside (including box corners).
    public double BoundaryDistance(Vector3D point)
    {
        if (Radius is double radius)
            return Vector3D.Distance(point, First) - radius;
        var outside = Vector3D.Max(First - point, point - Second);
        double largest = Math.Max(outside.X, Math.Max(outside.Y, outside.Z));
        return largest <= 0 ? largest : Vector3D.Max(outside, Vector3D.Zero).Length();
    }

    public bool Intersects(BoundingBoxD bounds) =>
        Radius is double radius
            ? Vector3D.DistanceSquared(First, Vector3D.Clamp(First, bounds.Min, bounds.Max))
                <= radius * radius
            : new BoundingBoxD(First, Second).Intersects(bounds);

    // Returns the fraction of the segment at its first contact from outside.
    // A shot originating inside may travel within the region or leave it.
    public double? Entry(Vector3D from, Vector3D to)
    {
        if (Contains(from))
            return null;
        Vector3D delta = to - from;
        if (Radius is double radius)
        {
            double lengthSquared = delta.LengthSquared();
            if (lengthSquared == 0)
                return null;
            double closest = Vector3D.Dot(First - from, delta) / lengthSquared;
            if (closest <= 0)
                return null;
            double perpendicularSquared = Vector3D.DistanceSquared(from + closest * delta, First);
            double chordSquared = radius * radius - perpendicularSquared;
            if (chordSquared < 0)
                return null;
            double entry = closest - Math.Sqrt(chordSquared / lengthSquared);
            return entry <= 1 || Contains(to) ? Math.Max(0, Math.Min(1, entry)) : null;
        }

        double enter = 0,
            exit = 1;
        bool hits =
            ClipAxis(from.X, delta.X, First.X, Second.X, ref enter, ref exit)
            && ClipAxis(from.Y, delta.Y, First.Y, Second.Y, ref enter, ref exit)
            && ClipAxis(from.Z, delta.Z, First.Z, Second.Z, ref enter, ref exit);
        return hits ? enter : null;
    }

    // A parametric segment avoids the parallel-direction tolerance in VRage's infinite-ray test.
    private static bool ClipAxis(
        double start,
        double delta,
        double min,
        double max,
        ref double enter,
        ref double exit
    )
    {
        if (delta == 0)
            return start >= min && start <= max;
        double a = (min - start) / delta;
        double b = (max - start) / delta;
        enter = Math.Max(enter, Math.Min(a, b));
        exit = Math.Min(exit, Math.Max(a, b));
        return enter <= exit;
    }

    public XElement ToXml()
    {
        XElement element = new XElement(
            Radius.HasValue ? "Sphere" : "Box",
            new XAttribute("name", Name),
            new XAttribute("rules", Rules),
            new XAttribute("skin", Skin),
            new XAttribute("patternMeters", PatternMeters),
            Point("First", First)
        );
        if (Radius.HasValue)
            element.Add(new XAttribute("radius", Radius.Value));
        else
            element.Add(Point("Second", Second));
        return element;
    }

    private static XElement Point(string tag, Vector3D p) =>
        new XElement(
            tag,
            new XAttribute("x", p.X),
            new XAttribute("y", p.Y),
            new XAttribute("z", p.Z)
        );

    private static Vector3D ReadPoint(XElement e) =>
        new Vector3D((double)e.Attribute("x"), (double)e.Attribute("y"), (double)e.Attribute("z"));

    public static CombatRegion FromXml(XElement e)
    {
        bool sphere = e.Name == "Sphere";
        if (!sphere && e.Name != "Box")
            throw new InvalidDataException("Unknown region shape.");
        Vector3D first = ReadPoint(
            e.Element("First") ?? throw new InvalidDataException("Missing first point.")
        );
        return new CombatRegion(
            (string)e.Attribute("name"),
            (RegionRules)Enum.Parse(typeof(RegionRules), (string)e.Attribute("rules")),
            first,
            sphere
                ? first
                : ReadPoint(
                    e.Element("Second") ?? throw new InvalidDataException("Missing second corner.")
                ),
            sphere ? (double)e.Attribute("radius") : null,
            (BarrierSkin)
                Enum.Parse(typeof(BarrierSkin), (string)e.Attribute("skin") ?? "Grid", true),
            (int?)e.Attribute("patternMeters") ?? 500
        );
    }
}
