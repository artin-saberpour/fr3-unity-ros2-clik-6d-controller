using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>
/// Minimal simulation utilities: parse frames, filename helpers,
/// constraint definitions, and average-angle evaluation (no scene required).
/// </summary>
public static class TrajectorySimCore
{
    // ---------- Data ----------
    public struct SimTrans { public Vector3 pos; public Quaternion rot; }

    public class SimCtx
    {
        public Dictionary<string, SimTrans> map; // name -> pose this frame
        public string handName; // "left_hand" or "right_hand"
    }

    public delegate Vector3? VecGetter(SimCtx ctx);

    public struct SimPair
    {
        public VecGetter Source;
        public VecGetter Target;
    }

    // ---------- Parsing ----------
    public static Dictionary<string, SimTrans> ParseSimFrame(string line)
    {
        var dict = new Dictionary<string, SimTrans>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(line)) return dict;

        if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
            line = line.Substring("TRANSFORM|".Length);

        var segments = line.Split(';');
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].Trim();
            if (string.IsNullOrEmpty(seg)) continue;

            var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 8) continue;

            string name = parts[0];

            if (!float.TryParse(parts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float px) ||
                !float.TryParse(parts[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float py) ||
                !float.TryParse(parts[3], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float pz) ||
                !float.TryParse(parts[4], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float rx) ||
                !float.TryParse(parts[5], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float ry) ||
                !float.TryParse(parts[6], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float rz) ||
                !float.TryParse(parts[7], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float rw))
                continue;

            dict[name] = new SimTrans
            {
                pos = new Vector3(px, py, pz),
                rot = new Quaternion(rx, ry, rz, rw)
            };
        }

        return dict;
    }

    public static List<string> ExtractBodyPartNamesFromLine(string line)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return names;
        if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
            line = line.Substring("TRANSFORM|".Length);

        bool inBody = false;
        foreach (var segRaw in line.Split(';'))
        {
            var seg = segRaw.Trim();
            if (string.IsNullOrEmpty(seg)) continue;

            var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            string name = parts[0];
            if (string.Equals(name, "left_hand", StringComparison.Ordinal))
                inBody = true;

            if (inBody) names.Add(name);
        }
        return names;
    }

    // ---------- Filename helpers ----------
    public static string ExtractActiveObjectFromFilename(string filename)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(stem)) return null;
        var parts = stem.Split('_');
        return parts.Length >= 1 ? parts[0] : null;
    }

    public static string ExtractHandNameFromFilename(string filename)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(stem)) return null;
        var parts = stem.Split('_');
        if (parts.Length < 2) return null;

        var token = parts[1].Trim().ToLowerInvariant();
        if (token.StartsWith("left"))  return "left_hand";
        if (token.StartsWith("right")) return "right_hand";
        return null;
    }

    // ---------- Constraints ----------
    enum AxisKind { Forward, Up, Right }

    static Vector3? AxisSim(SimCtx ctx, string name, AxisKind kind)
    {
        if (!ctx.map.TryGetValue(name, out var tr)) return null;
        Vector3 v = kind switch
        {
            AxisKind.Forward => tr.rot * Vector3.forward,
            AxisKind.Up      => tr.rot * Vector3.up,
            AxisKind.Right   => tr.rot * Vector3.right,
            _ => tr.rot * Vector3.forward
        };
        return NormalizeOrNull(v);
    }

    static Vector3? DiffToSelectedHandSim(SimCtx ctx, string objName)
    {
        if (string.IsNullOrEmpty(ctx.handName)) return null;
        if (!ctx.map.TryGetValue(objName, out var obj)) return null;
        if (!ctx.map.TryGetValue(ctx.handName, out var hand)) return null;
        return NormalizeOrNull(hand.pos - obj.pos);
    }

    static Vector3? NormalizeOrNull(Vector3 v)
    {
        if (v.sqrMagnitude < 1e-10f) return null;
        return v.normalized;
    }

    public static Dictionary<string, List<SimPair>> BuildSimConstraints()
    {
        VecGetter Fwd(string n)    => ctx => AxisSim(ctx, n, AxisKind.Forward); // ".z"
        VecGetter UpV(string n)    => ctx => AxisSim(ctx, n, AxisKind.Up);      // ".y"
        VecGetter RightV(string n) => ctx => AxisSim(ctx, n, AxisKind.Right);   // ".x"

        VecGetter WorldUp = ctx => Vector3.up;
        VecGetter ObjToSelectedHand(string n) => ctx => DiffToSelectedHandSim(ctx, n);
        VecGetter Neg(VecGetter f) => ctx => { var v = f(ctx); return v.HasValue ? (Vector3?)(-v.Value) : null; };

        return new Dictionary<string, List<SimPair>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sugar"]        = new() { new SimPair{ Source = Fwd("sugar"), Target = WorldUp } },
            ["hammer"]       = new() { new SimPair{ Source = Fwd("hammer"), Target = Neg(ObjToSelectedHand("hammer")) } },
            ["mug"]          = new() {
                                   new SimPair{ Source = UpV("mug"),    Target = WorldUp },
                                   new SimPair{ Source = RightV("mug"), Target = ObjToSelectedHand("mug") } },
            ["bowl"]         = new() { new SimPair{ Source = Fwd("bowl"),  Target = WorldUp } },
            ["banana"]       = new() { new SimPair{ Source = RightV("banana"), Target = WorldUp } },
            ["mustard"]      = new() { new SimPair{ Source = Fwd("mustard"), Target = WorldUp } },
            ["plate"]        = new() { new SimPair{ Source = Fwd("plate"), Target = WorldUp } },
            ["skillet"]      = new() {
                                   new SimPair{ Source = Fwd("skillet"), Target = WorldUp },
                                   new SimPair{ Source = UpV("skillet"), Target = ObjToSelectedHand("skillet") } },
            ["spoon"]        = new() { new SimPair{ Source = Fwd("spoon"), Target = WorldUp } },
            ["bleach"]       = new() { new SimPair{ Source = Fwd("bleach"), Target = WorldUp } },
            ["powerdrill"]   = new() { new SimPair{ Source = Fwd("powerdrill"), Target = ObjToSelectedHand("powerdrill") } },
            ["screwdriver"]  = new() { new SimPair{ Source = Fwd("screwdriver"), Target = Neg(ObjToSelectedHand("screwdriver")) } },
            ["spatula"]      = new() { new SimPair{ Source = UpV("spatula"), Target = WorldUp } },
            ["wood"]         = new() { new SimPair{ Source = Fwd("wood"), Target = WorldUp } },

            // Add more if needed, e.g.:
            // ["apple"] = new() { new SimPair{ Source = UpV("apple"), Target = WorldUp } },
        };
    }

    // ---------- Angle evaluation ----------
    public static float? ComputeAvgAngle(
        Dictionary<string, SimTrans> map,
        string activeObject,
        string handName,
        Dictionary<string, List<SimPair>> constraints)
    {
        if (string.IsNullOrEmpty(activeObject)) return null;
        if (constraints == null ||
            !constraints.TryGetValue(activeObject, out var list) ||
            list == null || list.Count == 0) return null;

        var ctx = new SimCtx { map = map, handName = handName };
        var angles = new List<float>(list.Count);

        foreach (var pair in list)
        {
            var s = pair.Source(ctx);
            var t = pair.Target(ctx);
            if (!s.HasValue || !t.HasValue) continue;

            float angle = Vector3.Angle(s.Value, t.Value);
            if (!float.IsNaN(angle) && !float.IsInfinity(angle)) angles.Add(angle);
        }

        if (angles.Count == 0) return null;
        float sum = 0f; for (int i = 0; i < angles.Count; i++) sum += angles[i];
        return sum / angles.Count;
    }
}
